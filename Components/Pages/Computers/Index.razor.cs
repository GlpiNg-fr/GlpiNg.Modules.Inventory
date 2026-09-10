using GlpiNg.Modules.Abstractions.Preferences;
using System.Security.Claims;
using System.Text.Json;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Computers;

public partial class Index : ComponentBase
{
    private enum SavedSearchesTab
    {
        Computer,
        Other
    }

    private static readonly SearchField<Computer>[] SearchFields =
    [
        SearchField<Computer>.AllFields(),
        SearchField<Computer>.Text("name", "Nom", computer => computer.Name),
        SearchField<Computer>.Text("status", "Statut", computer => computer.StatusItem?.Name),
        SearchField<Computer>.Text("manufacturer", "Fabricant", computer => computer.Manufacturer),
        SearchField<Computer>.Text("model", "Modèle", computer => computer.Model),
        SearchField<Computer>.Text("serial", "Numéro de série", computer => computer.SerialNumber),
        SearchField<Computer>.Text("chassistype", "Type", computer => computer.ChassisType),
        SearchField<Computer>.Text("os", "Système d'exploitation", computer => computer.OperatingSystem),
        SearchField<Computer>.Text("osversion", "Version de l'OS", computer => computer.OsVersion),
        SearchField<Computer>.Text("oskernel", "Version du noyau", computer => computer.OsKernelVersion),
        SearchField<Computer>.Text("uuid", "UUID matériel", computer => computer.HardwareUuid),
        SearchField<Computer>.Text("vmsystem", "Système de virtualisation", computer => computer.VmSystem),
        SearchField<Computer>.Text("site", "Site", computer => computer.Site),
        SearchField<Computer>.Text("building", "Bâtiment", computer => computer.Building),
        SearchField<Computer>.Text("room", "Salle", computer => computer.Room),
        SearchField<Computer>.Text("assigneduser", "Usager", computer => computer.AssignedUser),
        SearchField<Computer>.Text("lastloggeduser", "Dernier utilisateur connecté", computer => computer.LastLoggedUser),
        SearchField<Computer>.Number("memory", "Mémoire (Mo)", computer => computer.TotalMemoryMb),
        SearchField<Computer>.Number("battery", "Batterie (%)", computer => GetBatteryPercent(computer)),
        SearchField<Computer>.Date("lastinventory", "Dernière remontée", computer => computer.LastInventoryAt),
        SearchField<Computer>.Date("createdat", "Date de création", computer => computer.CreatedAt),
    ];

