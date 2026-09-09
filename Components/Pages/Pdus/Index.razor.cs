using GlpiNg.Modules.Abstractions.Preferences;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Pdus;

public partial class Index : ComponentBase
{
    /// <summary>Champs interrogeables de cette liste — voir SearchEngine. Propriété d'instance
    /// et non champ statique : certains accesseurs appellent des méthodes de la page.</summary>
    private SearchField<Pdu>[] SearchFields =>
    [
        SearchField<Pdu>.AllFields(),
        SearchField<Pdu>.Text("name", "Nom", item => item.Name),
        SearchField<Pdu>.Text("status", "Statut", item => item.StatusItem?.Name),
        SearchField<Pdu>.Text("location", "Lieu", item => item.LocationItem?.Name),
        SearchField<Pdu>.Text("type", "Type", item => item.Type),
        SearchField<Pdu>.Text("manufacturer", "Fabricant", item => item.Manufacturer),
        SearchField<Pdu>.Text("model", "Modèle", item => item.Model),
        SearchField<Pdu>.Text("serial", "Numéro de série", item => item.SerialNumber),
        SearchField<Pdu>.Text("inventorynumber", "Numéro d'inventaire", item => item.InventoryNumber),
        SearchField<Pdu>.Text("technician", "Technicien responsable", item => item.TechnicianInCharge),
        SearchField<Pdu>.Text("user", "Utilisateur", item => item.AssignedUser),
        SearchField<Pdu>.Text("comment", "Commentaires", item => item.Comment),
        SearchField<Pdu>.Date("createdat", "Date de création", item => item.CreatedAt),
        SearchField<Pdu>.Date("updatedat", "Dernière modification", item => item.UpdatedAt),
    ];

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<Pdu> _items = [];
    private List<Pdu> _filtered = [];
    private List<Pdu> _paged = [];
    private readonly HashSet<int> _selectedIds = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "name" }];
    // Taille de page par défaut du compte connecté (page /preferences). Injecté par l'hôte, qui
    // seul connaît le modèle d'utilisateur — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private int _pageSize = 25;
    private int _currentPage = 1;
    private Pdu _newItem = NewBlank();
    private List<DropdownItem> _statusOptions = [];
    private List<DropdownItem> _locationOptions = [];

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
        _items = await db.Set<Pdu>()
            .AsNoTracking()
            .Include(item => item.StatusItem)
            .Include(item => item.LocationItem)
            .OrderBy(item => item.Name)
            .ToListAsync();

        _statusOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Status).OrderBy(i => i.Name).ToListAsync();
        _locationOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Location).OrderBy(i => i.Name).ToListAsync();

        _selectedIds.Clear();
        _currentPage = 1;
        ApplyFilterAndSort();
    }

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
        foreach (Pdu item in _paged)
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
        List<Pdu> toDelete = await db.Set<Pdu>().Where(item => _selectedIds.Contains(item.Id)).ToListAsync();
        db.Set<Pdu>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<Pdu>().Add(_newItem);
        await db.SaveChangesAsync();

        _newItem = NewBlank();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newItemModal");
        await LoadAsync();
    }

    private static Pdu NewBlank() => new() { Name = string.Empty };
}
