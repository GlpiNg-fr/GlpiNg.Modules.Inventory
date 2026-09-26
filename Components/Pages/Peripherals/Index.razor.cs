using GlpiNg.Modules.Abstractions.FieldUnicity;
using GlpiNg.Modules.Abstractions.Items;
using GlpiNg.Modules.Abstractions.Preferences;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Peripherals;

public partial class Index : ComponentBase
{
    private enum SortField
    {
        Name,
        Status,
        Manufacturer,
        Location,
        Type,
        Model,
        UpdatedAt,
        AssignedUser
    }

    private enum SearchFieldType
    {
        Text,
        Date
    }

    private sealed record SearchFieldDefinition(string Key, string Label, SearchFieldType Type);

    private sealed class SearchCriterion
    {
        public string Link { get; set; } = "AND";
        public string FieldKey { get; set; } = "all";
        public string Operator { get; set; } = "contains";
        public string Value { get; set; } = string.Empty;
    }

    private sealed class SortCriterion
    {
        public SortField Field { get; set; }
        public bool Descending { get; set; }
    }

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
        new("status", "Statut", SearchFieldType.Text),
        new("type", "Type", SearchFieldType.Text),
        new("manufacturer", "Fabricant", SearchFieldType.Text),
        new("model", "Modèle", SearchFieldType.Text),
        new("brand", "Marque", SearchFieldType.Text),
        new("serial", "Numéro de série", SearchFieldType.Text),
        new("inventorynumber", "Numéro d'inventaire", SearchFieldType.Text),
        new("site", "Site", SearchFieldType.Text),
        new("building", "Bâtiment", SearchFieldType.Text),
        new("room", "Salle", SearchFieldType.Text),
        new("technician", "Technicien responsable", SearchFieldType.Text),
        new("assigneduser", "Usager", SearchFieldType.Text),
        new("uuid", "UUID", SearchFieldType.Text),
        new("createdat", "Date de création", SearchFieldType.Date)
    ];

    private static readonly (SortField Field, string Label)[] SortableColumns =
    [
        (SortField.Name, "Nom"),
        (SortField.Status, "Statut"),
        (SortField.Manufacturer, "Fabricant"),
        (SortField.Location, "Lieu"),
        (SortField.Type, "Type"),
        (SortField.Model, "Modèle"),
        (SortField.UpdatedAt, "Dernière modification"),
        (SortField.AssignedUser, "Usager")
    ];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IFieldUnicityChecker FieldUnicity { get; set; } = null!;

    /// <summary>Refus d'un critère d'unicité des champs, affiché dans la fenêtre de création.</summary>
    private string? _createError;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    private List<Peripheral> _peripherals = [];
    private List<Peripheral> _filteredPeripherals = [];
    private List<Peripheral> _pagedPeripherals = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = SortField.Name, Descending = false }];
    private readonly HashSet<int> _selectedIds = [];
    // Taille de page par défaut du compte connecté (page /preferences). Injecté par l'hôte, qui
    // seul connaît le modèle d'utilisateur — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private int _pageSize = 25;
    private int _currentPage = 1;
    private Peripheral _newPeripheral = NewBlankPeripheral();
    private List<DropdownItem> _statusOptions = [];

    private bool AllSelected => _pagedPeripherals.Count > 0 && _selectedIds.IsSupersetOf(_pagedPeripherals.Select(peripheral => peripheral.Id));

    private int TotalPages => _filteredPeripherals.Count == 0 ? 1 : (int)Math.Ceiling(_filteredPeripherals.Count / (double)_pageSize);

    private int ActiveCriteriaCount => _criteria.Count(criterion => criterion.Operator == "empty" || !string.IsNullOrWhiteSpace(criterion.Value));

    protected override async Task OnInitializedAsync()
    {
        _pageSize = (await UserPreferences.GetAsync()).ItemsPerPage;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _peripherals = await db.Set<Peripheral>()
            .AsNoTracking()
            .Include(peripheral => peripheral.StatusItem)
            .OrderBy(peripheral => peripheral.Name)
            .ToListAsync();

        _statusOptions = await db.Set<DropdownItem>()
            .AsNoTracking()
            .Where(i => i.Type == DropdownType.Status)
            .OrderBy(i => i.Name)
            .ToListAsync();

        _selectedIds.Clear();
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private async Task OnRefreshAsync()
    {
        await LoadAsync();
    }

    private static SearchFieldDefinition? FindField(string key)
    {
        return SearchFields.FirstOrDefault(field => field.Key == key);
    }

    private List<string> GetDistinctTextValues(string fieldKey) =>
        _peripherals.Select(peripheral => GetFieldText(peripheral, fieldKey))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    private void SetSort(SortField field, bool additive)
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

    private SortCriterion? GetSortCriterion(SortField field)
    {
        return _sortCriteria.FirstOrDefault(criterion => criterion.Field == field);
    }

    private void ApplyFilterAndSort()
    {
        List<Peripheral> matched = _peripherals;
        bool first = true;

        foreach (SearchCriterion criterion in _criteria)
        {
            if (criterion.Operator != "empty" && string.IsNullOrWhiteSpace(criterion.Value)) continue;

            List<Peripheral> criterionMatches = _peripherals.Where(peripheral => EvaluateCriterion(peripheral, criterion)).ToList();

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

        IOrderedEnumerable<Peripheral>? ordered = null;
        foreach (SortCriterion criterion in _sortCriteria)
        {
            ordered = ordered is null
                ? (criterion.Descending
                    ? matched.OrderByDescending(peripheral => GetSortKey(peripheral, criterion.Field))
                    : matched.OrderBy(peripheral => GetSortKey(peripheral, criterion.Field)))
                : (criterion.Descending
                    ? ordered.ThenByDescending(peripheral => GetSortKey(peripheral, criterion.Field))
                    : ordered.ThenBy(peripheral => GetSortKey(peripheral, criterion.Field)));
        }

        _filteredPeripherals = (ordered ?? matched.AsEnumerable()).ToList();
        _selectedIds.IntersectWith(_filteredPeripherals.Select(peripheral => peripheral.Id));
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedPeripherals = _filteredPeripherals
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    private static IComparable GetSortKey(Peripheral peripheral, SortField field) => field switch
    {
        SortField.Status => peripheral.StatusItem?.Name ?? string.Empty,
        SortField.Manufacturer => peripheral.Manufacturer ?? string.Empty,
        SortField.Location => LocationLabel(peripheral) ?? string.Empty,
        SortField.Type => peripheral.Type ?? string.Empty,
        SortField.Model => peripheral.Model ?? string.Empty,
        SortField.UpdatedAt => peripheral.UpdatedAt ?? peripheral.CreatedAt,
        SortField.AssignedUser => peripheral.AssignedUser ?? string.Empty,
        _ => peripheral.Name
    };

    private void ToggleSelectAll(bool selectAll)
    {
        foreach (Peripheral peripheral in _pagedPeripherals)
        {
            if (selectAll)
            {
                _selectedIds.Add(peripheral.Id);
            }
            else
            {
                _selectedIds.Remove(peripheral.Id);
            }
        }
    }

    private void ToggleSelect(int peripheralId, bool selected)
    {
        if (selected)
        {
            _selectedIds.Add(peripheralId);
        }
        else
        {
            _selectedIds.Remove(peripheralId);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Peripheral> toDelete = await db.Set<Peripheral>()
            .Where(peripheral => _selectedIds.Contains(peripheral.Id))
            .ToListAsync();

        db.Set<Peripheral>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreatePeripheralAsync()
    {
        if (string.IsNullOrWhiteSpace(_newPeripheral.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        // Unicité des champs (Configuration > Unicité des champs) : un critère peut refuser la
        // création d'un doublon. La fenêtre reste ouverte avec la saisie, pour la corriger.
        FieldUnicityVerdict verdict = await FieldUnicity.CheckAsync(ItemTypes.Peripheral, db.Set<Peripheral>(), _newPeripheral);
        if (verdict.Refused)
        {
            _createError = verdict.Message;
            return;
        }

        _createError = null;
        db.Set<Peripheral>().Add(_newPeripheral);
        await db.SaveChangesAsync();

        _newPeripheral = NewBlankPeripheral();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newPeripheralModal");
        await LoadAsync();
    }

    private static Peripheral NewBlankPeripheral() => new() { Name = string.Empty };

    private static bool EvaluateCriterion(Peripheral peripheral, SearchCriterion criterion)
    {
        SearchFieldDefinition? field = FindField(criterion.FieldKey);
        if (field is null) return true;

        if (field.Key == "all")
        {
            string term = criterion.Value.Trim();
            if (term.Length == 0) return true;

            bool anyMatch = GetAllFieldsText(peripheral).Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
            return criterion.Operator == "notcontains" ? !anyMatch : anyMatch;
        }

        return field.Type switch
        {
            SearchFieldType.Text => EvaluateText(GetFieldText(peripheral, field.Key), criterion),
            SearchFieldType.Date => EvaluateDate(GetFieldDate(peripheral, field.Key), criterion),
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

    private static IEnumerable<string> GetAllFieldsText(Peripheral peripheral)
    {
        if (!string.IsNullOrWhiteSpace(peripheral.Name)) yield return peripheral.Name;
        if (!string.IsNullOrWhiteSpace(peripheral.Type)) yield return peripheral.Type;
        if (!string.IsNullOrWhiteSpace(peripheral.Manufacturer)) yield return peripheral.Manufacturer;
        if (!string.IsNullOrWhiteSpace(peripheral.Model)) yield return peripheral.Model;
        if (!string.IsNullOrWhiteSpace(peripheral.SerialNumber)) yield return peripheral.SerialNumber;
        if (!string.IsNullOrWhiteSpace(peripheral.AssignedUser)) yield return peripheral.AssignedUser;
    }

    private static string? GetFieldText(Peripheral peripheral, string key) => key switch
    {
        "name" => peripheral.Name,
        "status" => peripheral.StatusItem?.Name,
        "type" => peripheral.Type,
        "manufacturer" => peripheral.Manufacturer,
        "model" => peripheral.Model,
        "brand" => peripheral.Brand,
        "serial" => peripheral.SerialNumber,
        "inventorynumber" => peripheral.InventoryNumber,
        "site" => peripheral.Site,
        "building" => peripheral.Building,
        "room" => peripheral.Room,
        "technician" => peripheral.TechnicianInCharge,
        "assigneduser" => peripheral.AssignedUser,
        "uuid" => peripheral.Uuid,
        _ => null
    };

    private static DateTime? GetFieldDate(Peripheral peripheral, string key) => key switch
    {
        "createdat" => peripheral.CreatedAt,
        _ => null
    };

    private static string? LocationLabel(Peripheral peripheral)
    {
        var parts = new[] { peripheral.Site, peripheral.Building, peripheral.Room }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(" > ", parts);
        return label.Length > 0 ? label : null;
    }

    private string LastModifiedLabel(Peripheral peripheral)
    {
        DateTime lastModified = peripheral.UpdatedAt ?? peripheral.CreatedAt;
        return Display.DateTime(lastModified)!;
    }

}
