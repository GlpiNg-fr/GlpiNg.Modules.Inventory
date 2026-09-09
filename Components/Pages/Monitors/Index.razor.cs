using GlpiNg.Modules.Abstractions.Preferences;
using System.Security.Claims;
using System.Text.Json;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Monitors;

public partial class Index : ComponentBase
{
    private enum SavedSearchesTab
    {
        Monitor,
        Other
    }

    private sealed record MonitorRow(int Id, int ComputerId, string ComputerName, string Designation, string? Manufacturer, string? Serial, string? Status);

    private static readonly SearchField<MonitorRow>[] SearchFields =
    [
        SearchField<MonitorRow>.AllFields(),
        SearchField<MonitorRow>.Text("designation", "Désignation", monitor => monitor.Designation),
        SearchField<MonitorRow>.Text("status", "Statut", monitor => monitor.Status),
        SearchField<MonitorRow>.Text("manufacturer", "Fabricant", monitor => monitor.Manufacturer),
        SearchField<MonitorRow>.Text("serial", "Numéro de série", monitor => monitor.Serial),
        SearchField<MonitorRow>.Text("computername", "Poste associé", monitor => monitor.ComputerName),
    ];

    private static readonly string[] DefaultColumns = ["manufacturer", "serial", "computername"];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    private List<MonitorRow> _monitors = [];
    private List<MonitorRow> _filteredMonitors = [];
    private List<MonitorRow> _pagedMonitors = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "designation", Descending = false }];
    private List<string> _columns = [.. DefaultColumns];
    private string? _draggedColumn;
    private string _columnToAdd = string.Empty;
    private bool _showColumnsModal;
    private List<SavedSearch> _savedSearches = [];
    private List<SavedSearch> _otherSavedSearches = [];
    private SavedSearch? _draggedSavedSearch;
    private bool _savedSearchesOpen;
    private bool _savedSearchesPinned;
    private SavedSearchesTab _savedSearchesTab = SavedSearchesTab.Monitor;
    private string _savedSearchFilter = string.Empty;
    private string _newSavedSearchName = string.Empty;
    private bool _newSavedSearchIsPublic;
    private bool _showSaveSearchModal;
    private int? _currentUserId;
    private string? _currentUserName;
    // Taille de page par défaut du compte connecté (page /preferences). Injecté par l'hôte, qui
    // seul connaît le modèle d'utilisateur — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private int _pageSize = 25;
    private int _currentPage = 1;
    private bool _isRefreshing;

    private const string ItemType = "Monitor";

    [SupplyParameterFromQuery(Name = "savedSearch")]
    private int? SavedSearchId { get; set; }

    private int TotalPages => _filteredMonitors.Count == 0 ? 1 : (int)Math.Ceiling(_filteredMonitors.Count / (double)_pageSize);

    private int ActiveCriteriaCount => _criteria.Count(criterion => criterion.Operator == "empty" || !string.IsNullOrWhiteSpace(criterion.Value));

    private List<SavedSearch> FilteredSavedSearches => string.IsNullOrWhiteSpace(_savedSearchFilter)
        ? _savedSearches
        : _savedSearches.Where(saved => saved.Name.Contains(_savedSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    private List<SavedSearch> FilteredOtherSavedSearches => string.IsNullOrWhiteSpace(_savedSearchFilter)
        ? _otherSavedSearches
        : _otherSavedSearches.Where(saved => saved.Name.Contains(_savedSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    private IEnumerable<SearchField<MonitorRow>> AvailableColumnsToAdd =>
        SearchFields.Where(candidate => candidate.Key != SearchField.AllFieldsKey && candidate.Key != "designation" && !_columns.Contains(candidate.Key));

    protected override async Task OnInitializedAsync()
    {
        _pageSize = (await UserPreferences.GetAsync()).ItemsPerPage;

        if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            string? userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _currentUserId = int.TryParse(userIdClaim, out int userId) ? userId : null;
            _currentUserName = authState.User.FindFirst(ClaimTypes.Name)?.Value;
        }

        await LoadAsync();
        await LoadSavedSearchesAsync();
        await LoadColumnPreferenceAsync();

        SavedSearch? searchToApply = SavedSearchId is { } requestedId
            ? _savedSearches.FirstOrDefault(saved => saved.Id == requestedId)
            : _savedSearches.FirstOrDefault(saved => saved.IsDefault && saved.OwnerUserId == _currentUserId);

        if (searchToApply is not null)
        {
            ApplySavedSearch(searchToApply);
        }
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _monitors = await db.Set<ComputerPeripheral>()
            .AsNoTracking()
            .Where(peripheral => peripheral.Kind == PeripheralKind.Monitor)
            .Join(db.Set<Computer>().AsNoTracking(),
                peripheral => peripheral.ComputerId,
                computer => computer.Id,
                (peripheral, computer) => new { peripheral, computer })
            .OrderBy(joined => joined.peripheral.Designation)
            .Select(joined => new MonitorRow(joined.peripheral.Id, joined.computer.Id, joined.computer.Name, joined.peripheral.Designation, joined.peripheral.Manufacturer, joined.peripheral.Serial,
                joined.peripheral.StatusItem != null ? joined.peripheral.StatusItem.Name : null))
            .ToListAsync();

        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private async Task OnRefreshAsync()
    {
        if (_isRefreshing) return;

        _isRefreshing = true;
        StateHasChanged();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static SearchField<MonitorRow>? FindField(string key) => SearchEngine.Find(SearchFields, key);

    /// <summary>Valeurs proposées derrière « est » / « n'est pas » sur un champ texte.</summary>
    private IEnumerable<string> DistinctValues(string fieldKey) => SearchEngine.DistinctValues(_monitors, SearchFields, fieldKey);

    private void ApplySearch()
    {
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void ResetSearch()
    {
        _criteria.Clear();
        _criteria.Add(new SearchCriterion());
        ApplySearch();
    }

    private async Task LoadSavedSearchesAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<SavedSearch> visible = await db.Set<SavedSearch>()
            .AsNoTracking()
            .Where(saved => saved.OwnerUserId == _currentUserId || saved.IsPublic)
            .OrderBy(saved => saved.Name)
            .ToListAsync();

        Dictionary<int, int> order = _currentUserId is { } userId
            ? await db.Set<SavedSearchOrder>()
                .AsNoTracking()
                .Where(o => o.UserId == userId)
                .ToDictionaryAsync(o => o.SavedSearchId, o => o.Position)
            : [];

        _savedSearches = visible.Where(saved => saved.ItemType == ItemType)
            .OrderBy(saved => order.TryGetValue(saved.Id, out int position) ? position : int.MaxValue)
            .ThenBy(saved => saved.Name)
            .ToList();
        _otherSavedSearches = visible.Where(saved => saved.ItemType != ItemType).ToList();
    }

    private void OnSavedSearchDragStart(SavedSearch saved)
    {
        _draggedSavedSearch = saved;
    }

    private async Task OnSavedSearchDropAsync(SavedSearch target)
    {
        if (_draggedSavedSearch is null || ReferenceEquals(_draggedSavedSearch, target)) return;

        _savedSearches.Remove(_draggedSavedSearch);
        int targetIndex = _savedSearches.IndexOf(target);
        _savedSearches.Insert(targetIndex, _draggedSavedSearch);
        _draggedSavedSearch = null;

        await SaveSavedSearchOrderAsync();
    }

    private async Task SaveSavedSearchOrderAsync()
    {
        if (_currentUserId is not { } userId) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<SavedSearchOrder> existing = await db.Set<SavedSearchOrder>()
            .Where(o => o.UserId == userId)
            .ToListAsync();

        for (int position = 0; position < _savedSearches.Count; position++)
        {
            int savedSearchId = _savedSearches[position].Id;
            SavedSearchOrder? entry = existing.FirstOrDefault(o => o.SavedSearchId == savedSearchId);

            if (entry is null)
            {
                db.Set<SavedSearchOrder>().Add(new SavedSearchOrder { UserId = userId, SavedSearchId = savedSearchId, Position = position });
            }
            else
            {
                entry.Position = position;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task LoadColumnPreferenceAsync()
    {
        if (_currentUserId is not { } userId)
        {
            _columns = [.. DefaultColumns];
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        TableColumnPreference? preference = await db.Set<TableColumnPreference>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ItemType == ItemType);

        _columns = preference is not null
            ? JsonSerializer.Deserialize<List<string>>(preference.ColumnsJson) is { Count: > 0 } saved ? saved : [.. DefaultColumns]
            : [.. DefaultColumns];
    }

    private async Task SaveColumnPreferenceAsync()
    {
        if (_currentUserId is not { } userId) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        TableColumnPreference? preference = await db.Set<TableColumnPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ItemType == ItemType);

        string json = JsonSerializer.Serialize(_columns);

        if (preference is null)
        {
            db.Set<TableColumnPreference>().Add(new TableColumnPreference { UserId = userId, ItemType = ItemType, ColumnsJson = json });
        }
        else
        {
            preference.ColumnsJson = json;
        }

        await db.SaveChangesAsync();
    }

    private async Task AddColumnAsync()
    {
        if (string.IsNullOrEmpty(_columnToAdd) || _columns.Contains(_columnToAdd)) return;

        _columns.Add(_columnToAdd);
        _columnToAdd = string.Empty;
        await SaveColumnPreferenceAsync();
    }

    private async Task RemoveColumnAsync(string columnKey)
    {
        _columns.Remove(columnKey);
        await SaveColumnPreferenceAsync();
    }

    private void OnColumnDragStart(string columnKey)
    {
        _draggedColumn = columnKey;
    }

    private async Task OnColumnDropAsync(string targetColumnKey)
    {
        if (_draggedColumn is null || _draggedColumn == targetColumnKey) return;

        _columns.Remove(_draggedColumn);
        int targetIndex = _columns.IndexOf(targetColumnKey);
        _columns.Insert(targetIndex, _draggedColumn);
        _draggedColumn = null;

        await SaveColumnPreferenceAsync();
    }

    private async Task SaveCurrentSearchAsync()
    {
        string name = _newSavedSearchName.Trim();
        if (name.Length == 0) return;

        SavedSearch saved = new()
        {
            Name = name,
            ItemType = ItemType,
            OwnerUserId = _currentUserId,
            OwnerName = _currentUserName,
            IsPublic = _newSavedSearchIsPublic,
            ResultCount = _filteredMonitors.Count,
            CriteriaJson = JsonSerializer.Serialize(_criteria),
            SortJson = JsonSerializer.Serialize(_sortCriteria)
        };

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<SavedSearch>().Add(saved);
        await db.SaveChangesAsync();

        _newSavedSearchName = string.Empty;
        _newSavedSearchIsPublic = false;
        _showSaveSearchModal = false;
        await LoadSavedSearchesAsync();
    }

    private void ApplySavedSearch(SavedSearch saved)
    {
        List<SearchCriterion>? criteria = JsonSerializer.Deserialize<List<SearchCriterion>>(saved.CriteriaJson);
        _criteria.Clear();
        _criteria.AddRange(criteria is { Count: > 0 } ? criteria : [new SearchCriterion()]);

        List<SortCriterion>? sort = JsonSerializer.Deserialize<List<SortCriterion>>(saved.SortJson);
        _sortCriteria.Clear();
        _sortCriteria.AddRange(sort is { Count: > 0 } ? sort : [new SortCriterion { Field = "designation", Descending = false }]);

        ApplySearch();

        if (!_savedSearchesPinned)
        {
            _savedSearchesOpen = false;
        }
    }

    private async Task SetDefaultSavedSearchAsync(SavedSearch saved)
    {
        if (saved.OwnerUserId != _currentUserId) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<SavedSearch> ownSearches = await db.Set<SavedSearch>()
            .Where(s => s.OwnerUserId == _currentUserId && s.ItemType == ItemType)
            .ToListAsync();

        foreach (SavedSearch ownSearch in ownSearches)
        {
            ownSearch.IsDefault = ownSearch.Id == saved.Id;
        }

        await db.SaveChangesAsync();
        await LoadSavedSearchesAsync();
    }

    private void SetPageSize(int pageSize)
    {
        if (_pageSize == pageSize) return;

        _pageSize = pageSize;
        _currentPage = 1;
        ApplyPaging();
    }

    private void GoToPage(int page)
    {
        int targetPage = Math.Clamp(page, 1, TotalPages);
        if (targetPage == _currentPage) return;

        _currentPage = targetPage;
        ApplyPaging();
    }

    private void SetSort(string field, bool additive)
    {
        SortCriterion? existing = _sortCriteria.FirstOrDefault(criterion => criterion.Field == field);

        if (additive)
        {
            if (existing is not null)
            {
                existing.Descending = !existing.Descending;
            }
            else
            {
                _sortCriteria.Add(new SortCriterion { Field = field, Descending = false });
            }
        }
        else if (_sortCriteria.Count == 1 && existing is not null)
        {
            existing.Descending = !existing.Descending;
        }
        else
        {
            _sortCriteria.Clear();
            _sortCriteria.Add(new SortCriterion { Field = field, Descending = false });
        }

        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private SortCriterion? GetSortCriterion(string field)
    {
        return _sortCriteria.FirstOrDefault(criterion => criterion.Field == field);
    }

    private void AddSortCriterion()
    {
        string[] sortableKeys = SearchFields.Where(field => field.Key != "all").Select(field => field.Key).ToArray();
        if (_sortCriteria.Count >= sortableKeys.Length) return;

        string nextField = sortableKeys.First(key => _sortCriteria.All(criterion => criterion.Field != key));
        _sortCriteria.Add(new SortCriterion { Field = nextField, Descending = false });
    }

    private void RemoveSortCriterion(SortCriterion criterion)
    {
        _sortCriteria.Remove(criterion);
        ApplySort();
    }

    private void ApplySort()
    {
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void ResetSort()
    {
        _sortCriteria.Clear();
        _sortCriteria.Add(new SortCriterion { Field = "designation", Descending = false });
        ApplySort();
    }

    private void ApplyFilterAndSort()
    {
        _filteredMonitors = SearchEngine.Apply(_monitors, SearchFields, _criteria, _sortCriteria);
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedMonitors = _filteredMonitors
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    private static string CellText(MonitorRow monitor, string key) =>
        FindField(key)?.Value(monitor)?.ToString() is { Length: > 0 } text ? text : "—";
}
