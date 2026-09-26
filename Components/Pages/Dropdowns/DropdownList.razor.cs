using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Dropdowns;

public partial class DropdownList : ComponentBase
{
    [Parameter]
    public string? TypeSlug { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    /// <summary>Route demandée ("/config/components/..." vs "/config/dropdowns/...") : détermine le groupe par défaut (TypeSlug absent) et quel jeu d'onglets afficher.</summary>
    private bool IsComponentsRoute => new Uri(Nav.Uri).AbsolutePath.StartsWith("/config/components", StringComparison.OrdinalIgnoreCase);

    private DropdownType _type;
    private bool _typeResolved;
    private List<DropdownItem> _items = [];
    private readonly HashSet<int> _selectedIds = [];
    private DropdownItem _newItem = new() { Name = string.Empty };

    /// <summary>Messages de doublon, affichés là où l'utilisateur agit : dans la modale d'ajout, ou au-dessus de la liste en édition.</summary>
    private string? _createError;
    private string? _editError;

    private int? _editingId;
    private string _editName = string.Empty;
    private string? _editComment;
    private string? _editColor;
    private int? _editParentId;

    /// <summary>Couleur par défaut proposée dans le sélecteur quand aucune couleur n'est encore définie (gris neutre Tabler, cohérent avec le badge de secours affiché tant que Color est null).</summary>
    private const string DefaultColorHex = "#6c757d";

    private bool AllSelected => _items.Count > 0 && _selectedIds.Count == _items.Count;

    /// <summary>Couleur affichée dans le sélecteur &lt;input type="color"&gt; : les navigateurs n'acceptent que des hex #rrggbb, jamais "transparent" ni null — d'où ce fallback purement visuel, sans effet sur la valeur enregistrée tant que "Transparent" n'est pas décoché.</summary>
    private static string ColorPickerValue(string? color) =>
        color is { Length: > 0 } c && c != "transparent" ? c : DefaultColorHex;

    private static bool IsTransparent(string? color) => color == "transparent";

    /// <summary>Profondeur d'un Lieu dans l'arborescence (0 = racine), calculée en remontant ParentId au sein de _items — jamais persistée. Bornée à 20 niveaux comme garde-fou contre un cycle accidentel.</summary>
    private int Depth(DropdownItem item)
    {
        Dictionary<int, DropdownItem> byId = _items.ToDictionary(i => i.Id);
        int depth = 0;
        int? parentId = item.ParentId;
        while (parentId is int pid && byId.TryGetValue(pid, out DropdownItem? parent) && depth < 20)
        {
            depth++;
            parentId = parent.ParentId;
        }
        return depth;
    }

    /// <summary>
    /// Réordonne _items (déjà trié par nom) en ordre d'arborescence — chaque parent suivi
    /// immédiatement de ses enfants — pour que l'indentation (Depth ci-dessus) forme une vraie
    /// arborescence visuelle plutôt qu'une liste plate triée alphabétiquement.
    ///
    /// Le tri lui-même vit dans <see cref="LocationHierarchy"/> : les selects de Lieu des fiches
    /// d'actifs en ont le même besoin, et deux implémentations divergeraient.
    /// </summary>
    private static List<DropdownItem> SortHierarchically(List<DropdownItem> items) =>
        LocationHierarchy.Sort(items);

    /// <summary>Libellé indenté d'un Lieu candidat comme parent — exclut l'élément lui-même (pas de cycle direct) dans le select "Lieu parent".</summary>
    private static string ParentOptionLabel(DropdownItem item, int depth) =>
        depth == 0 ? item.Name : new string(' ', depth * 4) + "— " + item.Name;

    /// <summary>Descendants d'un Lieu (parcours en largeur sur _items), pour exclure du select "Lieu parent" les candidats qui créeraient un cycle — en plus de l'élément lui-même, exclu séparément.</summary>
    private HashSet<int> DescendantIds(int rootId)
    {
        HashSet<int> result = [];
        Queue<int> queue = new();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (DropdownItem child in _items.Where(i => i.ParentId == current))
            {
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
            }
        }

        return result;
    }

    /// <summary>Candidats valides comme parent pour l'élément en cours d'édition (ou null en création) : tous les Lieux sauf lui-même et ses descendants (cycle).</summary>
    private IEnumerable<DropdownItem> ParentCandidates(int? excludingId)
    {
        HashSet<int> excluded = excludingId is int id ? DescendantIds(id) : [];
        if (excludingId is int selfId) excluded.Add(selfId);

        return _items.Where(i => !excluded.Contains(i.Id));
    }

    protected override async Task OnParametersSetAsync()
    {
        // Slug absent (route "/config/dropdowns" sans segment) : bascule sur le premier type par défaut.
        _typeResolved = string.IsNullOrEmpty(TypeSlug)
            ? SetDefaultType()
            : DropdownTypeCatalog.TryParseSlug(TypeSlug, out _type);

        if (!_typeResolved)
        {
            return;
        }

        _newItem = new DropdownItem { Name = string.Empty, Type = _type };
        _editingId = null;
        await LoadAsync();
    }

