using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    // Optionnel : seul GlpiNg.Web l'enregistre (voir Program.cs), pour ne pas faire dépendre ce
    // module du module Déploiement — voir IComputerDeploymentTasksProvider.
    [Inject]
    private IComputerDeploymentTasksProvider? DeploymentTasksProvider { get; set; }

    // Optionnel pour la même raison que DeploymentTasksProvider ci-dessus — alimente l'onglet
    // "Déploiement de package" (assignation de paquets à l'agent du poste).
    [Inject]
    private IComputerDeploymentAssignmentService? DeploymentAssignmentService { get; set; }

    // Optionnel pour la même raison que DeploymentTasksProvider ci-dessus. Réutilisé ici tel quel
    // (nom "Deployment" trompeur pour cet usage, mais la forme — projection Id/Nom des comptes
    // utilisateurs — est générique) plutôt que de dupliquer une seconde abstraction identique,
    // juste pour l'autocomplétion du champ "Utilisateur assigné" (EnterEditModeAsync).
    [Inject]
    private IDeploymentTargetDirectory? UserDirectory { get; set; }

    [Inject]
    private WakeOnLanSender WakeOnLan { get; set; } = null!;

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
    private List<ComputerSoftware> _softwareFiltered = [];
    private List<ComputerSoftware> _softwarePaged = [];
    private string _softwareFilter = "";
    private int _softwarePage = 1;
    private int _softwarePageSize = 25;

    // Onglets dont le tableau doit occuper toute la hauteur du panneau (entête/pied fixes, seules
    // les lignes défilent) — voir .glpi-fiche-panel-fill dans glpi-theme.css.
    private static readonly HashSet<string> FillPanelTabKeys = ["software", "importinfo", "history"];

    private static readonly int[] HistoryPageSizeOptions = [25, 50, 100, 200];
    /// <summary>
    /// Verrous du poste, indexés par champ : l'inventaire ne met plus à jour ces champs (voir
    /// LockedField). La ligne entière est conservée, et pas seulement le nom du champ, parce que
    /// l'onglet « Verrous » affiche aussi qui a posé le verrou et depuis quand.
    /// </summary>
    private Dictionary<string, LockedField> _locks = new(StringComparer.Ordinal);

    private bool _isWaking;
    private string? _wakeOnLanMessage;
    private string? _wakeOnLanDetail;
    private bool _wakeOnLanFailed;

    private List<ComputerHistoryEntry> _historySorted = [];
    private List<ComputerHistoryEntry> _historyFiltered = [];
    private List<ComputerHistoryEntry> _historyPaged = [];
    private string _historyFilter = "";
    private int _historyPage = 1;
    private int _historyPageSize = 25;

    private List<ComputerImportHistory> _importHistorySorted = [];
    private List<ComputerImportHistory> _importHistoryFiltered = [];
    private string _importHistoryFilter = "";
    private int _loadedComputerId;
    private string? _agentStatus;
    private bool _agentStatusLoading;
    private string? _inventoryRequestResult;
    private bool _inventoryRequestLoading;
    private string? _hostTasksRequestResult;
    private bool _hostTasksRequestLoading;
    private CancellationTokenSource? _agentStatusPollCts;
    private CancellationTokenSource? _deploymentAssignmentsPollCts;
    private bool _isReloading;

    // Édition des champs texte libre alimentés par les Intitulés (Manufacturer/ChassisType/Model/
    // OperatingSystem/OsVersion — voir Models/DropdownItem.cs) : la fiche reste en lecture seule
    // par défaut (comme le reste de Detail.razor), ce panneau de champs bascule seul en édition via
    // le bouton "Modifier" du menu d'actions.
    private bool _editMode;
    private string? _editManufacturer;
    private string? _editChassisType;
    private string? _editModel;
    private string? _editOperatingSystem;
    private string? _editOsVersion;
    private int? _editStatusId;
    private int? _editLocationId;
    private string? _editAssignedUser;
    private bool _editSaving;
    private List<string> _manufacturerOptions = [];
    private List<string> _computerTypeOptions = [];
    private List<string> _computerModelOptions = [];
    private List<string> _operatingSystemOptions = [];
    private List<string> _operatingSystemVersionOptions = [];
    private List<DropdownItem> _statusOptions = [];
    private List<DropdownItem> _locationOptions = [];
    private List<string> _userOptions = [];

    private ComputerDeploymentTasksInfo? _deploymentTasksInfo;
    private List<DeploymentPackageOption> _availablePackages = [];
    private List<ComputerDeploymentAssignment> _deploymentAssignments = [];
    private List<DeploymentPackageOption> _selectedPackagesToAssign = [];
    private DeploymentWakeMode _wakeMode = DeploymentWakeMode.None;
    private bool _prepareInstallLoading;
    private string? _prepareInstallError;
    private int? _expandedAssignmentJobId;
    private int? _retryingJobId;

    private enum DeploymentWakeMode
    {
        None,
        Local,
        Remote
    }

    private static readonly TimeSpan AgentStatusPollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DeploymentAssignmentsPollInterval = TimeSpan.FromSeconds(5);
    private int SoftwareTotalPages => _softwareFiltered.Count == 0 ? 1 : (int)Math.Ceiling(_softwareFiltered.Count / (double)_softwarePageSize);
    private int HistoryTotalPages => _historyFiltered.Count == 0 ? 1 : (int)Math.Ceiling(_historyFiltered.Count / (double)_historyPageSize);

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
        _editMode = false;
        _softwarePage = 1;
        _softwareFilter = "";
        _historyPage = 1;
        _historyFilter = "";
        _importHistoryFilter = "";
        _agentStatus = null;
        _wakeOnLanMessage = null;
        _wakeOnLanDetail = null;
        _inventoryRequestResult = null;
        _hostTasksRequestResult = null;
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
        ApplySoftwareFilter();

        _historySorted = _computer.HistoryEntries.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).ToList();
        ApplyHistoryFilter();

        await LoadLocksAsync(db);

        _importHistorySorted = _computer.ImportHistories.OrderByDescending(e => e.OccurredAt).ToList();
        ApplyImportHistoryFilter();

        _deploymentAssignmentsPollCts?.Cancel();
        _deploymentAssignmentsPollCts?.Dispose();
        _deploymentAssignmentsPollCts = null;

        _deploymentTasksInfo = DeploymentTasksProvider is null
            ? null
            : await DeploymentTasksProvider.GetForComputerAsync(ComputerId);
        await LoadDeploymentAssignmentsAsync();
        _tabs = BuildTabs(_computer, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);

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
            .Include(computer => computer.Connectors)
            .Include(computer => computer.Antiviruses)
            .Include(computer => computer.ImportHistories)
            .Include(computer => computer.HistoryEntries)
            .Include(computer => computer.Agent)
            .Include(computer => computer.StatusItem)
            .Include(computer => computer.LocationItem)
            .FirstOrDefaultAsync(computer => computer.Id == ComputerId);

    // Recharge la fiche depuis la base sans réinitialiser l'onglet actif ni la pagination des
    // logiciels, contrairement à OnParametersSetAsync (qui, lui, correspond à une navigation vers
    // un autre poste). Déclenché par le bouton de rechargement manuel de la barre du haut.
    private bool IsFieldLocked(string field) => _locks.ContainsKey(field);

    private LockedField? LockOf(string field) => _locks.GetValueOrDefault(field);

    private async Task LoadLocksAsync(DbContext db)
    {
        if (_computer is null)
        {
            return;
        }

        _locks = (await db.Set<LockedField>()
                .AsNoTracking()
                .Where(locked => locked.ItemType == ComputerLockableFields.ItemType && locked.ItemId == _computer.Id)
                .ToListAsync())
            .ToDictionary(locked => locked.Field, StringComparer.Ordinal);
    }

    /// <summary>
    /// Valeur courante d'un champ verrouillable, telle qu'affichée dans l'onglet « Verrous ».
    /// Sans elle, la liste des verrous ne dirait pas ce qu'ils protègent — or c'est précisément
    /// la question qu'on se pose en les relisant.
    /// </summary>
    private string CurrentFieldValue(string field)
    {
        if (_computer is null)
        {
            return "—";
        }

        string? value = field switch
        {
            "Name" => _computer.Name,
            "SerialNumber" => _computer.SerialNumber,
            "Manufacturer" => _computer.Manufacturer,
            "Model" => _computer.Model,
            "OperatingSystem" => _computer.OperatingSystem,
            "OsVersion" => _computer.OsVersion,
            "OsKernelVersion" => _computer.OsKernelVersion,
            "ChassisType" => _computer.ChassisType,
            "HardwareUuid" => _computer.HardwareUuid,
            "Domain" => _computer.Domain,
            "VmSystem" => _computer.VmSystem,
            "LastLoggedUser" => _computer.LastLoggedUser,
            "TotalMemoryMb" => _computer.TotalMemoryMb is { } mb ? $"{mb} Mo" : null,
            "RemoteManagement" => _computer.RemoteManagementId is { Length: > 0 } id
                ? (_computer.RemoteManagementType is { Length: > 0 } type ? $"{type} : {id}" : id)
                : null,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    /// <summary>
    /// Pose ou retire le verrou d'un champ. La bascule est tracée dans l'historique du poste : un
    /// champ qui cesse d'être alimenté par l'inventaire doit pouvoir s'expliquer des mois plus
    /// tard, sans quoi la fiche semble simplement ne plus se mettre à jour.
    /// </summary>
    private async Task ToggleFieldLockAsync(string field)
    {
        if (_computer is null || !ComputerLockableFields.IsLockable(field))
        {
            return;
        }

        string userName = await CurrentUserNameAsync();

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        LockedField? existing = await db.Set<LockedField>()
            .FirstOrDefaultAsync(locked => locked.ItemType == ComputerLockableFields.ItemType
                                           && locked.ItemId == _computer.Id
                                           && locked.Field == field);

        string label = ComputerLockableFields.Label(field);

        if (existing is not null)
        {
            db.Set<LockedField>().Remove(existing);
        }
        else
        {
            db.Set<LockedField>().Add(new LockedField
            {
                ItemType = ComputerLockableFields.ItemType,
                ItemId = _computer.Id,
                Field = field,
                LockedBy = userName,
            });
        }

        db.Set<ComputerHistoryEntry>().Add(new ComputerHistoryEntry
        {
            ComputerId = _computer.Id,
            User = userName,
            Field = "Verrou",
            Description = existing is not null
                ? $"Verrou retiré sur « {label} » : l'inventaire peut de nouveau le mettre à jour."
                : $"Verrou posé sur « {label} » : l'inventaire ne le met plus à jour.",
        });

        await db.SaveChangesAsync();
        await ReloadComputerAsync();
    }

    /// <summary>Le réveil n'a de sens que si l'inventaire réseau a remonté au moins une MAC valide.</summary>
    private bool HasWakeableMac => _computer is not null
        && _computer.NetworkPorts.Any(port => WakeOnLanSender.NormalizeMac(port.MacAddress) is not null);

    private async Task<string> CurrentUserNameAsync()
    {
        if (AuthStateTask is null)
        {
            return "Système";
        }

        AuthenticationState authState = await AuthStateTask;
        return authState.User.Identity?.Name is { Length: > 0 } name ? name : "Système";
    }

    /// <summary>
    /// Envoie un magic packet sur toutes les MAC connues du poste, depuis le serveur.
    ///
    /// Le résultat est affiché en clair (MAC visées, adresses de diffusion) parce qu'un réveil qui
    /// échoue ne se voit pas : la trame part sans accusé de réception, et une machine qui ne se
    /// rallume pas ne dit pas si c'est le routeur qui a filtré le broadcast, la carte réseau qui
    /// n'est pas armée, ou le BIOS. Sans ce détail, le bouton serait indébogable.
    /// </summary>
    private async Task SendWakeOnLanAsync()
    {
        if (_computer is null || _isWaking)
        {
            return;
        }

        _isWaking = true;
        _wakeOnLanMessage = null;
        _wakeOnLanDetail = null;
        _wakeOnLanFailed = false;

        try
        {
            List<WakeOnLanNic> nics = [.. _computer.NetworkPorts
                .Select(port => new WakeOnLanNic(port.MacAddress, port.IpAddress, port.IpMask))];

            WakeOnLanSendResult result = await WakeOnLan.SendAsync(nics);

            if (result.Macs.Count == 0)
            {
                _wakeOnLanFailed = true;
                _wakeOnLanMessage = "Aucune adresse MAC exploitable sur ce poste : le réveil est impossible tant que l'inventaire réseau n'a rien remonté.";
                return;
            }

            _wakeOnLanFailed = !result.Sent;
            _wakeOnLanMessage = result.Sent
                ? $"Magic packet envoyé sur {result.Macs.Count} adresse(s) MAC ({result.PacketsSent} datagramme(s))."
                : "Aucun datagramme n'a pu être émis.";
            _wakeOnLanDetail = $"MAC : {string.Join(", ", result.Macs)} — diffusion : {string.Join(", ", result.Broadcasts)}"
                + (result.Errors.Count > 0 ? $" — erreurs : {string.Join(" ; ", result.Errors)}" : string.Empty);

            string userName = await CurrentUserNameAsync();

            await using DbContext db = await DbFactory.CreateDbContextAsync();
            db.Set<ComputerHistoryEntry>().Add(new ComputerHistoryEntry
            {
                ComputerId = _computer.Id,
                User = userName,
                Field = "Wake-on-LAN",
                Description = result.Sent
                    ? $"Réveil demandé depuis le serveur sur {result.Macs.Count} adresse(s) MAC : {string.Join(", ", result.Macs)}."
                    : $"Réveil tenté sans succès : {string.Join(" ; ", result.Errors)}",
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _wakeOnLanFailed = true;
            _wakeOnLanMessage = $"Échec de l'envoi du magic packet : {ex.Message}";
        }
        finally
        {
            _isWaking = false;
        }
    }

    private void DismissWakeOnLanMessage()
    {
        _wakeOnLanMessage = null;
        _wakeOnLanDetail = null;
    }

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
                ApplySoftwareFilter();

                _historySorted = _computer.HistoryEntries.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).ToList();
                ApplyHistoryFilter();

                _importHistorySorted = _computer.ImportHistories.OrderByDescending(e => e.OccurredAt).ToList();
                ApplyImportHistoryFilter();

                // Sans ce rechargement, poser un verrou l'enregistrait sans que le cadenas change
                // d'état : ToggleFieldLockAsync passe par ici, et la fiche continuait d'afficher
                // les verrous lus à l'ouverture de la page.
                await LoadLocksAsync(db);

                _deploymentTasksInfo = DeploymentTasksProvider is null
                    ? null
                    : await DeploymentTasksProvider.GetForComputerAsync(ComputerId);
                await LoadDeploymentAssignmentsAsync();
                _tabs = BuildTabs(_computer, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);
            }
        }
        finally
        {
            _isReloading = false;
            StateHasChanged();
        }
    }

    // Bascule le panneau "Ordinateur"/"Systèmes d'exploitation" en édition : recopie les valeurs
    // actuelles dans les champs d'édition et charge les valeurs déjà connues de chaque Intitulé
    // pour l'autocomplétion (datalist), voir SaveEditAsync pour la création à la volée d'une
    // valeur absente de la liste.
    private async Task EnterEditModeAsync()
    {
        if (_computer is null) return;

        _editManufacturer = _computer.Manufacturer;
        _editChassisType = _computer.ChassisType;
        _editModel = _computer.Model;
        _editOperatingSystem = _computer.OperatingSystem;
        _editOsVersion = _computer.OsVersion;
        _editStatusId = _computer.StatusId;
        _editLocationId = _computer.LocationId;
        _editAssignedUser = _computer.AssignedUser;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DropdownItem> items = await db.Set<DropdownItem>()
            .AsNoTracking()
            .Where(i => i.Type == DropdownType.Manufacturer || i.Type == DropdownType.ComputerType
                || i.Type == DropdownType.ComputerModel || i.Type == DropdownType.OperatingSystem
                || i.Type == DropdownType.OperatingSystemVersion || i.Type == DropdownType.Status
                || i.Type == DropdownType.Location)
            .OrderBy(i => i.Name)
            .ToListAsync();

        _userOptions = UserDirectory is null
            ? []
            : (await UserDirectory.GetUsersAsync()).Select(u => u.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        _manufacturerOptions = items.Where(i => i.Type == DropdownType.Manufacturer).Select(i => i.Name).ToList();
        _computerTypeOptions = items.Where(i => i.Type == DropdownType.ComputerType).Select(i => i.Name).ToList();
        _computerModelOptions = items.Where(i => i.Type == DropdownType.ComputerModel).Select(i => i.Name).ToList();
        _operatingSystemOptions = items.Where(i => i.Type == DropdownType.OperatingSystem).Select(i => i.Name).ToList();
        _operatingSystemVersionOptions = items.Where(i => i.Type == DropdownType.OperatingSystemVersion).Select(i => i.Name).ToList();
        _statusOptions = items.Where(i => i.Type == DropdownType.Status).ToList();
        _locationOptions = items.Where(i => i.Type == DropdownType.Location).ToList();

        _editMode = true;
    }

    private void CancelEdit()
    {
        _editMode = false;
    }

    // Enregistre les champs édités et crée à la volée, dans la liste d'Intitulé correspondante,
    // toute valeur saisie qui n'y figure pas encore (comparaison insensible à la casse — même
    // contrainte que l'index unique (Type, Name) de DropdownItem, voir GlpiNgDbContext) : la
    // prochaine édition (sur ce poste ou un autre) la retrouvera dans la liste de suggestions.
    private async Task SaveEditAsync()
    {
        if (_computer is null || _editSaving) return;

        _editSaving = true;
        StateHasChanged();

        try
        {
            await using DbContext db = await DbFactory.CreateDbContextAsync();

            (DropdownType Type, string? Value)[] fields =
            [
                (DropdownType.Manufacturer, _editManufacturer),
                (DropdownType.ComputerType, _editChassisType),
                (DropdownType.ComputerModel, _editModel),
                (DropdownType.OperatingSystem, _editOperatingSystem),
                (DropdownType.OperatingSystemVersion, _editOsVersion),
            ];

            foreach ((DropdownType type, string? value) in fields)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                bool exists = await db.Set<DropdownItem>()
                    .AnyAsync(i => i.Type == type && i.Name.ToLower() == value.ToLower());
                if (!exists)
                {
                    db.Set<DropdownItem>().Add(new DropdownItem { Type = type, Name = value });
                }
            }

            // Utilisateur assigné : autocomplétion sur les comptes existants (voir UserDirectory,
            // Detail.razor.cs.EnterEditModeAsync), mais champ texte libre comme Manufacturer/Model
            // ci-dessus — pas de FK, pour ne pas toucher au matching par nom normalisé de
            // SelfServiceDeploymentService (module Déploiement, hors de portée ici).

            Computer? tracked = await db.Set<Computer>().FirstOrDefaultAsync(c => c.Id == ComputerId);
            if (tracked is not null)
            {
                tracked.Manufacturer = _editManufacturer;
                tracked.ChassisType = _editChassisType;
                tracked.Model = _editModel;
                tracked.OperatingSystem = _editOperatingSystem;
                tracked.OsVersion = _editOsVersion;
                tracked.StatusId = _editStatusId;
                tracked.LocationId = _editLocationId;
                tracked.AssignedUser = _editAssignedUser;
            }

            await db.SaveChangesAsync();

            _editMode = false;
            await ReloadComputerAsync();
        }
        finally
        {
            _editSaving = false;
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

        EnsureDeploymentAssignmentsPollingIfNeeded();
    }

    // Tant qu'au moins une assignation de paquet n'est pas terminée (statut différent de
    // Réussi/En erreur, i.e. CompletedAtUtc null), interroge la base toutes les
    // DeploymentAssignmentsPollInterval pour refléter la progression rapportée par l'agent via
    // setStatus (voir AgentController.HandleSetStatusAsync) sans que l'admin ait à recharger la
    // page. Idempotente : appelée après chaque LoadDeploymentAssignmentsAsync (y compris depuis
    // la boucle de poll elle-même), elle ne relance pas de boucle si une tourne déjà
    // (_deploymentAssignmentsPollCts non nul) — la boucle en cours s'arrête d'elle-même (voir
    // PollDeploymentAssignmentsAsync) une fois qu'aucune assignation n'est plus en attente.
    private void EnsureDeploymentAssignmentsPollingIfNeeded()
    {
        if (_deploymentAssignmentsPollCts is not null || !_deploymentAssignments.Exists(a => a.CompletedAtUtc is null))
        {
            return;
        }

        _deploymentAssignmentsPollCts = new CancellationTokenSource();
        _ = PollDeploymentAssignmentsAsync(_deploymentAssignmentsPollCts.Token);
    }

    private async Task PollDeploymentAssignmentsAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(DeploymentAssignmentsPollInterval);
            while (await timer.WaitForNextTickAsync(token))
            {
                await InvokeAsync(async () =>
                {
                    await LoadDeploymentAssignmentsAsync();
                    _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);
                    StateHasChanged();
                });

                if (!_deploymentAssignments.Exists(a => a.CompletedAtUtc is null))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _deploymentAssignmentsPollCts?.Cancel();
            _deploymentAssignmentsPollCts?.Dispose();
            _deploymentAssignmentsPollCts = null;
        }
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
    // DeploymentGeneralSettings.MaxAgentsToWakePerTask) : les deux modes de réveil s'appuient donc
    // sur l'interface web locale de l'agent (httpd-trust) plutôt que sur un vrai WOL — le "réveil
    // local" y accède via le hostname/HTTP (comme RequestInventoryAsync), le "réveil à distance" via
    // la dernière IP de contact connue/HTTPS, avec ?task=deploy pour ne déclencher que le déploiement.
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
                _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);

                _prepareInstallError = _wakeMode switch
                {
                    DeploymentWakeMode.Local => await TriggerLocalAgentWakeAsync(),
                    DeploymentWakeMode.Remote => await TriggerRemoteAgentWakeAsync(),
                    _ => null
                };
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

    // Variante "réveil à distance" : on n'est pas sur le poste, donc pas de hostname fiable à
    // résoudre depuis le navigateur/réseau de l'admin — on cible plutôt la dernière IP de contact
    // connue de l'agent, en HTTPS (voir agent.LastContactIp, déjà affiché sous "Adresse publique de
    // contact"), et ?task=deploy pour ne déclencher que la tâche de déploiement plutôt qu'un
    // inventaire complet.
    private async Task<string?> TriggerRemoteAgentWakeAsync()
    {
        if (_computer?.Agent is not { } agent)
        {
            return "Aucun agent associé à ce poste.";
        }

        string? url = AgentRemoteWebUrl(agent);
        if (url is null)
        {
            return "L'adresse IP de contact de l'agent est inconnue, impossible de le réveiller à distance.";
        }

        try
        {
            using HttpClient client = HttpClientFactory.CreateClient("GlpiAgent");
            using HttpResponseMessage response = await client.GetAsync($"{url}/now?task=deploy");
            return response.IsSuccessStatusCode
                ? null
                : $"Les paquets ont été assignés, mais le réveil à distance de l'agent a échoué ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"Les paquets ont été assignés, mais l'agent est injoignable pour le réveil à distance ({ex.Message}).";
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
        _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);
        StateHasChanged();
    }

    // Relance un job terminé (Réussi/En erreur) : le remet en attente puis réveille l'agent selon
    // le mode actuellement sélectionné dans le panneau "Préparer l'installation" juste en dessous
    // (_wakeMode — même dropdown, même logique que PrepareInstallationAsync), pour que l'agent
    // vienne chercher ce job fraîchement remis en attente sans attendre son prochain contact
    // périodique. Erreur affichée dans le même emplacement que celle du panneau d'installation.
    private async Task RetryAssignmentAsync(int jobId)
    {
        if (DeploymentAssignmentService is null || _retryingJobId is not null)
        {
            return;
        }

        _retryingJobId = jobId;
        _prepareInstallError = null;
        StateHasChanged();

        try
        {
            bool retried = await DeploymentAssignmentService.RetryAssignmentAsync(jobId);
            if (!retried)
            {
                return;
            }

            await LoadDeploymentAssignmentsAsync();
            _tabs = BuildTabs(_computer!, _deploymentTasksInfo, _deploymentAssignments.Count, _locks.Count);

            _prepareInstallError = _wakeMode switch
            {
                DeploymentWakeMode.Local => await TriggerLocalAgentWakeAsync(),
                DeploymentWakeMode.Remote => await TriggerRemoteAgentWakeAsync(),
                _ => null
            };
        }
        finally
        {
            _retryingJobId = null;
            StateHasChanged();
        }
    }

    private void ToggleAssignmentLog(int jobId)
    {
        _expandedAssignmentJobId = _expandedAssignmentJobId == jobId ? null : jobId;
    }

    // Même mise en forme que TaskDetail.RenderLog (module Déploiement) : GlpiNg.Modules.Inventory
    // ne référence jamais GlpiNg.Modules.Deployment (seulement son abstraction, voir
    // IComputerDeploymentAssignmentService), le rendu est donc dupliqué ici plutôt que partagé.
    // Journal ligne par ligne "[HH:mm:ss] [phase] message", horodatage/phase stylés à part et
    // ligne entière colorée selon son issue (ok/succès en vert, ko/erreur en rouge, séparateurs
    // "====" atténués) — voir les classes .glpi-log-* dans glpi-theme.css.
    private static readonly Regex LogLinePrefixRegex = new(
        @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?:\[(?<tag>[a-zA-Z]+)\]\s*)?(?<rest>.*)$",
        RegexOptions.Compiled);

    private static MarkupString RenderLog(string log)
    {
        StringBuilder html = new();

        foreach (string rawLine in log.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            string lineClass = trimmed.Length > 0 && trimmed.All(c => c == '=')
                ? "glpi-log-line glpi-log-sep"
                : Regex.IsMatch(line, @"\(ok\)\s*$", RegexOptions.IgnoreCase) || line.Contains("success", StringComparison.OrdinalIgnoreCase)
                    ? "glpi-log-line glpi-log-ok"
                    : Regex.IsMatch(line, @"\(ko\)\s*$", RegexOptions.IgnoreCase)
                      || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                      || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                        ? "glpi-log-line glpi-log-error"
                        : "glpi-log-line";

            html.Append("<div class=\"").Append(lineClass).Append("\">");

            Match match = LogLinePrefixRegex.Match(line);
            if (match.Success)
            {
                html.Append("<span class=\"glpi-log-time\">[").Append(WebUtility.HtmlEncode(match.Groups["time"].Value)).Append("]</span> ");
                if (match.Groups["tag"].Success)
                {
                    html.Append("<span class=\"glpi-log-tag\">[").Append(WebUtility.HtmlEncode(match.Groups["tag"].Value)).Append("]</span> ");
                }

                html.Append(WebUtility.HtmlEncode(match.Groups["rest"].Value));
            }
            else
            {
                html.Append(WebUtility.HtmlEncode(line));
            }

            html.Append("</div>");
        }

        return new MarkupString(html.ToString());
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
    // "networkports", "connectors", "antivirus", "locks", "domains", "importinfo", "history",
    // "tasks" et "deploy" ont un contenu réel pour l'instant (voir le @switch de Detail.razor) ;
    // les autres affichent un placeholder en attendant d'être alimentés au fur et à mesure des
    // besoins.
    private static List<FicheTab> BuildTabs(Computer computer, ComputerDeploymentTasksInfo? deploymentTasksInfo, int deploymentAssignmentsCount, int lockedFieldsCount) =>
    [
        new("computer", "ti-device-desktop", "Ordinateur", null),
        new("os", "ti-settings-cog", "Systèmes d'exploitation", computer.OperatingSystem is null ? 0 : 1),
        new("components", "ti-cpu", "Composants", computer.Components.Count),
        new("batteries", "ti-battery", "Batteries", computer.Batteries.Count),
        new("volumes", "ti-server-2", "Volumes", computer.Volumes.Count),
        new("software", "ti-apps", "Logiciels", computer.Softwares.Count),
        new("connections", "ti-plug-connected", "Connexions", computer.Peripherals.Count),
        new("networkports", "ti-network", "Ports réseau", computer.NetworkPorts.Count),
        new("connectors", "ti-usb", "Connecteurs", computer.Connectors.Count),
        new("remotecontrol", "ti-device-desktop-share", "Contrôle à distance", null),
        new("antivirus", "ti-shield-check", "Antivirus", computer.Antiviruses.Count),
        new("locks", "ti-lock", "Verrous", lockedFieldsCount),
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
        _softwarePaged = _softwareFiltered
            .Skip((_softwarePage - 1) * _softwarePageSize)
            .Take(_softwarePageSize)
            .ToList();
    }

    // Filtre par nom/version/éditeur (champ de recherche de l'entête du bloc "Logiciels").
    // Séparé de ApplySoftwarePaging pour pouvoir être réappliqué après un rechargement des
    // données (ReloadComputerAsync) sans perdre la page courante, contrairement à la saisie
    // dans le champ (OnSoftwareFilterInput) qui revient toujours en page 1.
    private void ApplySoftwareFilter()
    {
        _softwareFiltered = string.IsNullOrWhiteSpace(_softwareFilter)
            ? _softwareSorted
            : _softwareSorted.Where(s =>
                (s.Name?.Contains(_softwareFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Version?.Contains(_softwareFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Publisher?.Contains(_softwareFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        ApplySoftwarePaging();
    }

    private void OnSoftwareFilterInput(ChangeEventArgs e)
    {
        _softwareFilter = (string?)e.Value ?? "";
        _softwarePage = 1;
        ApplySoftwareFilter();
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

    private void ApplyHistoryPaging()
    {
        _historyPage = Math.Clamp(_historyPage, 1, HistoryTotalPages);
        _historyPaged = _historyFiltered
            .Skip((_historyPage - 1) * _historyPageSize)
            .Take(_historyPageSize)
            .ToList();
    }

    // Filtre par utilisateur/champ/description (champ de recherche de l'entête du bloc
    // "Historique"). Voir ApplySoftwareFilter pour le même principe côté onglet "Logiciels".
    private void ApplyHistoryFilter()
    {
        _historyFiltered = string.IsNullOrWhiteSpace(_historyFilter)
            ? _historySorted
            : _historySorted.Where(e =>
                e.User.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)
                || e.Field.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)
                || e.Description.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        ApplyHistoryPaging();
    }

    private void OnHistoryFilterInput(ChangeEventArgs e)
    {
        _historyFilter = (string?)e.Value ?? "";
        _historyPage = 1;
        ApplyHistoryFilter();
    }

    // Onglet "Informations d'import" : pas de pagination (liste généralement courte), seulement
    // un filtre — voir .glpi-fiche-panel-fill pour le remplissage en hauteur du bloc.
    private void ApplyImportHistoryFilter()
    {
        _importHistoryFiltered = string.IsNullOrWhiteSpace(_importHistoryFilter)
            ? _importHistorySorted
            : _importHistorySorted.Where(e =>
                e.RuleName.Contains(_importHistoryFilter, StringComparison.OrdinalIgnoreCase)
                || e.Module.Contains(_importHistoryFilter, StringComparison.OrdinalIgnoreCase)
                || (e.AgentIdentifier?.Contains(_importHistoryFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.InputValue?.Contains(_importHistoryFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
    }

    private void OnImportHistoryFilterInput(ChangeEventArgs e)
    {
        _importHistoryFilter = (string?)e.Value ?? "";
        ApplyImportHistoryFilter();
    }

    private void SetHistoryPageSize(int size)
    {
        if (_historyPageSize == size) return;
        _historyPageSize = size;
        _historyPage = 1;
        ApplyHistoryPaging();
    }

    private void GoToHistoryPage(int page)
    {
        int target = Math.Clamp(page, 1, HistoryTotalPages);
        if (target == _historyPage) return;
        _historyPage = target;
        ApplyHistoryPaging();
    }

    // Priorité à l'Emplacement choisi manuellement (LocationItem, Intitulé) sur Site/Building/Room
    // (renseignés par l'inventaire automatique) : voir Computer.LocationId.
    private static string? LocationLabel(Computer computer)
    {
        if (computer.LocationItem is { } location) return location.Name;

        var parts = new[] { computer.Site, computer.Building, computer.Room }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(" > ", parts);
        return label.Length > 0 ? label : null;
    }

    // Chemin complet ("Site > Bâtiment > Salle") d'un Lieu dans le select d'édition, en remontant
    // ParentId au sein de la liste déjà chargée (all) — jamais de requête supplémentaire, borné à
    // 20 niveaux comme garde-fou contre un cycle accidentel (voir aussi DropdownList.razor.cs.Depth).
    private static string LocationOptionLabel(DropdownItem item, List<DropdownItem> all)
    {
        Dictionary<int, DropdownItem> byId = all.ToDictionary(i => i.Id);
        List<string> parts = [item.Name];
        int? parentId = item.ParentId;
        int guard = 0;

        while (parentId is int pid && byId.TryGetValue(pid, out DropdownItem? parent) && guard++ < 20)
        {
            parts.Insert(0, parent.Name);
            parentId = parent.ParentId;
        }

        return string.Join(" > ", parts);
    }

    /// <summary>Port par défaut de l'interface web locale de GLPI-Agent (httpd-trust).</summary>
    private const int AgentWebPort = 62354;

    private static string? AgentWebUrl(GlpiAgent agent, Computer computer)
    {
        string? hostname = agent.Hostname ?? computer.Name;
        return string.IsNullOrWhiteSpace(hostname) ? null : $"http://{hostname}:{AgentWebPort}";
    }

    private static string? AgentRemoteWebUrl(GlpiAgent agent) =>
        string.IsNullOrWhiteSpace(agent.LastContactIp) ? null : $"https://{agent.LastContactIp}:{AgentWebPort}";

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
        _deploymentAssignmentsPollCts?.Cancel();
        _deploymentAssignmentsPollCts?.Dispose();
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
            using HttpResponseMessage response = await client.GetAsync($"{url}/now?task=inventory");
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

    // Déclenche l'ensemble des tâches planifiées de l'agent (httpd-trust /now, sans filtre "task"),
    // à la différence de RequestInventoryAsync qui ne cible que la tâche d'inventaire.
    private async Task RequestHostTasksAsync()
    {
        if (_hostTasksRequestLoading || _computer?.Agent is not { } agent)
        {
            return;
        }

        string? url = AgentWebUrl(agent, _computer);
        if (url is null)
        {
            return;
        }

        _hostTasksRequestLoading = true;
        StateHasChanged();

        try
        {
            using HttpClient client = HttpClientFactory.CreateClient("GlpiAgent");
            using HttpResponseMessage response = await client.GetAsync($"{url}/now");
            _hostTasksRequestResult = response.IsSuccessStatusCode
                ? DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                : $"Erreur ({(int)response.StatusCode})";
        }
        catch (Exception ex)
        {
            _hostTasksRequestResult = $"Injoignable ({ex.Message})";
        }
        finally
        {
            _hostTasksRequestLoading = false;
        }
    }

    private static string LastInventoryLabel(Computer computer)
    {
        return computer.LastInventoryAt is { } lastInventory
            ? lastInventory.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "Jamais";
    }

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
