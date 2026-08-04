using System.Security.Claims;
using System.Text.Json;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Computers;

public partial class Index : ComponentBase
{
    private enum SearchFieldType
    {
        Text,
        Select,
        Number,
        Date
    }

    private enum SavedSearchesTab
    {
        Computer,
        Other
    }

    private sealed record SearchFieldDefinition(string Key, string Label, SearchFieldType Type, (string Value, string Label)[]? Options = null);

    private sealed class SearchCriterion
    {
        public string Link { get; set; } = "AND";
        public string FieldKey { get; set; } = "all";
        public string Operator { get; set; } = "contains";
        public string Value { get; set; } = string.Empty;
    }

    private sealed class SortCriterion
    {
        public string Field { get; set; } = "name";
        public bool Descending { get; set; }
    }

    private static readonly (string Value, string Label)[] StatusOptions =
    [
        (nameof(ComputerStatus.InStock), "En stock"),
        (nameof(ComputerStatus.InProduction), "En production"),
        (nameof(ComputerStatus.Broken), "En panne"),
        (nameof(ComputerStatus.Retired), "Réformé")
    ];

    private static readonly Dictionary<SearchFieldType, (string Value, string Label)[]> OperatorsByType = new()
    {
        [SearchFieldType.Text] =
        [
            ("contains", "contient"),
            ("notcontains", "ne contient pas"),
            ("equals", "est"),
            ("notequals", "n'est pas"),
            ("empty", "est vide")
        ],
        [SearchFieldType.Select] =
        [
            ("equals", "est"),
            ("notequals", "n'est pas")
        ],
        [SearchFieldType.Number] =
        [
            ("equals", "est"),
            ("notequals", "n'est pas"),
            ("greaterthan", "supérieur à"),
            ("lessthan", "inférieur à"),
            ("empty", "est vide")
        ],
        [SearchFieldType.Date] =
        [
            ("equals", "est"),
            ("before", "avant le"),
            ("after", "après le"),
            ("empty", "est vide")
        ]
    };

    private static readonly SearchFieldDefinition[] SearchFields =
    [
        new("all", "Tous les champs", SearchFieldType.Text),
        new("name", "Nom", SearchFieldType.Text),
        new("status", "Statut", SearchFieldType.Select, StatusOptions),
        new("manufacturer", "Fabricant", SearchFieldType.Text),
        new("model", "Modèle", SearchFieldType.Text),
        new("serial", "Numéro de série", SearchFieldType.Text),
        new("chassistype", "Type", SearchFieldType.Text),
        new("os", "Système d'exploitation", SearchFieldType.Text),
        new("osversion", "Version de l'OS", SearchFieldType.Text),
        new("oskernel", "Version du noyau", SearchFieldType.Text),
        new("uuid", "UUID matériel", SearchFieldType.Text),
        new("vmsystem", "Système de virtualisation", SearchFieldType.Text),
        new("site", "Site", SearchFieldType.Text),
        new("building", "Bâtiment", SearchFieldType.Text),
        new("room", "Salle", SearchFieldType.Text),
        new("assigneduser", "Usager", SearchFieldType.Text),
        new("lastloggeduser", "Dernier utilisateur connecté", SearchFieldType.Text),
        new("memory", "Mémoire (Mo)", SearchFieldType.Number),
        new("lastinventory", "Dernière remontée", SearchFieldType.Date),
        new("createdat", "Date de création", SearchFieldType.Date)
    ];

    /// <summary>Colonnes affichées par défaut (hors "name", toujours en premier) tant qu'aucune préférence utilisateur n'est enregistrée.</summary>
    private static readonly string[] DefaultColumns = ["status", "manufacturer", "model", "os", "lastinventory"];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

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

    private IEnumerable<SearchFieldDefinition> AvailableColumnsToAdd =>
        SearchFields.Where(field => field.Key != "all" && field.Key != "name" && !_columns.Contains(field.Key));

