using GlpiNg.Modules.Abstractions.FieldUnicity;
using GlpiNg.Modules.Abstractions.Items;
using GlpiNg.Modules.Abstractions.Preferences;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Search;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Cables;

public partial class Index : ComponentBase
{
    /// <summary>Champs interrogeables de cette liste — voir SearchEngine. Propriété d'instance
    /// et non champ statique : certains accesseurs appellent des méthodes de la page.</summary>
    private SearchField<Cable>[] SearchFields =>
    [
        SearchField<Cable>.AllFields(),
        SearchField<Cable>.Text("name", "Nom", item => item.Name),
        SearchField<Cable>.Text("status", "Statut", item => item.StatusItem?.Name),
        SearchField<Cable>.Text("type", "Type", item => item.Type),
        SearchField<Cable>.Text("color", "Couleur", item => item.Color),
        SearchField<Cable>.Text("endpointA", "Extrémité A", item => EndpointLabel(item.EndpointAType, item.EndpointAId)),
        SearchField<Cable>.Text("endpointB", "Extrémité B", item => item.EndpointBType is not null && item.EndpointBId is not null ? EndpointLabel(item.EndpointBType.Value, item.EndpointBId.Value) : null),
        SearchField<Cable>.Text("comment", "Commentaires", item => item.Comment),
        SearchField<Cable>.Date("createdat", "Date de création", item => item.CreatedAt),
        SearchField<Cable>.Date("updatedat", "Dernière modification", item => item.UpdatedAt),
    ];

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IFieldUnicityChecker FieldUnicity { get; set; } = null!;

    /// <summary>Refus d'un critère d'unicité des champs, affiché dans la fenêtre de création.</summary>
    private string? _createError;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<Cable> _items = [];
    private List<Cable> _filtered = [];
    private List<Cable> _paged = [];
    private readonly HashSet<int> _selectedIds = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "name" }];
    // Taille de page par défaut du compte connecté (page /preferences). Injecté par l'hôte, qui
    // seul connaît le modèle d'utilisateur — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private int _pageSize = 25;
    private int _currentPage = 1;
    private Cable _newItem = NewBlank();
    private List<DropdownItem> _statusOptions = [];
    private Dictionary<(CableEndpointType Type, int Id), string> _endpointNames = [];

    private bool AllSelected => _paged.Count > 0 && _selectedIds.IsSupersetOf(_paged.Select(item => item.Id));
    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync()
    {
        _pageSize = (await UserPreferences.GetAsync()).ItemsPerPage;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<Cable>()
            .AsNoTracking()
            .Include(item => item.StatusItem)
            .OrderBy(item => item.Name)
            .ToListAsync();

        _statusOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Status).OrderBy(i => i.Name).ToListAsync();

        List<(CableEndpointType Type, int Id)> keys = [.. _items.Select(c => (c.EndpointAType, c.EndpointAId))];
        keys.AddRange(_items.Where(c => c.EndpointBType is not null && c.EndpointBId is not null).Select(c => (c.EndpointBType!.Value, c.EndpointBId!.Value)));
        _endpointNames = await CableEndpointCatalog.ResolveNamesAsync(db, keys);

        _selectedIds.Clear();
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private string EndpointLabel(CableEndpointType type, int id) => _endpointNames.TryGetValue((type, id), out string? name) ? name : $"#{id}";

    /// <summary>Valeurs proposées derrière « est » / « n'est pas » sur un champ texte.</summary>
    private IEnumerable<string> DistinctValues(string fieldKey) => SearchEngine.DistinctValues(_items, SearchFields, fieldKey);

    /// <summary>
    /// Tri par clic sur un en-tête : remplace le tri courant, et inverse le sens si la colonne
    /// était déjà le seul tri actif. Les tris multiples se règlent depuis le panneau « Trier ».
    /// </summary>
    private void SetSort(string fieldKey)
    {
        if (_sortCriteria is [{ } only] && only.Field == fieldKey)
        {
            only.Descending = !only.Descending;
        }
        else
        {
            _sortCriteria.Clear();
            _sortCriteria.Add(new SortCriterion { Field = fieldKey });
        }

        ApplyFilterAndSort();
    }

    private MarkupString SortIndicator(string fieldKey)
    {
        SortCriterion? criterion = _sortCriteria.FirstOrDefault(sort => sort.Field == fieldKey);
        if (criterion is null) return new MarkupString(string.Empty);
        return new MarkupString($"<i class=\"ti {(criterion.Descending ? "ti-caret-up-filled" : "ti-caret-down-filled")}\"></i>");
    }

    private void ApplyFilterAndSort()
    {
        _filtered = SearchEngine.Apply(_items, SearchFields, _criteria, _sortCriteria);
        _selectedIds.IntersectWith(_filtered.Select(item => item.Id));
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _paged = _filtered.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
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
        int target = Math.Clamp(page, 1, TotalPages);
        if (target == _currentPage) return;
        _currentPage = target;
        ApplyPaging();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        foreach (Cable item in _paged)
        {
            if (selectAll) _selectedIds.Add(item.Id);
            else _selectedIds.Remove(item.Id);
        }
    }

    private void ToggleSelect(int id, bool selected)
    {
        if (selected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Cable> toDelete = await db.Set<Cable>().Where(item => _selectedIds.Contains(item.Id)).ToListAsync();
        db.Set<Cable>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name) || _newItem.EndpointAId <= 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        // Unicité des champs (Configuration > Unicité des champs) : un critère peut refuser la
        // création d'un doublon. La fenêtre reste ouverte avec la saisie, pour la corriger.
        FieldUnicityVerdict verdict = await FieldUnicity.CheckAsync(ItemTypes.Cable, db.Set<Cable>(), _newItem);
        if (verdict.Refused)
        {
            _createError = verdict.Message;
            return;
        }

        _createError = null;
        db.Set<Cable>().Add(_newItem);
        await db.SaveChangesAsync();

        _newItem = NewBlank();
        await JS.InvokeVoidAsync("glping.hideModal", "newItemModal");
        await LoadAsync();
    }

    private static Cable NewBlank() => new() { Name = string.Empty, EndpointAType = CableEndpointType.Computer };
}
