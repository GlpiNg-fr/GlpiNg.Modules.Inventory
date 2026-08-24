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

    public bool IsRunning { get; private set; }
    public GlpiImportResult? LastResult { get; private set; }
    public string? LastError { get; private set; }
    public GlpiAdminImportResult? LastAdminResult { get; private set; }
    public string? LastAdminError { get; private set; }
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
        && (Selection.AnySelected || AdminSelection.AnySelected);

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
            Changed?.Invoke();

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            GlpiMySqlImportService importService = scope.ServiceProvider.GetRequiredService<GlpiMySqlImportService>();
            IGlpiAdminImportService adminImportService = scope.ServiceProvider.GetRequiredService<IGlpiAdminImportService>();
            string connectionString = BuildConnectionString();

            if (Selection.AnySelected)
            {
                try
                {
                    LastResult = await importService.RunAsync(connectionStringOverride: connectionString, selection: Selection);
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
                    LastAdminResult = await adminImportService.RunAsync(connectionString, AdminSelection);
                }
                catch (Exception ex)
                {
                    LastAdminError = ex.Message;
                    LastAdminResult = null;
                }
            }

            LastRunAt = DateTime.UtcNow;
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
