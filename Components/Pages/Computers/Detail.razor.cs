using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Computers;

public partial class Detail : ComponentBase
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    [Parameter]
    public int ComputerId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private Computer? _computer;
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "computer";
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _computer = await db.Set<Computer>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(computer => computer.Components)
            .Include(computer => computer.Softwares)
            .Include(computer => computer.Peripherals)
            .Include(computer => computer.Volumes)
            .Include(computer => computer.Batteries)
            .Include(computer => computer.NetworkPorts)
            .Include(computer => computer.ImportHistories)
            .Include(computer => computer.HistoryEntries)
            .Include(computer => computer.Agent)
            .FirstOrDefaultAsync(computer => computer.Id == ComputerId);

        if (_computer is null)
        {
            return;
        }

        _tabs = BuildTabs(_computer);

        // Requêtes séquentielles sur le même DbContext : EF Core ne supporte pas
        // plusieurs opérations concurrentes sur une même instance.
        _total = await db.Set<Computer>().AsNoTracking().CountAsync();
        _position = await db.Set<Computer>().AsNoTracking().CountAsync(c => c.Id <= ComputerId);
        _previousId = await db.Set<Computer>().AsNoTracking()
            .Where(c => c.Id < ComputerId)
            .OrderByDescending(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
        _nextId = await db.Set<Computer>().AsNoTracking()
            .Where(c => c.Id > ComputerId)
            .OrderBy(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
    }

    // Reprend la liste et l'ordre des onglets de la fiche "Ordinateur" de GLPI. Seuls
    // "computer", "os", "components", "batteries", "volumes", "software", "connections",
    // "networkports", "importinfo" et "history" ont un contenu réel pour l'instant (voir le
    // @switch de Detail.razor) ; les autres affichent un placeholder en attendant d'être
    // alimentés au fur et à mesure des besoins.
    private static List<FicheTab> BuildTabs(Computer computer) =>
    [
        new("computer", "ti-device-desktop", "Ordinateur", null),
        new("os", "ti-settings-cog", "Systèmes d'exploitation", computer.OperatingSystem is null ? 0 : 1),
        new("components", "ti-cpu", "Composants", computer.Components.Count),
        new("batteries", "ti-battery", "Batteries", computer.Batteries.Count),
        new("volumes", "ti-server-2", "Volumes", computer.Volumes.Count),
        new("software", "ti-apps", "Logiciels", computer.Softwares.Count),
        new("connections", "ti-plug-connected", "Connexions", computer.Peripherals.Count),
        new("networkports", "ti-network", "Ports réseau", computer.NetworkPorts.Count),
        new("connectors", "ti-usb", "Connecteurs", null),
        new("remotecontrol", "ti-device-desktop-share", "Contrôle à distance", null),
        new("antivirus", "ti-shield-check", "Antivirus", null),
        new("locks", "ti-lock", "Verrous", null),
        new("domains", "ti-world-www", "Domaines", null),
        new("importinfo", "ti-file-import", "Informations d'import", computer.ImportHistories.Count),
        new("history", "ti-history", "Historique", computer.HistoryEntries.Count),
        new("tasks", "ti-checklist", "Tâches / groupes", null),
        new("collectinfo", "ti-cloud-upload", "Informations de collecte", null),
        new("deploy", "ti-package", "Déploiement de package", null),
        new("all", "ti-list", "Tous", null),
    ];

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private static string? LocationLabel(Computer computer)
    {
        var parts = new[] { computer.Site, computer.Building, computer.Room }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(" > ", parts);
        return label.Length > 0 ? label : null;
    }

    private static string LastInventoryLabel(Computer computer)
    {
        return computer.LastInventoryAt is { } lastInventory
            ? lastInventory.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "Jamais";
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

    private static IEnumerable<IGrouping<ComponentType, ComputerComponent>> ComponentGroups(Computer computer)
    {
        return computer.Components
            .GroupBy(component => component.Type)
            .OrderBy(group => (int)group.Key);
    }

    private static string ComponentTypeLabel(ComponentType type) => type switch
    {
        ComponentType.Cpu => "Processeurs",
        ComponentType.Ram => "Mémoire",
        ComponentType.Disk => "Disques durs",
        ComponentType.NetworkCard => "Cartes réseau",
        ComponentType.Gpu => "Cartes graphiques",
        ComponentType.Motherboard => "Cartes mères",
        _ => type.ToString()
    };

    private static string ComponentTypeIcon(ComponentType type) => type switch
    {
        ComponentType.Cpu => "ti-cpu",
        ComponentType.Ram => "ti-dimensions",
        ComponentType.Disk => "ti-device-floppy",
        ComponentType.NetworkCard => "ti-network",
        ComponentType.Gpu => "ti-device-gamepad",
        ComponentType.Motherboard => "ti-circuit-board",
        _ => "ti-puzzle"
    };

    private static IEnumerable<IGrouping<PeripheralKind, ComputerPeripheral>> PeripheralGroups(Computer computer)
    {
        return computer.Peripherals
            .GroupBy(peripheral => peripheral.Kind)
            .OrderBy(group => (int)group.Key);
    }

    private static string PeripheralKindLabel(PeripheralKind kind) => kind switch
    {
        PeripheralKind.Monitor => "Écrans",
        PeripheralKind.Printer => "Imprimantes",
        PeripheralKind.Other => "Autres périphériques",
        _ => kind.ToString()
    };

    private static string PeripheralKindIcon(PeripheralKind kind) => kind switch
    {
        PeripheralKind.Monitor => "ti-device-tv",
        PeripheralKind.Printer => "ti-printer",
        PeripheralKind.Other => "ti-device-usb",
        _ => "ti-device-tv"
    };

    private static string SizeLabel(long? sizeMb) => sizeMb switch
    {
        null => "—",
        >= 1024 => $"{sizeMb.Value / 1024.0:0.##} Gio",
        _ => $"{sizeMb} Mio"
    };

    private static int? UsagePercent(ComputerVolume volume)
    {
        if (volume.TotalSizeMb is not { } total || total <= 0 || volume.FreeSizeMb is not { } free)
        {
            return null;
        }

        long used = Math.Max(0, total - free);
        return (int)Math.Round(used * 100.0 / total);
    }

    private static string VoltageLabel(int? voltageMv) => voltageMv is { } mv ? $"{mv / 1000.0:0.##} V" : "—";

    private static string CapacityLabel(int? capacityMwh) => capacityMwh is { } mwh ? $"{mwh} mWh" : "—";

    private static string DhcpLabel(string? ipDhcp) => ipDhcp switch
    {
        null or "" => "—",
        "1" or "yes" or "true" => "Oui",
        "0" or "no" or "false" => "Non",
        _ => ipDhcp
    };

    private static string SpeedLabel(int? speedMbps) => speedMbps switch
    {
        null => "—",
        >= 1000 => $"{speedMbps.Value / 1000.0:0.##} Gb/s",
        _ => $"{speedMbps} Mb/s"
    };
}
