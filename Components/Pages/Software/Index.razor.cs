using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Software;

public partial class Index : ComponentBase
{
    private enum SearchFieldType
    {
        Text,
        Number
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
        public string Field { get; set; } = "name";
        public bool Descending { get; set; }
    }

    private sealed record SoftwareInstall(int ComputerId, string ComputerName, string? Version, string? InstallDate);

    private sealed record SoftwareGroup(string Name, string? Publisher, List<SoftwareInstall> Installs)
    {
        public int InstallCount => Installs.Count;
        public int VersionCount => Installs.Select(install => install.Version ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
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
        [SearchFieldType.Number] =
        [
            ("equals", "est"),
            ("notequals", "n'est pas"),
            ("greaterthan", "supérieur à"),
            ("lessthan", "inférieur à")
        ]
    };

    private static readonly SearchFieldDefinition[] SearchFields =
    [
        new("all", "Tous les champs", SearchFieldType.Text),
        new("name", "Nom", SearchFieldType.Text),
        new("publisher", "Éditeur", SearchFieldType.Text),
        new("version", "Version", SearchFieldType.Text),
        new("computer", "Poste", SearchFieldType.Text),
        new("versioncount", "Nb de versions", SearchFieldType.Number),
        new("installcount", "Nb d'installations", SearchFieldType.Number)
    ];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    private List<SoftwareGroup> _groups = [];
    private List<SoftwareGroup> _filteredGroups = [];
    private List<SoftwareGroup> _pagedGroups = [];
    private readonly HashSet<string> _expandedKeys = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "name", Descending = false }];
    private int _pageSize = 25;
    private int _currentPage = 1;

    private int TotalPages => _filteredGroups.Count == 0 ? 1 : (int)Math.Ceiling(_filteredGroups.Count / (double)_pageSize);

    private int ActiveCriteriaCount => _criteria.Count(criterion => criterion.Operator == "empty" || !string.IsNullOrWhiteSpace(criterion.Value));

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Computer> computers = await db.Set<Computer>()
            .AsNoTracking()
            .Include(computer => computer.Softwares)
            .ToListAsync();

        _groups = computers
            .SelectMany(computer => computer.Softwares.Select(software => (Computer: computer, Software: software)))
            .GroupBy(entry => (entry.Software.Name, entry.Software.Publisher))
            .Select(group => new SoftwareGroup(
                group.Key.Name,
                group.Key.Publisher,
                group.Select(entry => new SoftwareInstall(entry.Computer.Id, entry.Computer.Name, entry.Software.Version, entry.Software.InstallDate))
                    .OrderBy(install => install.ComputerName, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _filteredGroups = _groups;
        ApplyPaging();
    }

    private static SearchFieldDefinition? FindField(string key)
    {
        return SearchFields.FirstOrDefault(field => field.Key == key);
    }

    private List<string> GetDistinctTextValues(string fieldKey) =>
        _groups.Select(group => GetFieldText(group, fieldKey))
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
        List<SoftwareGroup> matched = _groups;
        bool first = true;

        foreach (SearchCriterion criterion in _criteria)
        {
            if (criterion.Operator != "empty" && string.IsNullOrWhiteSpace(criterion.Value)) continue;

            List<SoftwareGroup> criterionMatches = _groups.Where(group => EvaluateCriterion(group, criterion)).ToList();

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

        IOrderedEnumerable<SoftwareGroup>? ordered = null;
        foreach (SortCriterion criterion in _sortCriteria)
        {
            ordered = ordered is null
                ? (criterion.Descending
                    ? matched.OrderByDescending(group => GetSortKey(group, criterion.Field))
                    : matched.OrderBy(group => GetSortKey(group, criterion.Field)))
                : (criterion.Descending
                    ? ordered.ThenByDescending(group => GetSortKey(group, criterion.Field))
                    : ordered.ThenBy(group => GetSortKey(group, criterion.Field)));
        }

        _filteredGroups = (ordered ?? matched.AsEnumerable()).ToList();
        ApplyPaging();
    }

    private static bool EvaluateCriterion(SoftwareGroup group, SearchCriterion criterion)
    {
        SearchFieldDefinition? field = FindField(criterion.FieldKey);
        if (field is null) return true;

        if (field.Key == "all")
        {
            string term = criterion.Value.Trim();
            if (term.Length == 0) return true;

            bool anyMatch = GetAllFieldsText(group).Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
            return criterion.Operator == "notcontains" ? !anyMatch : anyMatch;
        }

        return field.Type switch
        {
            SearchFieldType.Text => EvaluateText(GetFieldText(group, field.Key), criterion),
            SearchFieldType.Number => EvaluateNumber(GetFieldNumber(group, field.Key), criterion),
            _ => true
        };
    }

    private static IEnumerable<string> GetAllFieldsText(SoftwareGroup group)
    {
        yield return group.Name;
        if (!string.IsNullOrWhiteSpace(group.Publisher)) yield return group.Publisher;
        foreach (SoftwareInstall install in group.Installs)
        {
            yield return install.ComputerName;
            if (!string.IsNullOrWhiteSpace(install.Version)) yield return install.Version;
        }
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

    private static bool EvaluateNumber(double? raw, SearchCriterion criterion)
    {
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

    private static string? GetFieldText(SoftwareGroup group, string key) => key switch
    {
        "name" => group.Name,
        "publisher" => group.Publisher,
        "version" => string.Join(", ", group.Installs.Select(i => i.Version).Where(v => v is not null).Distinct(StringComparer.OrdinalIgnoreCase)),
        "computer" => string.Join(", ", group.Installs.Select(i => i.ComputerName).Distinct(StringComparer.OrdinalIgnoreCase)),
        _ => null
    };

    private static double? GetFieldNumber(SoftwareGroup group, string key) => key switch
    {
        "versioncount" => group.VersionCount,
        "installcount" => group.InstallCount,
        _ => null
    };

    private static IComparable GetSortKey(SoftwareGroup group, string key)
    {
        SearchFieldDefinition? field = FindField(key);
        if (field is null) return group.Name;

        return field.Type switch
        {
            SearchFieldType.Number => GetFieldNumber(group, key) ?? double.MinValue,
            _ => GetFieldText(group, key) ?? string.Empty
        };
    }

    private static string GroupKey(SoftwareGroup group) => $"{group.Name}|{group.Publisher}";

    private void ToggleGroup(string key)
    {
        if (!_expandedKeys.Add(key))
        {
            _expandedKeys.Remove(key);
        }
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedGroups = _filteredGroups
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
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
}
