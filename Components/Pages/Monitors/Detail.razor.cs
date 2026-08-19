using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Monitors;

public partial class Detail : ComponentBase
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    [Parameter]
    public int MonitorId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private ComputerPeripheral? _monitor;
    private Computer? _computer;
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "monitor";
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;
    private int _loadedMonitorId;

    // Édition du statut (voir Models/DropdownItem.cs, DropdownType.Status) : seul champ éditable
    // manuellement de cette fiche, le reste vient de l'inventaire automatique de l'agent.
    private bool _editMode;
    private string? _editStatus;
    private bool _editSaving;
    private List<string> _statusOptions = [];

    // OnParametersSetAsync (pas OnInitializedAsync) : en navigation via les boutons
    // précédent/suivant, le routeur Blazor réutilise la même instance de composant et ne fait
    // que changer MonitorId — OnInitializedAsync ne se redéclencherait donc jamais.
    protected override async Task OnParametersSetAsync()
    {
        if (_monitor is not null && _loadedMonitorId == MonitorId)
        {
            return;
        }

        _loadedMonitorId = MonitorId;
        _activeTabKey = "monitor";
        _editMode = false;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _monitor = await db.Set<ComputerPeripheral>()
            .AsNoTracking()
            .Include(peripheral => peripheral.StatusItem)
            .FirstOrDefaultAsync(peripheral => peripheral.Id == MonitorId && peripheral.Kind == PeripheralKind.Monitor);

        if (_monitor is null)
        {
            return;
        }

        _computer = await db.Set<Computer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(computer => computer.Id == _monitor.ComputerId);

        _tabs = BuildTabs(_computer);

        IQueryable<ComputerPeripheral> monitors = db.Set<ComputerPeripheral>().AsNoTracking().Where(p => p.Kind == PeripheralKind.Monitor);

        _total = await monitors.CountAsync();
        _position = await monitors.CountAsync(p => p.Id <= MonitorId);
        _previousId = await monitors
            .Where(p => p.Id < MonitorId)
            .OrderByDescending(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync();
        _nextId = await monitors
            .Where(p => p.Id > MonitorId)
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync();
    }

    // Reprend une partie de la liste et de l'ordre des onglets de la fiche "Moniteur" de GLPI
    // (vérifiés sur une instance GLPI réelle via front/monitor.form.php) — sans "Analyse
    // d'impact", "Systèmes d'exploitation", "Logiciels", "Ports réseau", "Domaines" ni
    // "Applicatifs", non pertinents ici. Seuls "monitor" et "connections" ont un contenu réel
    // pour l'instant (voir le @switch de Detail.razor) — ComputerPeripheral ne porte pas encore
    // de sous-entités (historique, ...) contrairement à Computer ; les autres onglets affichent
    // un placeholder en attendant d'être alimentés au fur et à mesure des besoins.
    private static List<FicheTab> BuildTabs(Computer? computer) =>
    [
        new("monitor", "ti-device-tv", "Écran", null),
        new("connections", "ti-plug-connected", "Connexions", computer is null ? 0 : 1),
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
        new("importinfo", "ti-file-import", "Informations d'import", null),
        new("history", "ti-history", "Historique", null),
        new("all", "ti-list", "Tous", null),
    ];

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private async Task EnterEditModeAsync()
    {
        if (_monitor is null) return;

        _editStatus = _monitor.StatusItem?.Name;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _statusOptions = await db.Set<DropdownItem>()
            .AsNoTracking()
            .Where(i => i.Type == DropdownType.Status)
            .OrderBy(i => i.Name)
            .Select(i => i.Name)
            .ToListAsync();

        _editMode = true;
    }

    private void CancelEdit()
    {
        _editMode = false;
    }

    private async Task SaveEditAsync()
    {
        if (_monitor is null || _editSaving) return;

        _editSaving = true;
        StateHasChanged();

        try
        {
            await using DbContext db = await DbFactory.CreateDbContextAsync();

            int? statusId = null;
            if (!string.IsNullOrWhiteSpace(_editStatus))
            {
                DropdownItem? statusItem = await db.Set<DropdownItem>()
                    .FirstOrDefaultAsync(i => i.Type == DropdownType.Status && i.Name.ToLower() == _editStatus.ToLower());
                if (statusItem is null)
                {
                    statusItem = new DropdownItem { Type = DropdownType.Status, Name = _editStatus };
                    db.Set<DropdownItem>().Add(statusItem);
                    await db.SaveChangesAsync();
                }
                statusId = statusItem.Id;
            }

            ComputerPeripheral? tracked = await db.Set<ComputerPeripheral>().FirstOrDefaultAsync(p => p.Id == MonitorId);
            if (tracked is not null)
            {
                tracked.StatusId = statusId;
            }

            await db.SaveChangesAsync();

            _editMode = false;

            await using DbContext reloadDb = await DbFactory.CreateDbContextAsync();
            _monitor = await reloadDb.Set<ComputerPeripheral>()
                .AsNoTracking()
                .Include(peripheral => peripheral.StatusItem)
                .FirstOrDefaultAsync(peripheral => peripheral.Id == MonitorId);
        }
        finally
        {
            _editSaving = false;
            StateHasChanged();
        }
    }
}
