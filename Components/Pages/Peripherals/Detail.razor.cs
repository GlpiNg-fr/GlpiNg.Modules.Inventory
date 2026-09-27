using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Inventory.Components.Pages.Peripherals;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    // Snapshot des champs journalisés dans l'onglet "Historique" (même principe que
    // GlpiNg.Web.Components.Pages.Groups.Detail) : peu importe quel bouton "Sauvegarder" a été
    // cliqué, ce diff générique retrouve les champs réellement modifiés sur le _peripheral
    // partagé en mémoire.
    private sealed record PeripheralSnapshot(
        string Name, int? StatusId, string? Type, string? Manufacturer, string? Model, string? Brand,
        string? SerialNumber, string? InventoryNumber, string? Uuid, string? Site, string? Building, string? Room,
        string? TechnicianInCharge, string? AssignedUser, string? Contact, string? ContactNumber,
        bool IsGlobalManagement, string? Comment, int? ComputerId);

    [Parameter]
    public int PeripheralId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private Peripheral? _peripheral;
    private List<Computer> _allComputers = [];
    private List<DropdownItem> _statusOptions = [];
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "peripheral";
    private bool _isSaving;
    private string _currentUserName = "Système";
    private PeripheralSnapshot _beforeEdit = null!;
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;
    private int _computerIdToLink;
    private int _loadedPeripheralId;

    // OnParametersSetAsync (pas OnInitializedAsync) : en navigation via les boutons
    // précédent/suivant, le routeur Blazor réutilise la même instance de composant et ne fait
    // que changer PeripheralId — OnInitializedAsync ne se redéclencherait donc jamais.
    protected override async Task OnParametersSetAsync()
    {
        if (_peripheral is not null && _loadedPeripheralId == PeripheralId)
        {
            return;
        }

        if (AuthStateTask is not null)
        {
            var authState = await AuthStateTask;
            string? name = authState.User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _currentUserName = name;
            }
        }

        _loadedPeripheralId = PeripheralId;
        _activeTabKey = "peripheral";

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();

        _peripheral = await _db.Set<Peripheral>()
            .Include(peripheral => peripheral.Computer)
            .Include(peripheral => peripheral.HistoryEntries)
            .Include(peripheral => peripheral.StatusItem)
            .FirstOrDefaultAsync(peripheral => peripheral.Id == PeripheralId);

        if (_peripheral is null)
        {
            return;
        }

        _allComputers = await _db.Set<Computer>().AsNoTracking().OrderBy(computer => computer.Name).ToListAsync();
        _statusOptions = await _db.Set<DropdownItem>().AsNoTracking()
            .Where(i => i.Type == DropdownType.Status)
            .OrderBy(i => i.Name)
            .ToListAsync();
        _computerIdToLink = _peripheral.ComputerId ?? 0;
        _beforeEdit = Snapshot(_peripheral);
        RebuildTabs();

        _total = await _db.Set<Peripheral>().AsNoTracking().CountAsync();
        _position = await _db.Set<Peripheral>().AsNoTracking().CountAsync(peripheral => peripheral.Id <= PeripheralId);
        _previousId = await _db.Set<Peripheral>().AsNoTracking()
            .Where(peripheral => peripheral.Id < PeripheralId)
            .OrderByDescending(peripheral => peripheral.Id)
            .Select(peripheral => (int?)peripheral.Id)
            .FirstOrDefaultAsync();
        _nextId = await _db.Set<Peripheral>().AsNoTracking()
            .Where(peripheral => peripheral.Id > PeripheralId)
            .OrderBy(peripheral => peripheral.Id)
            .Select(peripheral => (int?)peripheral.Id)
            .FirstOrDefaultAsync();
    }

    // Reprend la liste et l'ordre des onglets de la fiche "Périphérique" de GLPI (vérifiés sur
    // une instance GLPI réelle via front/peripheral.form.php). Seuls "peripheral", "connections"
    // et "history" ont un contenu réel pour l'instant (voir le @switch de Detail.razor) ; les
    // autres dépendent de fonctionnalités absentes de GlpiNg (contrats, tickets, documents, ...)
    // et affichent un placeholder en attendant d'être alimentés au fur et à mesure des besoins.
    private void RebuildTabs()
    {
        if (_peripheral is null)
        {
            return;
        }

        _tabs =
        [
            new("peripheral", "ti-mouse", "Périphérique", null),
            new("impact", "ti-affiliate", "Analyse d'impact", null),
            new("os", "ti-settings-cog", "Systèmes d'exploitation", null),
            new("software", "ti-apps", "Logiciels", null),
            new("components", "ti-cpu", "Composants", null),
            new("phonelines", "ti-phone", "Lignes téléphoniques", null),
            new("connections", "ti-plug-connected", "Connexions", _peripheral.ComputerId is null ? 0 : 1),
            new("networkports", "ti-network", "Ports réseau", null),
            new("connectors", "ti-usb", "Connecteurs", null),
            new("management", "ti-settings", "Gestion", null),
            new("contracts", "ti-file-contract", "Contrats", null),
            new("documents", "ti-file", "Documents", null),
            new("knowbase", "ti-book", "Base de connaissances", null),
            new("tickets", "ti-ticket", "Tickets", null),
            new("problems", "ti-alert-triangle", "Problèmes", null),
            new("changes", "ti-replace", "Changements", null),
            new("projects", "ti-clipboard-list", "Projets", null),
            new("links", "ti-link", "Liens", null),
            new("locks", "ti-lock", "Verrous", null),
            new("notes", "ti-notes", "Notes", null),
            new("reservations", "ti-calendar", "Réservations", null),
            new("certificates", "ti-certificate", "Certificats", null),
            new("domains", "ti-world-www", "Domaines", null),
            new("applications", "ti-apps", "Applicatifs", null),
            new("importinfo", "ti-file-import", "Informations d'import", null),
            new("history", "ti-history", "Historique", _peripheral.HistoryEntries.Count),
            new("all", "ti-list", "Tous", null),
        ];
    }

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private static PeripheralSnapshot Snapshot(Peripheral p) => new(
        p.Name, p.StatusId, p.Type, p.Manufacturer, p.Model, p.Brand,
        p.SerialNumber, p.InventoryNumber, p.Uuid, p.Site, p.Building, p.Room,
        p.TechnicianInCharge, p.AssignedUser, p.Contact, p.ContactNumber,
        p.IsGlobalManagement, p.Comment, p.ComputerId);

    private static string YesNo(bool value) => value ? Tr.T("Oui") : Tr.T("Non");

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(PeripheralSnapshot before, PeripheralSnapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.StatusId != after.StatusId) yield return ("Statut", StatusLabel(before.StatusId), StatusLabel(after.StatusId));
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Manufacturer != after.Manufacturer) yield return ("Fabricant", before.Manufacturer, after.Manufacturer);
        if (before.Model != after.Model) yield return ("Modèle", before.Model, after.Model);
        if (before.Brand != after.Brand) yield return ("Marque", before.Brand, after.Brand);
        if (before.SerialNumber != after.SerialNumber) yield return ("Numéro de série", before.SerialNumber, after.SerialNumber);
        if (before.InventoryNumber != after.InventoryNumber) yield return ("Numéro d'inventaire", before.InventoryNumber, after.InventoryNumber);
        if (before.Uuid != after.Uuid) yield return ("UUID", before.Uuid, after.Uuid);
        if (before.Site != after.Site) yield return ("Site", before.Site, after.Site);
        if (before.Building != after.Building) yield return ("Bâtiment", before.Building, after.Building);
        if (before.Room != after.Room) yield return ("Salle", before.Room, after.Room);
        if (before.TechnicianInCharge != after.TechnicianInCharge) yield return ("Technicien responsable", before.TechnicianInCharge, after.TechnicianInCharge);
        if (before.AssignedUser != after.AssignedUser) yield return ("Utilisateur", before.AssignedUser, after.AssignedUser);
        if (before.Contact != after.Contact) yield return ("Usager", before.Contact, after.Contact);
        if (before.ContactNumber != after.ContactNumber) yield return ("Usager numéro", before.ContactNumber, after.ContactNumber);
        if (before.IsGlobalManagement != after.IsGlobalManagement) yield return ("Type de gestion", YesNo(before.IsGlobalManagement), YesNo(after.IsGlobalManagement));
        if (before.Comment != after.Comment) yield return ("Commentaires", before.Comment, after.Comment);
        if (before.ComputerId != after.ComputerId) yield return ("Connexion", ComputerLabel(before.ComputerId), ComputerLabel(after.ComputerId));
    }

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private string ComputerLabel(int? computerId) =>
        computerId is { } id && _allComputers.FirstOrDefault(computer => computer.Id == id) is { } computer ? computer.Name : "-----";

    private async Task SaveAsync()
    {
        if (_db is null || _peripheral is null)
        {
            return;
        }

        _isSaving = true;

        try
        {
            PeripheralSnapshot after = Snapshot(_peripheral);
            List<PeripheralHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new PeripheralHistoryEntry
                {
                    PeripheralId = _peripheral.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _peripheral.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<PeripheralHistoryEntry>().AddRange(entries);
            }

            await _db.SaveChangesAsync();
            _beforeEdit = after;
            await _db.Entry(_peripheral).Collection(p => p.HistoryEntries).LoadAsync();
            await _db.Entry(_peripheral).Reference(p => p.StatusItem).LoadAsync();
            RebuildTabs();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _peripheral is null)
        {
            return;
        }

        _db.Set<Peripheral>().Remove(_peripheral);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/peripherals");
    }

    private async Task LinkComputerAsync()
    {
        if (_db is null || _peripheral is null)
        {
            return;
        }

        _peripheral.ComputerId = _computerIdToLink == 0 ? null : _computerIdToLink;
        await SaveAsync();
        await _db.Entry(_peripheral).Reference(p => p.Computer).LoadAsync();
    }

    private async Task UnlinkComputerAsync()
    {
        _computerIdToLink = 0;
        await LinkComputerAsync();
    }

    private string StatusLabel(int? statusId) =>
        statusId is { } id ? _statusOptions.FirstOrDefault(s => s.Id == id)?.Name ?? "—" : "—";

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