    private bool SetDefaultType()
    {
        _type = (IsComponentsRoute ? DropdownTypeCatalog.Components : DropdownTypeCatalog.Labels)[0];
        return true;
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DropdownItem> loaded = await db.Set<DropdownItem>()
            .AsNoTracking()
            .Include(i => i.Parent)
            .Where(i => i.Type == _type)
            .OrderBy(i => i.Name)
            .ToListAsync();

        _items = _type == DropdownType.Location ? SortHierarchically(loaded) : loaded;
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DropdownItem item in _items)
            {
                _selectedIds.Add(item.Id);
            }
        }
    }

    private void ToggleSelect(int id, bool selected)
    {
        if (selected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private string? _rebuildMessage;

    /// <summary>
    /// Reconstruit la catégorie courante à partir des composants déjà présents dans le parc.
    ///
    /// L'inventaire alimente le catalogue au fil de l'eau, mais seulement depuis qu'il sait le
    /// faire : sur une base existante, les modèles remontés par les inventaires passés ne sont
    /// nulle part. Ce rattrapage les retrouve, sans attendre que chaque poste repasse.
    ///
    /// Idempotent : il n'ajoute que les noms absents, et peut donc être relancé sans crainte.
    /// </summary>
    private async Task RebuildFromParcAsync()
    {
        _rebuildMessage = null;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        List<string> names = _type == DropdownType.Battery
            ? await db.Set<ComputerBattery>().Select(battery => battery.Name).Distinct().ToListAsync()
            : await CollectComponentNamesAsync(db);

        HashSet<string> existing = new(
            await db.Set<DropdownItem>().Where(item => item.Type == _type).Select(item => item.Name).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        List<string> missing = [.. names
            .Where(DropdownTypeCatalog.IsCatalogueableName)
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !existing.Contains(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)];

        foreach (string name in missing)
        {
            db.Set<DropdownItem>().Add(new DropdownItem { Type = _type, Name = name });
        }

        await db.SaveChangesAsync();

        _rebuildMessage = missing.Count == 0
            ? "Aucun modèle à ajouter : le catalogue est déjà à jour pour cette catégorie."
            : $"{missing.Count} modèle(s) ajouté(s) depuis le parc.";

        await LoadAsync();
    }

    private async Task<List<string>> CollectComponentNamesAsync(DbContext db)
    {
        List<ComponentType> sources = [.. DropdownTypeCatalog.ComponentTypesFor(_type)];

        return sources.Count == 0
            ? []
            : await db.Set<ComputerComponent>()
                .Where(component => sources.Contains(component.Type))
                .Select(component => component.Designation)
                .Distinct()
                .ToListAsync();
    }

    /// <summary>Vrai quand la catégorie courante peut être reconstruite : toutes n'ont pas de
    /// source dans le parc (les boîtiers, capteurs et alimentations ne sont pas inventoriés).</summary>
    private bool CanRebuildFromParc =>
        _type == DropdownType.Battery || DropdownTypeCatalog.ComponentTypesFor(_type).Count > 0;

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DropdownItem> toDelete = await db.Set<DropdownItem>().Where(i => _selectedIds.Contains(i.Id)).ToListAsync();
        db.Set<DropdownItem>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task DeleteAsync(int id)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DropdownItem? tracked = await db.Set<DropdownItem>().FirstOrDefaultAsync(i => i.Id == id);
        if (tracked is null) return;

        db.Set<DropdownItem>().Remove(tracked);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private void StartEdit(DropdownItem item)
    {
        _editingId = item.Id;
        _editName = item.Name;
        _editComment = item.Comment;
        _editColor = item.Color;
        _editParentId = item.ParentId;
    }

    private void CancelEdit()
    {
        _editingId = null;
    }

    private async Task SaveEditAsync()
    {
        if (_editingId is not int id || string.IsNullOrWhiteSpace(_editName)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DropdownItem? tracked = await db.Set<DropdownItem>().FirstOrDefaultAsync(i => i.Id == id);
        if (tracked is null) return;

        _editError = null;

        if (await IsDuplicateAsync(db, _editName, _editParentId, excludedId: id))
        {
            _editError = DuplicateMessage(_editParentId);
            return;
        }

        tracked.Name = _editName;
        tracked.Comment = _editComment;
        tracked.Color = _editColor;
        tracked.ParentId = _editParentId;
        await db.SaveChangesAsync();

        _editingId = null;
        await LoadAsync();
    }

    /// <summary>
    /// Vrai si une valeur de même nom existe déjà dans ce type, sous le même parent. Contrôle
    /// préalable plutôt que de laisser remonter la violation d'index : l'utilisateur doit savoir
    /// que c'est le couple nom + parent qui est en cause, pas le nom seul.
    /// </summary>
    private async Task<bool> IsDuplicateAsync(DbContext db, string name, int? parentId, int? excludedId)
    {
        string trimmed = name.Trim();

        return await db.Set<DropdownItem>().AnyAsync(item =>
            item.Type == _type
            && item.ParentId == parentId
            && item.Name == trimmed
            && (excludedId == null || item.Id != excludedId));
    }

    private string DuplicateMessage(int? parentId)
    {
        DropdownItem? parent = parentId is { } id ? _items.FirstOrDefault(item => item.Id == id) : null;

        return parent is null
            ? "Cette valeur existe déjà à la racine."
            : $"Cette valeur existe déjà sous « {parent.Name} ».";
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        _createError = null;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        if (await IsDuplicateAsync(db, _newItem.Name, _newItem.ParentId, excludedId: null))
        {
            _createError = DuplicateMessage(_newItem.ParentId);
            return;
        }

        db.Set<DropdownItem>().Add(_newItem);
        await db.SaveChangesAsync();

        // Nouvelle instance plutôt que de réinitialiser les champs de _newItem en place : une fois
        // enregistré, cet objet porte l'Id généré par la base — le réutiliser tel quel dans un
        // futur Add() sur un DbContext frais ferait tenter un INSERT avec un Id explicite déjà pris
        // (colonne Identity), rejeté par SQL Server (IDENTITY_INSERT OFF).
        _newItem = new DropdownItem { Name = string.Empty, Type = _type };

        await JS.InvokeVoidAsync("glping.hideModal", "newDropdownItemModal");
        await LoadAsync();
    }
}
