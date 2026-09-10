using GlpiNg.Modules.Abstractions.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// État partagé (singleton) de l'import GLPI MySQL, observé par
/// <c>GlpiMySqlImportPage</c> : tous les admins qui consultent la page voient le même
/// formulaire de connexion, la même analyse, la même sélection de cases à cocher, le
/// même import en cours et le même dernier résultat, plutôt qu'un état par circuit
/// Blazor (par défaut InteractiveServer instancie la page par circuit/onglet).
///
/// Flux en deux temps : <see cref="AnalyzeAsync"/> compte les éléments disponibles par
/// catégorie sans rien importer (et coche par défaut les catégories non vides) ; l'admin
/// ajuste éventuellement la sélection, puis <see cref="RunAsync"/> lance l'import réel
/// restreint aux catégories cochées. L'exécution reste déléguée à
/// <see cref="GlpiMySqlImportService"/> (scoped, dépend du DbContext) : ce singleton en
/// résout une instance via <see cref="IServiceScopeFactory"/> à chaque appel plutôt que
/// de la référencer directement.
/// </summary>
public sealed class GlpiImportStateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Répertoire des fichiers du plugin d'inventaire sur l'installation GLPI source, vu depuis la
    /// machine GlpiNg. Renseigné, l'import rapatrie le contenu des fichiers de paquets ; sinon ils
    /// sont seulement déclarés, à téléverser à la main.
    /// </summary>
    public string DeployFilesPath { get; set; } = string.Empty;

    /// <summary>
    /// Identifiants du partage réseau hébergeant <see cref="DeployFilesPath"/>, quand le compte
    /// sous lequel tourne GlpiNg n'y a pas accès. Gardés en mémoire le temps de la session, comme
    /// le mot de passe MySQL du même formulaire, et jamais persistés.
    /// </summary>
    public string DeployFilesUserName { get; set; } = string.Empty;

    /// <inheritdoc cref="DeployFilesUserName"/>
    public string DeployFilesPassword { get; set; } = string.Empty;

    /// <summary>Racine HTTP de GLPI, essayée en repli quand le répertoire n'est pas joignable.</summary>
    public string GlpiBaseUrl { get; set; } = string.Empty;

    public bool IsAnalyzing { get; private set; }
    public string? AnalysisError { get; private set; }
    public GlpiImportAnalysis? Analysis { get; private set; }
    public GlpiImportSelection Selection { get; } = new();

    /// <summary>
    /// Analyse/sélection "Administration" (entités/groupes/profils/utilisateurs) et configuration
    /// générale — voir <see cref="IGlpiAdminImportService"/> (implémenté par l'hôte). Distincte de
    /// <see cref="Analysis"/>/<see cref="Selection"/> (parc, module Inventory) car ce sont deux
    /// services différents, mais pilotée par la même page /admin/import/glpi et le même bouton
    /// "Analyser"/"Lancer l'import".
    /// </summary>
    public GlpiAdminImportAnalysis? AdminAnalysis { get; private set; }
    public GlpiAdminImportSelection AdminSelection { get; } = new();

    /// <summary>
    /// Sélection des données du plugin d'inventaire détecté sur la base source (voir
    /// <see cref="GlpiInventoryPluginInfo"/>). Distincte des deux autres pour la même raison :
    /// troisième service, même page. Rien n'est coché par défaut — contrairement au parc et à
    /// l'administration, ces données doublonnent un domaine que GlpiNg gère en propre, donc la
    /// reprise se demande explicitement.
    /// </summary>
    public GlpiPluginImportSelection PluginSelection { get; } = new();

    public bool IsRunning { get; private set; }

    /// <summary>Phase en cours, telle que nommée par <see cref="GlpiImportPhases"/>. Null hors import.</summary>
    public string? CurrentPhase { get; private set; }

    /// <summary>Éléments déjà traités, toutes phases confondues.</summary>
    public int ProgressCompleted { get; private set; }

    /// <summary>
    /// Éléments à traiter au total, calculés depuis l'analyse et la sélection au moment du
    /// lancement. Zéro quand rien n'est chiffrable — la barre est alors indéterminée plutôt que
    /// fausse.
    /// </summary>
    public int ProgressTotal { get; private set; }

    public int ProgressPercent => ProgressTotal > 0
        ? Math.Clamp((int)(100L * ProgressCompleted / ProgressTotal), 0, 100)
        : 0;

    public GlpiImportResult? LastResult { get; private set; }
    public string? LastError { get; private set; }
    public GlpiAdminImportResult? LastAdminResult { get; private set; }
    public string? LastAdminError { get; private set; }
    public GlpiPluginImportResult? LastPluginResult { get; private set; }
    public string? LastPluginError { get; private set; }
    public DateTime? LastRunAt { get; private set; }

    /// <summary>Levé après chaque changement d'état, pour que les pages abonnées se rafraîchissent (StateHasChanged).</summary>
    public event Action? Changed;

    public GlpiImportStateService(IServiceScopeFactory scopeFactory, IOptions<GlpiImportOptions> options)
    {
        _scopeFactory = scopeFactory;

        // Pré-remplit le formulaire depuis "GlpiImport:ConnectionString" (appsettings/user-secrets)
        // si une installation existante l'y configurait déjà — l'admin peut ensuite la modifier
        // depuis la page, ce qui devient alors la source utilisée pour les imports suivants.
        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            try
            {
                MySqlConnectionStringBuilder builder = new(options.Value.ConnectionString);
                Server = builder.Server;
                Database = builder.Database;
                Username = builder.UserID;
                Password = builder.Password;
            }
            catch (Exception)
            {
                // Chaîne malformée : le formulaire reste vide, l'admin la ressaisit depuis la page.
            }
        }
    }

    public bool CanAnalyze => !IsAnalyzing
        && !IsRunning
        && !string.IsNullOrWhiteSpace(Server)
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(Username);

    public bool CanRun => !IsRunning
        && !IsAnalyzing
        && Analysis is not null
        && (Selection.AnySelected || AdminSelection.AnySelected || PluginSelection.AnySelected);

    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze || !await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsAnalyzing = true;
            AnalysisError = null;
            Analysis = null;
            AdminAnalysis = null;
            LastResult = null;
            LastError = null;
            LastAdminResult = null;
            LastAdminError = null;
            LastPluginResult = null;
            LastPluginError = null;
            Changed?.Invoke();

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            GlpiMySqlImportService importService = scope.ServiceProvider.GetRequiredService<GlpiMySqlImportService>();
            IGlpiAdminImportService adminImportService = scope.ServiceProvider.GetRequiredService<IGlpiAdminImportService>();
            string connectionString = BuildConnectionString();

            try
            {
                GlpiImportAnalysis analysis = await importService.AnalyzeAsync(connectionString);
                Analysis = analysis;

                // Coche par défaut les catégories non vides : rien à gagner à laisser une case
                // cochée pour une catégorie que la base source n'a pas.
                Selection.ImportComputers = analysis.ComputersCount > 0;
                Selection.ImportMonitors = analysis.MonitorsCount > 0;
                Selection.ImportAgents = analysis.AgentsCount > 0;
                Selection.ImportComponents = analysis.ComponentsCount > 0;
                Selection.ImportSoftwares = analysis.SoftwaresCount > 0;
                Selection.ImportPrinters = analysis.PrintersCount > 0;
                Selection.ImportPeripherals = analysis.PeripheralsCount > 0;
                Selection.ImportVolumes = analysis.VolumesCount > 0;
                Selection.ImportBatteries = analysis.BatteriesCount > 0;

                GlpiAdminImportAnalysis adminAnalysis = await adminImportService.AnalyzeAsync(connectionString);
                AdminAnalysis = adminAnalysis;

                AdminSelection.ImportEntities = adminAnalysis.EntitiesCount > 0;
                AdminSelection.ImportGroups = adminAnalysis.GroupsCount > 0;
                AdminSelection.ImportProfiles = adminAnalysis.ProfilesCount > 0;
                AdminSelection.ImportUsers = adminAnalysis.UsersCount > 0;
                AdminSelection.ImportGeneralConfig = adminAnalysis.GeneralConfigAvailable;

                // L'adresse de téléchargement des fichiers de paquets vient de la base, pas de
                // l'administrateur : GLPI y range sa propre racine HTTP, et le plugin ses miroirs.
                // Le champ reste modifiable — on ne remplace pas une saisie existante.
                if (string.IsNullOrWhiteSpace(GlpiBaseUrl))
                {
                    GlpiBaseUrl = analysis.InventoryPlugin.UrlBase ?? string.Empty;
                }

                // Volontairement décochées même quand le plugin en contient : voir PluginSelection.
                PluginSelection.ImportIpRanges = false;
                PluginSelection.ImportSnmpCredentials = false;
                PluginSelection.ImportDeployPackages = false;
                PluginSelection.ImportUnmanagedDevices = false;
            }
            catch (Exception ex)
            {
                AnalysisError = ex.Message;
            }
        }
        finally
        {
            IsAnalyzing = false;
            _gate.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Volume annoncé par l'analyse pour chaque phase sélectionnée, dans l'ordre où l'import les
    /// traite. C'est ce plan qui donne son échelle à la barre : sans lui, une phase de dix mille
    /// ordinateurs et une de trois profils avanceraient d'autant, et la barre mentirait.
    /// </summary>
    private List<(string Phase, int Count)> _plan = [];

    /// <summary>Éléments des phases déjà terminées, pour n'ajouter que l'avancement de la phase en cours.</summary>
    private int _completedBeforeCurrentPhase;

    // Écrits par le thread qui exécute l'import, lus par les circuits qui affichent la barre. Pas
    // de verrou : ce sont des entiers et une référence de chaîne, et une lecture d'un cran en
    // retard sur une barre de progression n'a aucune conséquence.
    private DateTime _lastProgressNotifiedAt = DateTime.MinValue;

    /// <summary>
    /// Cadence de rafraîchissement de l'écran pendant l'import. Un rapport par élément traversant
    /// le circuit Blazor engorgerait la connexion sans rien apporter à l'œil : la barre est
    /// repeinte au plus quelques fois par seconde, et la fin de chaque phase est toujours notifiée.
    /// </summary>
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Rapporteur qui appelle son destinataire sur place, sans passer par un contexte de
    /// synchronisation.
    ///
    /// <see cref="Progress{T}"/> poste chaque rapport sur le contexte capturé à sa construction —
    /// ici le dispatcher du circuit Blazor. Un import de cent mille postes y déposerait cent mille
    /// messages, à traiter en concurrence du rendu, alors que la barre n'a besoin d'être repeinte
    /// que quelques fois par seconde. L'étranglement est donc fait en amont, dans
    /// <see cref="OnProgress"/>, et seul le rafraîchissement retenu franchit le dispatcher — via
    /// l'abonnement des pages à <see cref="Changed"/>, qui marshale déjà.
    /// </summary>
    private sealed class SynchronousProgress(Action<GlpiImportProgress> report) : IProgress<GlpiImportProgress>
    {
        public void Report(GlpiImportProgress value) => report(value);
    }

    private List<(string Phase, int Count)> BuildPlan()
    {
        if (Analysis is not { } analysis)
        {
            return [];
        }

        List<(string Phase, int Count)> plan = [];

        void Add(bool selected, string phase, int count)
        {
            if (selected)
            {
                // Une phase sélectionnée mais annoncée vide compte pour un : elle reste une étape
                // que l'import traverse, et la voir passer vaut mieux qu'un trou dans la liste.
                plan.Add((phase, Math.Max(count, 1)));
            }
        }

        // Même ordre que RunAsync : parc, puis administration, puis plugin.
        Add(Selection.ImportComputers, GlpiImportPhases.Computers, analysis.ComputersCount);
        Add(Selection.ImportAgents, GlpiImportPhases.Agents, analysis.AgentsCount);
        Add(Selection.ImportComponents, GlpiImportPhases.Components, analysis.ComponentsCount);
        Add(Selection.ImportMonitors, GlpiImportPhases.Monitors, analysis.MonitorsCount);
        Add(Selection.ImportSoftwares, GlpiImportPhases.Softwares, analysis.SoftwaresCount);
        Add(Selection.ImportPrinters, GlpiImportPhases.Printers, analysis.PrintersCount);
        Add(Selection.ImportPeripherals, GlpiImportPhases.Peripherals, analysis.PeripheralsCount);
        Add(Selection.ImportVolumes, GlpiImportPhases.Volumes, analysis.VolumesCount);
        Add(Selection.ImportBatteries, GlpiImportPhases.Batteries, analysis.BatteriesCount);

        if (AdminAnalysis is { } adminAnalysis)
        {
            Add(AdminSelection.ImportEntities, GlpiImportPhases.Entities, adminAnalysis.EntitiesCount);
            Add(AdminSelection.ImportGroups, GlpiImportPhases.Groups, adminAnalysis.GroupsCount);
            Add(AdminSelection.ImportProfiles, GlpiImportPhases.Profiles, adminAnalysis.ProfilesCount);
            Add(AdminSelection.ImportUsers, GlpiImportPhases.Users, adminAnalysis.UsersCount);
            Add(AdminSelection.ImportGeneralConfig, GlpiImportPhases.GeneralConfig, 1);
        }

        GlpiInventoryPluginInfo plugin = analysis.InventoryPlugin;
        Add(PluginSelection.ImportIpRanges, GlpiImportPhases.IpRanges, plugin.IpRangesCount);
        Add(PluginSelection.ImportSnmpCredentials, GlpiImportPhases.SnmpCredentials, plugin.SnmpCredentialsCount);
        Add(PluginSelection.ImportDeployPackages, GlpiImportPhases.DeployPackages, plugin.DeployPackagesCount);
        Add(PluginSelection.ImportUnmanagedDevices, GlpiImportPhases.UnmanagedDevices, plugin.UnmanagedDevicesCount);

        return plan;
    }

    /// <summary>
    /// Range un rapport d'avancement dans le plan. Un changement de phase clôt la précédente à son
    /// volume annoncé : les services rapportent ce qu'ils ont traité, pas ce que l'analyse avait
    /// compté, et les deux peuvent différer (une ligne ignorée, une table absente). Créditer la
    /// phase de son volume complet évite que ces écarts se cumulent et laissent la barre bloquée
    /// sous 100 %.
    /// </summary>
    private void OnProgress(GlpiImportProgress progress)
    {
        if (progress.Phase != CurrentPhase)
        {
            int index = _plan.FindIndex(step => step.Phase == progress.Phase);
            _completedBeforeCurrentPhase = index < 0
                ? _completedBeforeCurrentPhase
                : _plan.Take(index).Sum(step => step.Count);
            CurrentPhase = progress.Phase;
        }

        int planned = _plan.FirstOrDefault(step => step.Phase == progress.Phase).Count;
        int withinPhase = planned > 0 ? Math.Min(progress.Completed, planned) : progress.Completed;

        ProgressCompleted = _completedBeforeCurrentPhase + withinPhase;

        DateTime now = DateTime.UtcNow;
        if (now - _lastProgressNotifiedAt < ProgressNotifyInterval)
        {
            return;
        }

        _lastProgressNotifiedAt = now;
        Changed?.Invoke();
    }

    public async Task RunAsync()
    {
        if (!CanRun || !await _gate.WaitAsync(0))
        {
            // Analyse manquante/en cours, formulaire incomplet, rien coché, ou un import déjà en
            // cours (déclenché par un autre admin) : pas de double exécution.
            return;
        }

        try
        {
            IsRunning = true;
            LastError = null;
            LastAdminError = null;

            _plan = BuildPlan();
            _completedBeforeCurrentPhase = 0;
            _lastProgressNotifiedAt = DateTime.MinValue;
            ProgressTotal = _plan.Sum(step => step.Count);
            ProgressCompleted = 0;
            CurrentPhase = _plan.Count > 0 ? _plan[0].Phase : null;

            SynchronousProgress progress = new(OnProgress);
            Changed?.Invoke();

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            GlpiMySqlImportService importService = scope.ServiceProvider.GetRequiredService<GlpiMySqlImportService>();
            IGlpiAdminImportService adminImportService = scope.ServiceProvider.GetRequiredService<IGlpiAdminImportService>();
            string connectionString = BuildConnectionString();

            if (Selection.AnySelected)
            {
                try
                {
                    LastResult = await importService.RunAsync(connectionStringOverride: connectionString, selection: Selection, progress: progress);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    LastResult = null;
                }
            }

            if (AdminSelection.AnySelected)
            {
                try
                {
                    LastAdminResult = await adminImportService.RunAsync(connectionString, AdminSelection, progress);
                }
                catch (Exception ex)
                {
                    LastAdminError = ex.Message;
                    LastAdminResult = null;
                }
            }

            // Après l'administration : les données du plugin sont rattachées à l'entité racine,
            // qui doit exister — c'est l'import "Administration" qui crée les entités.
            if (PluginSelection.AnySelected && Analysis?.InventoryPlugin.TablePrefix is { Length: > 0 } tablePrefix)
            {
                try
                {
                    IGlpiInventoryPluginImportService pluginImportService =
                        scope.ServiceProvider.GetRequiredService<IGlpiInventoryPluginImportService>();

                    PluginSelection.DeployFilesPath = string.IsNullOrWhiteSpace(DeployFilesPath) ? null : DeployFilesPath.Trim();
                    PluginSelection.DeployFilesUserName = string.IsNullOrWhiteSpace(DeployFilesUserName) ? null : DeployFilesUserName.Trim();
                    PluginSelection.DeployFilesPassword = string.IsNullOrEmpty(DeployFilesPassword) ? null : DeployFilesPassword;
                    PluginSelection.GlpiBaseUrl = string.IsNullOrWhiteSpace(GlpiBaseUrl) ? null : GlpiBaseUrl.Trim();
                    PluginSelection.DeployMirrorUrls = Analysis?.InventoryPlugin.MirrorUrls ?? [];

                    LastPluginResult = await pluginImportService.RunAsync(connectionString, tablePrefix, PluginSelection, progress);
                }
                catch (Exception ex)
                {
                    LastPluginError = ex.Message;
                    LastPluginResult = null;
                }
            }

            LastRunAt = DateTime.UtcNow;
            ProgressCompleted = ProgressTotal;
            CurrentPhase = null;
        }
        finally
        {
            IsRunning = false;
            _gate.Release();
            Changed?.Invoke();
        }
    }

    private string BuildConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = Server,
        Database = Database,
        UserID = Username,
        Password = Password,
    }.ConnectionString;
}