    protected override async Task OnInitializedAsync()
    {
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
        _computers = await db.Set<Computer>()
            .AsNoTracking()
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

    private static SearchFieldDefinition? FindField(string key)
    {
        return SearchFields.FirstOrDefault(field => field.Key == key);
    }

    private static (string Value, string Label)[] GetOperators(SearchFieldDefinition? field)
    {
        if (field is null) return [];
        if (field.Key == "all") return [("contains", "contient"), ("notcontains", "ne contient pas")];
        return OperatorsByType[field.Type];
    }

    private void OnCriterionFieldChanged(SearchCriterion criterion, string fieldKey)
    {
        criterion.FieldKey = fieldKey;
        (string Value, string Label)[] operators = GetOperators(FindField(fieldKey));
        criterion.Operator = operators.Length > 0 ? operators[0].Value : "contains";
        criterion.Value = string.Empty;
    }

    private void AddCriterion()
    {
        _criteria.Add(new SearchCriterion());
    }

    private void RemoveCriterion(SearchCriterion criterion)
    {
        if (_criteria.Count <= 1) return;
        _criteria.Remove(criterion);
        ApplySearch();
    }

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
        List<Computer> matched = _computers;
        bool first = true;

        foreach (SearchCriterion criterion in _criteria)
        {
            if (criterion.Operator != "empty" && string.IsNullOrWhiteSpace(criterion.Value)) continue;

            List<Computer> criterionMatches = _computers.Where(computer => EvaluateCriterion(computer, criterion)).ToList();

            if (first)
            {
                matched = criterionMatches;
                first = false;
            }
            else if (criterion.Link == "OR")
            {
                matched = matched.Union(criterionMatches).ToList();
            }
            else
            {
                matched = matched.Intersect(criterionMatches).ToList();
            }
        }

        IOrderedEnumerable<Computer>? ordered = null;
        foreach (SortCriterion criterion in _sortCriteria)
        {
            ordered = ordered is null
                ? (criterion.Descending
                    ? matched.OrderByDescending(computer => GetSortKey(computer, criterion.Field))
                    : matched.OrderBy(computer => GetSortKey(computer, criterion.Field)))
                : (criterion.Descending
                    ? ordered.ThenByDescending(computer => GetSortKey(computer, criterion.Field))
                    : ordered.ThenBy(computer => GetSortKey(computer, criterion.Field)));
        }

        _filteredComputers = (ordered ?? matched.AsEnumerable()).ToList();
        _selectedIds.IntersectWith(_filteredComputers.Select(computer => computer.Id));
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedComputers = _filteredComputers
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    private static IComparable GetSortKey(Computer computer, string key)
    {
        if (key == "status") return (int)computer.Status;

        SearchFieldDefinition? field = FindField(key);
        if (field is null) return computer.Name;

        return field.Type switch
        {
            SearchFieldType.Number => GetFieldNumber(computer, key) ?? double.MinValue,
            SearchFieldType.Date => GetFieldDate(computer, key) ?? DateTime.MinValue,
            SearchFieldType.Select => GetFieldSelectValue(computer, key),
            _ => GetFieldText(computer, key) ?? string.Empty
        };
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

    private static bool EvaluateCriterion(Computer computer, SearchCriterion criterion)
    {
        SearchFieldDefinition? field = FindField(criterion.FieldKey);
        if (field is null) return true;

        if (field.Key == "all")
        {
            string term = criterion.Value.Trim();
            if (term.Length == 0) return true;

            bool anyMatch = GetAllFieldsText(computer).Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
            return criterion.Operator == "notcontains" ? !anyMatch : anyMatch;
        }

        return field.Type switch
        {
            SearchFieldType.Text => EvaluateText(GetFieldText(computer, field.Key), criterion),
            SearchFieldType.Select => EvaluateSelect(GetFieldSelectValue(computer, field.Key), criterion),
            SearchFieldType.Number => EvaluateNumber(GetFieldNumber(computer, field.Key), criterion),
            SearchFieldType.Date => EvaluateDate(GetFieldDate(computer, field.Key), criterion),
            _ => true
        };
    }

    private static bool EvaluateText(string? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == "empty") return string.IsNullOrWhiteSpace(raw);

        string value = raw ?? string.Empty;
        string term = criterion.Value.Trim();
        return criterion.Operator switch
        {
            "contains" => value.Contains(term, StringComparison.OrdinalIgnoreCase),
            "notcontains" => !value.Contains(term, StringComparison.OrdinalIgnoreCase),
            "equals" => string.Equals(value, term, StringComparison.OrdinalIgnoreCase),
            "notequals" => !string.Equals(value, term, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool EvaluateSelect(string current, SearchCriterion criterion)
    {
        bool isEqual = string.Equals(current, criterion.Value, StringComparison.Ordinal);
        return criterion.Operator == "notequals" ? !isEqual : isEqual;
    }

    private static bool EvaluateNumber(double? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == "empty") return raw is null;
        if (raw is null) return false;
        if (!double.TryParse(criterion.Value, out double target)) return true;

        return criterion.Operator switch
        {
            "equals" => raw.Value == target,
            "notequals" => raw.Value != target,
            "greaterthan" => raw.Value > target,
            "lessthan" => raw.Value < target,
            _ => true
        };
    }

    private static bool EvaluateDate(DateTime? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == "empty") return raw is null;
        if (raw is null) return false;
        if (!DateTime.TryParse(criterion.Value, out DateTime target)) return true;

        DateTime rawDate = raw.Value.Date;
        DateTime targetDate = target.Date;
        return criterion.Operator switch
        {
            "equals" => rawDate == targetDate,
            "before" => rawDate < targetDate,
            "after" => rawDate > targetDate,
            _ => true
        };
    }

    private static IEnumerable<string> GetAllFieldsText(Computer computer)
    {
        if (!string.IsNullOrWhiteSpace(computer.Name)) yield return computer.Name;
        if (!string.IsNullOrWhiteSpace(computer.Manufacturer)) yield return computer.Manufacturer;
        if (!string.IsNullOrWhiteSpace(computer.Model)) yield return computer.Model;
        if (!string.IsNullOrWhiteSpace(computer.OperatingSystem)) yield return computer.OperatingSystem;
        if (!string.IsNullOrWhiteSpace(computer.SerialNumber)) yield return computer.SerialNumber;
    }

    private static string? GetFieldText(Computer computer, string key) => key switch
    {
        "name" => computer.Name,
        "manufacturer" => computer.Manufacturer,
        "model" => computer.Model,
        "serial" => computer.SerialNumber,
        "chassistype" => computer.ChassisType,
        "os" => computer.OperatingSystem,
        "osversion" => computer.OsVersion,
        "oskernel" => computer.OsKernelVersion,
        "uuid" => computer.HardwareUuid,
        "vmsystem" => computer.VmSystem,
        "site" => computer.Site,
        "building" => computer.Building,
        "room" => computer.Room,
        "assigneduser" => computer.AssignedUser,
        "lastloggeduser" => computer.LastLoggedUser,
        _ => null
    };

    private static string GetFieldSelectValue(Computer computer, string key) => key switch
    {
        "status" => computer.Status.ToString(),
        _ => string.Empty
    };

    private static double? GetFieldNumber(Computer computer, string key) => key switch
    {
        "memory" => computer.TotalMemoryMb,
        _ => null
    };

    private static DateTime? GetFieldDate(Computer computer, string key) => key switch
    {
        "lastinventory" => computer.LastInventoryAt,
        "createdat" => computer.CreatedAt,
        _ => null
    };

    private static string CellText(Computer computer, string key)
    {
        SearchFieldDefinition? field = FindField(key);
        if (field is null) return "—";

        return field.Type switch
        {
            SearchFieldType.Date => GetFieldDate(computer, key) is { } date
                ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : (key == "lastinventory" ? "Jamais" : "—"),
            SearchFieldType.Number => GetFieldNumber(computer, key) is { } number ? number.ToString("0.##") : "—",
            _ => GetFieldText(computer, key) is { Length: > 0 } text ? text : "—"
        };
    }

    private static string StatusLabel(ComputerStatus status) => status switch
    {
        ComputerStatus.InStock => "En stock",
        ComputerStatus.InProduction => "En production",
        ComputerStatus.Broken => "En panne",
        ComputerStatus.Retired => "Réformé",
        _ => status.ToString()
    };

    private static string StatusCssClass(ComputerStatus status) => status switch
    {
        ComputerStatus.InStock => "glpi-status-instock",
        ComputerStatus.InProduction => "glpi-status-inproduction",
        ComputerStatus.Broken => "glpi-status-broken",
        ComputerStatus.Retired => "glpi-status-retired",
        _ => "bg-secondary"
    };
}
