using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GlpiNg.Modules.Inventory.Components.Pages.Computers;

public partial class Detail : ComponentBase, IDisposable
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    [Parameter]
    public int ComputerId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    // Optionnel : seul GlpiNg.Web l'enregistre (voir Program.cs), pour ne pas faire dépendre ce
    // module du module Déploiement — voir IComputerDeploymentTasksProvider.
    [Inject]
    private IComputerDeploymentTasksProvider? DeploymentTasksProvider { get; set; }

    // Optionnel pour la même raison que DeploymentTasksProvider ci-dessus — alimente l'onglet
    // "Déploiement de package" (assignation de paquets à l'agent du poste).
    [Inject]
    private IComputerDeploymentAssignmentService? DeploymentAssignmentService { get; set; }

    [Inject]
    private ComputerListStateService ListState { get; set; } = null!;

    [Inject]
    private IConfiguration Configuration { get; set; } = null!;

    [Inject]
    private IHttpClientFactory HttpClientFactory { get; set; } = null!;

    private Computer? _computer;
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "computer";
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;
    private string _listUrl = "/parc/computer";

    private static readonly int[] SoftwarePageSizeOptions = [25, 50, 100, 200];
    private List<ComputerSoftware> _softwareSorted = [];
    private List<ComputerSoftware> _softwarePaged = [];
    private int _softwarePage = 1;
    private int _softwarePageSize = 25;
    private int _loadedComputerId;
    private string? _agentStatus;
    private bool _agentStatusLoading;
    private string? _inventoryRequestResult;
    private bool _inventoryRequestLoading;
    private CancellationTokenSource? _agentStatusPollCts;
    private bool _isReloading;
    private ComputerDeploymentTasksInfo? _deploymentTasksInfo;
    private List<DeploymentPackageOption> _availablePackages = [];
    private List<ComputerDeploymentAssignment> _deploymentAssignments = [];
    private List<DeploymentPackageOption> _selectedPackagesToAssign = [];
    private DeploymentWakeMode _wakeMode = DeploymentWakeMode.None;
    private bool _prepareInstallLoading;
    private string? _prepareInstallError;

    private enum DeploymentWakeMode
    {
        None,
        Local
    }

    private static readonly TimeSpan AgentStatusPollInterval = TimeSpan.FromSeconds(10);
    private int SoftwareTotalPages => _softwareSorted.Count == 0 ? 1 : (int)Math.Ceiling(_softwareSorted.Count / (double)_softwarePageSize);

    // OnParametersSetAsync (pas OnInitializedAsync) : en navigation via les boutons
    // précédent/suivant, le routeur Blazor réutilise la même instance de composant et ne fait
    // que changer ComputerId — OnInitializedAsync ne se redéclencherait donc jamais.
    protected override async Task OnParametersSetAsync()
    {
        if (_computer is not null && _loadedComputerId == ComputerId)
        {
            return;
        }

        _loadedComputerId = ComputerId;
        _activeTabKey = "computer";
        _softwarePage = 1;
        _agentStatus = null;
        _inventoryRequestResult = null;
        _selectedPackagesToAssign = [];
        _wakeMode = DeploymentWakeMode.None;
        _prepareInstallError = null;

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _computer = await LoadComputerFromDbAsync(db);

        if (_computer is null)
        {
            return;
        }

        _softwareSorted = _computer.Softwares.OrderBy(s => s.Name).ToList();
        ApplySoftwarePaging();

        _deploymentTasksInfo = DeploymentTasksProvider is null
            ? null
            : await DeploymentTasksProvider.GetForComputerAsync(ComputerId);
        await LoadDeploymentAssignmentsAsync();
        _tabs = BuildTabs(_computer, _deploymentTasksInfo, _deploymentAssignments.Count);

        _agentStatusPollCts?.Cancel();
        _agentStatusPollCts?.Dispose();
        _agentStatusPollCts = null;

        if (_computer.Agent is not null)
        {
            _agentStatusPollCts = new CancellationTokenSource();
            _ = PollAgentStatusAsync(_agentStatusPollCts.Token);
        }

        if (ListState.FilteredIds is { Count: > 0 } ids)
        {
            int index = ids.IndexOf(ComputerId);
            if (index >= 0)
            {
                _total = ids.Count;
                _position = index + 1;
                _previousId = index > 0 ? ids[index - 1] : null;
                _nextId = index < ids.Count - 1 ? ids[index + 1] : null;
            }
            else
            {
                await LoadPaginationFromDbAsync(db);
            }
        }
        else
        {
            await LoadPaginationFromDbAsync(db);
        }
    }

    private Task<Computer?> LoadComputerFromDbAsync(DbContext db) =>
        db.Set<Computer>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(computer => computer.Components)
            .Include(computer => computer.Softwares)
            .Include(computer => computer.Peripherals)
            .Include(computer => computer.Volumes)
            .Include(computer => computer.Batteries)
            .Include(computer => computer.NetworkPorts)
            .Include(computer => computer.Antiviruses)
            .Include(computer => computer.ImportHistories)
            .Include(computer => computer.HistoryEntries)
            .Include(computer => computer.Agent)
            .FirstOrDefaultAsync(computer => computer.Id == ComputerId);

    // Recharge la fiche depuis la base sans réinitialiser l'onglet actif ni la pagination des
    // logiciels, contrairement à OnParametersSetAsync (qui, lui, correspond à une navigation vers
    // un autre poste). Déclenché par le bouton de rechargement manuel de la barre du haut.
    private async Task ReloadComputerAsync()
    {
        if (_isReloading) return;

        _isReloading = true;
        StateHasChanged();

        try
        {
            await using DbContext db = await DbFactory.CreateDbContextAsync();
            Computer? computer = await LoadComputerFromDbAsync(db);

            if (computer is not null)
            {
                _computer = computer;
                _softwareSorted = _computer.Softwares.OrderBy(s => s.Name).ToList();
                ApplySoftwarePaging();

                _deploymentTasksInfo = DeploymentTasksProvider is null
                    ? null
                    : await DeploymentTasksProvider.GetForComputerAsync(ComputerId);
                await LoadDeploymentAssignmentsAsync();
                _tabs = BuildTabs(_computer, _deploymentTasksInfo, _deploymentAssignments.Count);
            }
        }
        finally
        {
            _isReloading = false;
            StateHasChanged();
        }
    }

    private async Task LoadDeploymentAssignmentsAsync()
    {
        if (DeploymentAssignmentService is null)
        {
            _availablePackages = [];
            _deploymentAssignments = [];
            return;
        }

        _availablePackages = await DeploymentAssignmentService.GetAvailablePackagesAsync();
        _deploymentAssignments = await DeploymentAssignmentService.GetAssignmentsAsync(ComputerId);
    }

    private void AddPackageToAssign(ChangeEventArgs e)
    {
        string? value = (string?)e.Value;
        if (string.IsNullOrEmpty(value) || !int.TryParse(value, out int packageId))
        {
            return;
        }

        DeploymentPackageOption? package = _availablePackages.FirstOrDefault(p => p.Id == packageId);
        if (package is not null && !_selectedPackagesToAssign.Any(p => p.Id == packageId))
        {
            _selectedPackagesToAssign.Add(package);
        }
    }

    private void RemovePackageToAssign(int packageId)
    {
        _selectedPackagesToAssign.RemoveAll(p => p.Id == packageId);
    }

    // Reproduit le split-bouton "Préparer l'installation" / mode de réveil de la fiche
    // Ordinateur de GLPI. Contrairement à GLPI, GlpiNg n'a pas de mécanisme de Wake-on-LAN (voir
    // DeploymentGeneralSettings.MaxAgentsToWakePerTask) : seul le "réveil local" a un équivalent
    // réel ici (l'appel à l'interface web locale de l'agent, comme RequestInventoryAsync), le
    // "réveil à distance" reste donc désactivé dans le menu plutôt que simulé.
    private async Task PrepareInstallationAsync()
    {
        if (_prepareInstallLoading || DeploymentAssignmentService is null || _selectedPackagesToAssign.Count == 0)
        {
            return;
        }

        _prepareInstallLoading = true;
        _prepareInstallError = null;
        StateHasChanged();

        try
        {
            DeploymentAssignmentResult result = await DeploymentAssignmentService.AssignPackagesAsync(
                ComputerId, _selectedPackagesToAssign.Select(p => p.Id).ToList());

            if (result.Status == DeploymentAssignmentStatus.Success)
            {
                _selectedPackagesToAssign = [];
                await LoadDeploymentAssignmentsAsync();
                _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count);

                if (_wakeMode == DeploymentWakeMode.Local)
                {
                    _prepareInstallError = await TriggerLocalAgentWakeAsync();
                }
            }
            else
            {
                _prepareInstallError = result.ErrorMessage;
            }
        }
        finally
        {
            _prepareInstallLoading = false;
            StateHasChanged();
        }
    }

    private async Task<string?> TriggerLocalAgentWakeAsync()
    {
        if (_computer?.Agent is not { } agent)
        {
            return "Aucun agent associé à ce poste.";
        }

        string? url = AgentWebUrl(agent, _computer);
        if (url is null)
        {
            return "L'agent ne fournit pas d'interface web locale (httpd-trust).";
        }

        try
        {
            using HttpClient client = HttpClientFactory.CreateClient("GlpiAgent");
            using HttpResponseMessage response = await client.GetAsync($"{url}/now");
            return response.IsSuccessStatusCode
                ? null
                : $"Les paquets ont été assignés, mais le réveil local de l'agent a échoué ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"Les paquets ont été assignés, mais l'agent est injoignable pour le réveil local ({ex.Message}).";
        }
    }

    private async Task CancelAssignmentAsync(int jobId)
    {
        if (DeploymentAssignmentService is null)
        {
            return;
        }

        await DeploymentAssignmentService.CancelAssignmentAsync(jobId);
        await LoadDeploymentAssignmentsAsync();
        _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count);
        StateHasChanged();
    }

    private async Task LoadPaginationFromDbAsync(DbContext db)
    {
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
    // "networkports", "antivirus", "domains", "importinfo", "history", "tasks" et "deploy" ont un
    // contenu réel pour l'instant (voir le @switch de Detail.razor) ; les autres affichent un
    // placeholder en attendant d'être alimentés au fur et à mesure des besoins.
    private static List<FicheTab> BuildTabs(Computer computer, ComputerDeploymentTasksInfo? deploymentTasksInfo, int deploymentAssignmentsCount) =>
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
        new("antivirus", "ti-shield-check", "Antivirus", computer.Antiviruses.Count),
        new("locks", "ti-lock", "Verrous", null),
        new("domains", "ti-world-www", "Domaines", computer.Domain is null ? 0 : 1),
        new("importinfo", "ti-file-import", "Informations d'import", computer.ImportHistories.Count),
        new("history", "ti-history", "Historique", computer.HistoryEntries.Count),
        new("tasks", "ti-checklist", "Tâches / groupes",
            deploymentTasksInfo is null ? null : deploymentTasksInfo.Tasks.Count + deploymentTasksInfo.Groups.Count),
        new("collectinfo", "ti-cloud-upload", "Informations de collecte", null),
        new("deploy", "ti-package", "Déploiement de package", deploymentAssignmentsCount == 0 ? null : deploymentAssignmentsCount),
        new("all", "ti-list", "Tous", null),
    ];

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private void ApplySoftwarePaging()
    {
        _softwarePage = Math.Clamp(_softwarePage, 1, SoftwareTotalPages);
        _softwarePaged = _softwareSorted
            .Skip((_softwarePage - 1) * _softwarePageSize)
            .Take(_softwarePageSize)
            .ToList();
    }

    private void SetSoftwarePageSize(int size)
    {
        if (_softwarePageSize == size) return;
        _softwarePageSize = size;
        _softwarePage = 1;
        ApplySoftwarePaging();
    }

    private void GoToSoftwarePage(int page)
    {
        int target = Math.Clamp(page, 1, SoftwareTotalPages);
        if (target == _softwarePage) return;
        _softwarePage = target;
        ApplySoftwarePaging();
    }

    private static string? LocationLabel(Computer computer)
    {
        var parts = new[] { computer.Site, computer.Building, computer.Room }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(" > ", parts);
        return label.Length > 0 ? label : null;
    }

    /// <summary>Port par défaut de l'interface web locale de GLPI-Agent (httpd-trust).</summary>
    private const int AgentWebPort = 62354;

    private static string? AgentWebUrl(GlpiAgent agent, Computer computer)
    {
        string? hostname = agent.Hostname ?? computer.Name;
        return string.IsNullOrWhiteSpace(hostname) ? null : $"http://{hostname}:{AgentWebPort}";
    }

    // Interroge automatiquement /status à l'ouverture de la fiche, puis toutes les
    // AgentStatusPollInterval tant que le composant reste affiché (annulé par Dispose ou par le
    // changement de poste dans OnParametersSetAsync). InvokeAsync est nécessaire : ce code tourne
    // en dehors du contexte de synchronisation du circuit Blazor (le délai vient de PeriodicTimer,
    // pas d'un événement UI), donc RefreshAgentStatusAsync doit y être réintroduit explicitement
    // pour que StateHasChanged() soit valide.
    private async Task PollAgentStatusAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(AgentStatusPollInterval);
            do
            {
                await InvokeAsync(RefreshAgentStatusAsync);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _agentStatusPollCts?.Cancel();
        _agentStatusPollCts?.Dispose();
    }

    // Interroge l'interface web locale de l'agent (httpd-trust) sur /status, comme le fait GLPI.
    // Requête faite depuis le serveur (pas le navigateur) : le poste client n'est en général
    // joignable que depuis le réseau interne où tourne GlpiNg.Web, pas depuis le navigateur admin.
    private async Task RefreshAgentStatusAsync()
    {
        if (_agentStatusLoading || _computer?.Agent is not { } agent)
        {
            return;
        }

        string? url = AgentWebUrl(agent, _computer);
        if (url is null)
        {
            return;
        }

        _agentStatusLoading = true;
        StateHasChanged();

        try
        {
            using HttpClient client = HttpClientFactory.CreateClient("GlpiAgent");
            string result = await client.GetStringAsync($"{url}/status");
            string trimmed = result.Trim();
            if (trimmed.StartsWith("status: ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed["status: ".Length..];
            }
            _agentStatus = trimmed.Length > 0 ? trimmed : "—";
        }
        catch (Exception ex)
        {
            _agentStatus = $"Injoignable ({ex.Message})";
        }
        finally
        {
            _agentStatusLoading = false;
            // Nécessaire ici (contrairement à un handler @onclick, qui déclenche un rendu
            // automatiquement à la fin de sa tâche) : quand cette méthode est appelée depuis
            // PollAgentStatusAsync via InvokeAsync, rien d'autre ne rafraîchit l'UI en fin
            // d'exécution — sans ce StateHasChanged, le spinner reste visuellement bloqué à
            // "loading" jusqu'au prochain tick, qui le repasse à true avant que l'état false
            // n'ait jamais été rendu.
            StateHasChanged();
        }
    }

    // Déclenche un inventaire immédiat via l'interface web locale de l'agent (httpd-trust /now),
    // comme le fait GLPI. Même remarque que RefreshAgentStatusAsync : requête faite depuis le
    // serveur, pas le navigateur.
    private async Task RequestInventoryAsync()
    {
        if (_inventoryRequestLoading || _computer?.Agent is not { } agent)
        {
            return;
        }

        string? url = AgentWebUrl(agent, _computer);
        if (url is null)
        {
            return;
        }

        _inventoryRequestLoading = true;
        StateHasChanged();

        try
        {
            using HttpClient client = HttpClientFactory.CreateClient("GlpiAgent");
            using HttpResponseMessage response = await client.GetAsync($"{url}/now");
            _inventoryRequestResult = response.IsSuccessStatusCode
                ? DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                : $"Erreur ({(int)response.StatusCode})";
        }
        catch (Exception ex)
        {
            _inventoryRequestResult = $"Injoignable ({ex.Message})";
        }
        finally
        {
            _inventoryRequestLoading = false;
        }
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

    private static string CapacityLabel(int? capacityMwh) => capacityMwh is { } mwh ? $"{mwh / 1000.0:0.##} Wh" : "—";

    private static string RealCapacityLabel(int? realCapacityMwh) => realCapacityMwh is { } mwh ? $"{mwh / 1000.0:0.##} Wh" : "—";

    private static int? WearPercent(int? designCapacityMwh, int? realCapacityMwh) =>
        designCapacityMwh is > 0 && realCapacityMwh is { } real
            ? (int)Math.Round(real * 100.0 / designCapacityMwh.Value)
            : null;

    private static string WearBadgeCss(int? percent) => percent switch
    {
        null => "bg-secondary-lt",
        >= 80 => "bg-green-lt",
        >= 50 => "bg-yellow-lt",
        _ => "bg-red-lt"
    };

    private string FormatMac(string? mac) =>
        MacAddressFormatter.Format(mac, Configuration["GeneralSettings:MacAddressFormat"] ?? "Windows");

    private static string DhcpLabel(string? ipDhcp) => ipDhcp switch
    {
        null or "" => "—",
        "1" or "yes" or "true" => "Oui",
        "0" or "no" or "false" => "Non",
        _ => ipDhcp
    };

    private static string BoolLabel(bool? value) => value switch
    {
        null => "—",
        true => "Oui",
        false => "Non"
    };

    private static string BoolBadgeCss(bool? value) => value switch
    {
        null => "bg-secondary-lt",
        true => "bg-green-lt",
        false => "bg-red-lt"
    };

    private static string SpeedLabel(int? speedMbps) => speedMbps switch
    {
        null => "—",
        >= 1000 => $"{speedMbps.Value / 1000.0:0.##} Gb/s",
        _ => $"{speedMbps} Mb/s"
    };
}