    /// <summary>Colonnes affichées par défaut (hors "name", toujours en premier) tant qu'aucune préférence utilisateur n'est enregistrée.</summary>
    private static readonly string[] DefaultColumns = ["status", "manufacturer", "model", "os", "lastinventory"];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private ComputerListStateService ListState { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    private List<Computer> _computers = [];
    private List<Computer> _filteredComputers = [];
    private List<Computer> _pagedComputers = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "name", Descending = false }];
    private readonly HashSet<int> _selectedIds = [];
    private List<string> _columns = [.. DefaultColumns];
    private string? _draggedColumn;
    private string _columnToAdd = string.Empty;
    private bool _showColumnsModal;
    private List<SavedSearch> _savedSearches = [];
    private List<SavedSearch> _otherSavedSearches = [];
    private SavedSearch? _draggedSavedSearch;
    private bool _savedSearchesOpen;
    private bool _savedSearchesPinned;
    private SavedSearchesTab _savedSearchesTab = SavedSearchesTab.Computer;
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
    private bool _showTrash;
    private bool _isRefreshing;

    private const string ItemType = "Computer";

    [SupplyParameterFromQuery(Name = "savedSearch")]
    private int? SavedSearchId { get; set; }

    private int TotalPages => _filteredComputers.Count == 0 ? 1 : (int)Math.Ceiling(_filteredComputers.Count / (double)_pageSize);

    private int ActiveCriteriaCount => _criteria.Count(criterion => criterion.Operator == "empty" || !string.IsNullOrWhiteSpace(criterion.Value));

    private List<SavedSearch> FilteredSavedSearches => string.IsNullOrWhiteSpace(_savedSearchFilter)
        ? _savedSearches
        : _savedSearches.Where(saved => saved.Name.Contains(_savedSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    private List<SavedSearch> FilteredOtherSavedSearches => string.IsNullOrWhiteSpace(_savedSearchFilter)
        ? _otherSavedSearches
        : _otherSavedSearches.Where(saved => saved.Name.Contains(_savedSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    private IEnumerable<SearchField<Computer>> AvailableColumnsToAdd =>
        SearchFields.Where(candidate => candidate.Key != SearchField.AllFieldsKey && candidate.Key != "name" && !_columns.Contains(candidate.Key));

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
        else if (SavedSearchId is null && ListState.HasState)
        {
            RestoreListState();
        }
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _computers = await db.Set<Computer>()
            .AsNoTracking()
            .Include(computer => computer.Batteries)
            .Include(computer => computer.StatusItem)
            .Where(computer => computer.IsDeleted == _showTrash)
            .OrderBy(computer => computer.Name)
            .ToListAsync();

        _selectedIds.Clear();
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

    private async Task ToggleTrashAsync(bool showTrash)
    {
        if (_showTrash == showTrash) return;

        _showTrash = showTrash;
        await LoadAsync();
    }

    private async Task ExportAsync(string format, bool allPages)
    {
        IReadOnlyList<Computer> source = allPages ? _filteredComputers : _pagedComputers;
        string scopeSuffix = allPages ? "toutes-les-pages" : "page-courante";

        (byte[] Bytes, string Extension, string ContentType) export = format switch
        {
            "pdf-landscape" => (ComputerExportWriter.BuildPdf(source, landscape: true), "pdf", "application/pdf"),
            "pdf-portrait" => (ComputerExportWriter.BuildPdf(source, landscape: false), "pdf", "application/pdf"),
            "csv" => (ComputerExportWriter.BuildCsv(source), "csv", "text/csv"),
            "ods" => (ComputerExportWriter.BuildOds(source), "ods", "application/vnd.oasis.opendocument.spreadsheet"),
            "xlsx" => (ComputerExportWriter.BuildXlsx(source), "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

        string fileName = $"ordinateurs-{scopeSuffix}.{export.Extension}";

        using MemoryStream stream = new(export.Bytes);
        using DotNetStreamReference streamRef = new(stream);
        await JS.InvokeVoidAsync("glpiNg.downloadFileFromStream", fileName, export.ContentType, streamRef);
    }

    private async Task CopyNamesToClipboardAsync()
    {
        string names = string.Join('\n', _filteredComputers.Select(computer => computer.Name));
        await JS.InvokeVoidAsync("glpiNg.copyToClipboard", names);
    }

    private static SearchField<Computer>? FindField(string key) => SearchEngine.Find(SearchFields, key);

    /// <summary>Valeurs proposées derrière « est » / « n'est pas » sur un champ texte.</summary>
    private IEnumerable<string> DistinctValues(string fieldKey) => SearchEngine.DistinctValues(_computers, SearchFields, fieldKey);

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
            ResultCount = _filteredComputers.Count,
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
        _sortCriteria.AddRange(sort is { Count: > 0 } ? sort : [new SortCriterion { Field = "name", Descending = false }]);

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
        _sortCriteria.Add(new SortCriterion { Field = "name", Descending = false });
        ApplySort();
    }

    private void ApplyFilterAndSort()
    {
        _filteredComputers = SearchEngine.Apply(_computers, SearchFields, _criteria, _sortCriteria);
        _selectedIds.IntersectWith(_filteredComputers.Select(computer => computer.Id));
        ApplyPaging();
        SaveListState();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedComputers = _filteredComputers
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    /// <summary>
    /// Vrai quand toutes les lignes visibles sont cochées. Compare la page courante et non le
    /// résultat filtré : au-delà d'une page, la case ne se cocherait jamais.
    /// </summary>
    private bool AllSelected => _pagedComputers.Count > 0
        && _selectedIds.IsSupersetOf(_pagedComputers.Select(computer => computer.Id));

    private void ToggleSelectAll(bool selectAll)
    {
        foreach (Computer computer in _pagedComputers)
        {
            if (selectAll)
            {
                _selectedIds.Add(computer.Id);
            }
            else
            {
                _selectedIds.Remove(computer.Id);
            }
        }
    }

    private void ToggleSelect(int computerId, bool selected)
    {
        if (selected)
        {
            _selectedIds.Add(computerId);
        }
        else
        {
            _selectedIds.Remove(computerId);
        }
    }

    /// <summary>
    /// Sort de la corbeille les postes sélectionnés.
    ///
    /// Sans elle, la corbeille n'avait qu'une sortie : la suppression définitive. Un poste mis au
    /// rebut par erreur était perdu avec tout son historique, alors que le rebut est justement
    /// l'étape censée être réversible.
    /// </summary>
    private async Task RestoreSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Computer> toRestore = await db.Set<Computer>()
            .Where(computer => _selectedIds.Contains(computer.Id))
            .ToListAsync();

        foreach (Computer computer in toRestore)
        {
            computer.IsDeleted = false;

            db.Set<ComputerHistoryEntry>().Add(new ComputerHistoryEntry
            {
                ComputerId = computer.Id,
                User = _currentUserName ?? "Système",
                Field = "Corbeille",
                Description = "Poste sorti de la corbeille.",
            });
        }

        await db.SaveChangesAsync();

        _selectedIds.Clear();
        await LoadAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Computer> toDelete = await db.Set<Computer>()
            .Where(computer => _selectedIds.Contains(computer.Id))
            .ToListAsync();

        if (_showTrash)
        {
            db.Set<Computer>().RemoveRange(toDelete);
        }
        else
        {
            foreach (Computer computer in toDelete)
            {
                computer.IsDeleted = true;
            }
        }

        await db.SaveChangesAsync();

        await LoadAsync();
    }

    /// <summary>Pourcentage d'usure de la batterie (capacité réelle / capacité constructeur), moyenné si plusieurs batteries. Null si aucune batterie exploitable.</summary>
    private static double? GetBatteryPercent(Computer computer)
    {
        List<double> percents = [.. computer.Batteries
            .Where(battery => battery.CapacityMwh is > 0 && battery.RealCapacityMwh is not null)
            .Select(battery => battery.RealCapacityMwh!.Value * 100.0 / battery.CapacityMwh!.Value)];

        return percents.Count > 0 ? percents.Average() : null;
    }

    /// <summary>
    /// Rendu d'une cellule de colonne dynamique. Passe par l'accesseur du champ, donc une colonne
    /// ajoutée à SearchFields devient affichable sans code supplémentaire.
    /// </summary>
    private static string CellText(Computer computer, string key)
    {
        SearchField<Computer>? field = FindField(key);
        if (field is null) return "—";

        object? raw = field.Value(computer);

        return field.Type switch
        {
            SearchFieldType.Date => raw is DateTime date
                ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : (key == "lastinventory" ? "Jamais" : "—"),
            SearchFieldType.Number => raw is not null
                ? Convert.ToDouble(raw).ToString("0.##") + (key == "battery" ? "%" : string.Empty)
                : "—",
            _ => raw?.ToString() is { Length: > 0 } text ? text : "—",
        };
    }

    private void SaveListState()
    {
        ListState.CriteriaJson = JsonSerializer.Serialize(_criteria);
        ListState.SortJson = JsonSerializer.Serialize(_sortCriteria);
        ListState.CurrentPage = _currentPage;
        ListState.PageSize = _pageSize;
        ListState.FilteredIds = _filteredComputers.Select(c => c.Id).ToList();
        ListState.HasState = true;
    }

    private void RestoreListState()
    {
        if (ListState.CriteriaJson is not null)
        {
            List<SearchCriterion>? criteria = JsonSerializer.Deserialize<List<SearchCriterion>>(ListState.CriteriaJson);
            _criteria.Clear();
            _criteria.AddRange(criteria is { Count: > 0 } ? criteria : [new SearchCriterion()]);
        }

        if (ListState.SortJson is not null)
        {
            List<SortCriterion>? sort = JsonSerializer.Deserialize<List<SortCriterion>>(ListState.SortJson);
            _sortCriteria.Clear();
            _sortCriteria.AddRange(sort is { Count: > 0 } ? sort : [new SortCriterion { Field = "name", Descending = false }]);
        }

        _pageSize = ListState.PageSize;
        _currentPage = ListState.CurrentPage;
        ApplyFilterAndSort();
    }
}
