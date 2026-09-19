using GlpiNg.Modules.Abstractions.FieldUnicity;
using GlpiNg.Modules.Abstractions.Items;
using GlpiNg.Modules.Abstractions.Preferences;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Printers;

public partial class Index : ComponentBase
{
    /// <summary>Champs interrogeables de cette liste — voir SearchEngine. Propriété d'instance
    /// et non champ statique : certains accesseurs appellent des méthodes de la page.</summary>
    private SearchField<Printer>[] SearchFields =>
    [
        SearchField<Printer>.AllFields(),
        SearchField<Printer>.Text("name", "Nom", item => item.Name),
        SearchField<Printer>.Text("status", "Statut", item => item.StatusItem?.Name),
        SearchField<Printer>.Text("location", "Lieu", item => item.LocationItem?.Name),
        SearchField<Printer>.Text("type", "Type", item => item.Type),
        SearchField<Printer>.Text("manufacturer", "Fabricant", item => item.Manufacturer),
        SearchField<Printer>.Text("model", "Modèle", item => item.Model),
        SearchField<Printer>.Text("serial", "Numéro de série", item => item.SerialNumber),
        SearchField<Printer>.Text("inventorynumber", "Numéro d'inventaire", item => item.InventoryNumber),
        SearchField<Printer>.Text("technician", "Technicien responsable", item => item.TechnicianInCharge),
        SearchField<Printer>.Text("user", "Utilisateur", item => item.AssignedUser),
        SearchField<Printer>.Text("comment", "Commentaires", item => item.Comment),
        SearchField<Printer>.Text("uuid", "UUID", item => item.Uuid),
        SearchField<Printer>.Number("initialpages", "Compteur de pages initial", item => item.InitialPageCount),
        SearchField<Printer>.Number("currentpages", "Compteur de pages actuel", item => item.CurrentPageCount),
        SearchField<Printer>.Date("createdat", "Date de création", item => item.CreatedAt),
        SearchField<Printer>.Date("updatedat", "Dernière modification", item => item.UpdatedAt),
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

    /// <summary>
    /// Cartouches en service, par imprimante. Chargées d'un bloc avec la liste plutôt qu'à chaque
    /// dépliage : une requête de plus par ligne ouverte se paierait sur un parc de deux cents
    /// imprimantes, et ces lignes sont peu nombreuses par nature.
    /// </summary>
    private Dictionary<int, List<Cartridge>> _cartridgesByPrinter = [];

    /// <summary>Imprimantes dépliées. Conservé entre les pages et les tris : replier tout ce que
    /// l'utilisateur venait d'ouvrir à chaque changement de tri serait hostile.</summary>
    private readonly HashSet<int> _expandedIds = [];

    private List<Printer> _items = [];
    private List<Printer> _filtered = [];
    private List<Printer> _paged = [];
    private readonly HashSet<int> _selectedIds = [];
    private readonly List<SearchCriterion> _criteria = [new()];
    private readonly List<SortCriterion> _sortCriteria = [new() { Field = "name" }];
    // Taille de page par défaut du compte connecté (page /preferences). Injecté par l'hôte, qui
    // seul connaît le modèle d'utilisateur — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private int _pageSize = 25;
    private int _currentPage = 1;
    private Printer _newItem = NewBlank();
    private List<DropdownItem> _statusOptions = [];
    private List<DropdownItem> _locationOptions = [];

    private bool AllSelected => _paged.Count > 0 && _selectedIds.IsSupersetOf(_paged.Select(item => item.Id));
    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync()
    {
        _pageSize = (await UserPreferences.GetAsync()).ItemsPerPage;
        await LoadAsync();
    }

    private List<Cartridge> CartridgesOf(int printerId) =>
        _cartridgesByPrinter.TryGetValue(printerId, out List<Cartridge>? cartridges) ? cartridges : [];

    private void ToggleExpanded(int printerId)
    {
        if (!_expandedIds.Remove(printerId))
        {
            _expandedIds.Add(printerId);
        }
    }

    /// <summary>
    /// Couleur du niveau d'une cartouche. Les seuils sont ceux qu'on utilise pour décider d'une
    /// commande : sous 10 % il faut agir, sous 25 % il faut prévoir.
    /// </summary>
    private static string LevelBadgeCss(int level) =>
        level <= 10 ? "bg-red-lt" : level <= 25 ? "bg-orange-lt" : "bg-green-lt";

    /// <summary>
    /// Niveau le plus bas parmi les cartouches d'une imprimante, ou null si aucune n'a été relevée.
    /// C'est lui qui résume la ligne repliée : une imprimante n'est utilisable que jusqu'à ce que
    /// sa cartouche la plus basse soit vide, et c'est donc celle-là qui commande.
    /// </summary>
    private int? LowestLevel(int printerId) => CartridgesOf(printerId)
        .Where(cartridge => cartridge.LevelPercent is not null)
        .Select(cartridge => cartridge.LevelPercent!.Value)
        .DefaultIfEmpty(-1)
        .Min() is var lowest && lowest >= 0 ? lowest : null;

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<Printer>()
            .AsNoTracking()
            .Include(item => item.StatusItem)
            .Include(item => item.LocationItem)
            .OrderBy(item => item.Name)
            .ToListAsync();

        // Seules les cartouches en service : une cartouche retirée appartient à l'historique de
        // l'imprimante, pas à l'état qu'on vient lire dans une liste de parc.
        _cartridgesByPrinter = (await db.Set<Cartridge>()
                .AsNoTracking()
                .Include(cartridge => cartridge.CartridgeItem)
                .Where(cartridge => cartridge.PrinterId != null && cartridge.DateOut == null)
                .ToListAsync())
            .GroupBy(cartridge => cartridge.PrinterId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(c => c.CartridgeItem!.Name).ToList());

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
        foreach (Printer item in _paged)
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
        List<Printer> toDelete = await db.Set<Printer>().Where(item => _selectedIds.Contains(item.Id)).ToListAsync();
        db.Set<Printer>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        // Unicité des champs (Configuration > Unicité des champs) : un critère peut refuser la
        // création d'un doublon. La fenêtre reste ouverte avec la saisie, pour la corriger.
        FieldUnicityVerdict verdict = await FieldUnicity.CheckAsync(ItemTypes.Printer, db.Set<Printer>(), _newItem);
        if (verdict.Refused)
        {
            _createError = verdict.Message;
            return;
        }

        _createError = null;
        db.Set<Printer>().Add(_newItem);
        await db.SaveChangesAsync();

        _newItem = NewBlank();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newItemModal");
        await LoadAsync();
    }

    private static Printer NewBlank() => new() { Name = string.Empty };
}
