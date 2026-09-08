using System.Runtime.CompilerServices;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Importe les postes, moniteurs, imprimantes, périphériques, logiciels, volumes,
/// batteries, agents et composants matériels depuis une base GLPI MySQL existante (accès
/// en lecture seule) vers le modèle de données de GlpiNg. <see cref="AnalyzeAsync"/>
/// compte les éléments disponibles par catégorie sans rien importer, pour permettre à
/// l'admin de choisir quoi importer (voir <see cref="GlpiImportSelection"/>,
/// <see cref="GlpiImportStateService"/>) avant de lancer <see cref="RunAsync"/>.
///
/// Idempotent : un poste déjà importé (identifié par <c>glpi_computers.id</c>, stocké
/// dans <see cref="Computer.SourceGlpiId"/>) est mis à jour plutôt que dupliqué à
/// chaque exécution. Tous les sous-éléments d'un poste (composants, moniteurs,
/// imprimantes, périphériques, logiciels, volumes, batteries) sont remplacés en bloc à
/// chaque import (suppression puis réinsertion), plutôt que fusionnés un par un —
/// contrairement à <see cref="Computer"/>, aucun n'a d'identifiant source pour un
/// rapprochement fin par élément.
///
/// Portée couverte : glpi_computers, glpi_monitors/glpi_printers/glpi_peripherals (via
/// glpi_computers_items), glpi_items_softwareversions/glpi_softwareversions/glpi_softwares,
/// glpi_items_disks, glpi_items_devicebatteries/glpi_devicebatteries,
/// glpi_manufacturers/computertypes/computermodels/monitortypes/monitormodels/
/// states/locations/filesystems, glpi_items_operatingsystems, glpi_agents (CPU/RAM/disques/
/// cartes réseau via glpi_items_device* + glpi_device*).
///
/// Point d'attention : le nom exact des colonnes "optionnelles" (fréquence, capacité...)
/// sur les tables de composants varie selon la version de GLPI installée. Plutôt que de
/// figer un nom de colonne au hasard, ce service interroge information_schema pour
/// détecter les colonnes réellement présentes et se dégrade proprement (valeur NULL,
/// jamais d'erreur SQL) si une colonne attendue n'existe pas.
/// </summary>
public class GlpiMySqlImportService(DbContext db, IOptions<GlpiImportOptions> options)
{
    /// <summary>
    /// Compte, sans rien importer, le nombre d'éléments disponibles par catégorie dans la base
    /// GLPI MySQL source — sert à afficher les cases à cocher de <c>/admin/import/glpi</c> avant
    /// de lancer un import réel. Chaque compte se dégrade à 0 (plutôt que d'échouer) si la table
    /// correspondante n'existe pas sur cette version de GLPI.
    /// </summary>
    public async Task<GlpiImportAnalysis> AnalyzeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        return new GlpiImportAnalysis
        {
            ComputersCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_computers WHERE is_deleted = 0 AND is_template = 0", cancellationToken),
            MonitorsCount = await TryCountAsync(connection, """
                SELECT COUNT(*) FROM glpi_computers_items ci
                JOIN glpi_monitors m ON m.id = ci.items_id
                WHERE ci.itemtype = 'Monitor' AND m.is_deleted = 0 AND m.is_template = 0
                """, cancellationToken),
            AgentsCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_agents WHERE itemtype = 'Computer'", cancellationToken),
            CpuCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_deviceprocessors WHERE itemtype = 'Computer'", cancellationToken),
            RamCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_devicememories WHERE itemtype = 'Computer'", cancellationToken),
            DiskCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_deviceharddrives WHERE itemtype = 'Computer'", cancellationToken),
            NetworkCardCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_devicenetworkcards WHERE itemtype = 'Computer'", cancellationToken),
            SoftwaresCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_softwareversions WHERE itemtype = 'Computer' AND is_deleted = 0", cancellationToken),
            PrintersCount = await TryCountAsync(connection, """
                SELECT COUNT(*) FROM glpi_computers_items ci
                JOIN glpi_printers p ON p.id = ci.items_id
                WHERE ci.itemtype = 'Printer' AND p.is_deleted = 0 AND p.is_template = 0
                """, cancellationToken),
            PeripheralsCount = await TryCountAsync(connection, """
                SELECT COUNT(*) FROM glpi_computers_items ci
                JOIN glpi_peripherals p ON p.id = ci.items_id
                WHERE ci.itemtype = 'Peripheral' AND p.is_deleted = 0 AND p.is_template = 0
                """, cancellationToken),
            VolumesCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_disks WHERE itemtype = 'Computer'", cancellationToken),
            BatteriesCount = await TryCountAsync(connection, "SELECT COUNT(*) FROM glpi_items_devicebatteries WHERE itemtype = 'Computer'", cancellationToken),
            InventoryPlugin = await DetectInventoryPluginAsync(connection, cancellationToken),
        };
    }

    /// <summary>Répertoires possibles du plugin, du plus récent au plus ancien : GLPI Inventory est un fork de FusionInventory, avec renommage des tables.</summary>
    private static readonly string[] InventoryPluginDirectories = ["glpiinventory", "fusioninventory"];

    /// <summary>
    /// Détecte le plugin d'inventaire de la base source (GLPI Inventory ou FusionInventory).
    ///
    /// Deux sources croisées, parce qu'aucune n'est suffisante seule : <c>glpi_plugins</c> donne le
    /// nom, la version et l'état, mais reste renseigné après une désinstallation qui a laissé
    /// tomber les tables ; l'existence des tables dit ce qu'il y a réellement à lire, mais pas la
    /// version. Un plugin désactivé côté GLPI conserve ses données, donc l'état ne conditionne pas
    /// la détection.
    ///
    /// Les tables sont découvertes par leur préfixe plutôt que listées en dur : leurs noms ont
    /// changé entre FusionInventory et GLPI Inventory, et changent encore d'une version à l'autre.
    /// Les compteurs par catégorie visent des noms connus et retombent à zéro si la table n'existe
    /// pas — même dégradation que le reste de l'analyse.
    /// </summary>
    private static async Task<GlpiInventoryPluginInfo> DetectInventoryPluginAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        GlpiInventoryPluginInfo info = new();
        HashSet<string> tables = await GetPluginTableNamesAsync(connection, cancellationToken);

        foreach (string directory in InventoryPluginDirectories)
        {
            string prefix = $"glpi_plugin_{directory}_";
            int tableCount = tables.Count(table => table.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            (string? name, string? version, int? state) = await ReadPluginRegistrationAsync(connection, directory, cancellationToken);

            if (tableCount == 0 && name is null)
            {
                continue;
            }

            info.IsPresent = true;
            info.Directory = directory;
            info.Name = name;
            info.Version = version;
            info.State = state;
            info.TablePrefix = tableCount > 0 ? prefix : null;
            info.TableCount = tableCount;

            info.DeployPackagesCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}deploypackages`", cancellationToken);
            info.TasksCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}tasks`", cancellationToken);
            info.AgentsCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}agents`", cancellationToken);
            info.IpRangesCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}ipranges`", cancellationToken);
            info.SnmpCredentialsCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}configsecurities`", cancellationToken);
            info.UnmanagedDevicesCount = await TryCountAsync(connection, $"SELECT COUNT(*) FROM `{prefix}unmanageds`", cancellationToken);

            // Premier trouvé : les deux plugins ne cohabitent pas sur une même instance GLPI, et
            // glpiinventory est testé en premier comme étant le successeur.
            break;
        }

        return info;
    }

    /// <summary>
    /// Noms des tables de plugin du schéma courant, filtrés ensuite en mémoire.
    ///
    /// Le préfixe n'est pas comparé en SQL : un <c>LIKE 'glpi_plugin_%'</c> traiterait chaque
    /// underscore comme un joker, et l'échapper dépendrait de <c>sql_mode</c> (le mode
    /// NO_BACKSLASH_ESCAPES change le sens de l'antislash). Ramener les quelques centaines de noms
    /// et les filtrer côté client évite complètement la question.
    /// </summary>
    private static async Task<HashSet<string>> GetPluginTableNamesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        const string sql = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()";

        try
        {
            await using MySqlCommand command = new(sql, connection);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }
        catch (MySqlException)
        {
            // information_schema inaccessible (droits restreints) : la détection retombe sur la
            // seule table glpi_plugins.
        }

        return tables;
    }

    private static async Task<(string? Name, string? Version, int? State)> ReadPluginRegistrationAsync(
        MySqlConnection connection, string directory, CancellationToken cancellationToken)
    {
        const string sql = "SELECT name, version, state FROM glpi_plugins WHERE directory = @directory LIMIT 1";

        try
        {
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@directory", directory);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return (null, null, null);
            }

            return (
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2));
        }
        catch (MySqlException)
        {
            // glpi_plugins absente : base très ancienne ou dump partiel. La détection par les
            // tables reste valable.
            return (null, null, null);
        }
    }

    /// <param name="cancellationToken"></param>
    /// <param name="connectionStringOverride">
    /// Chaîne de connexion à utiliser à la place de la section "GlpiImport:ConnectionString"
    /// (appsettings/user-secrets) — permet à <see cref="GlpiImportStateService"/> de fournir une
    /// connexion saisie depuis le formulaire de <c>/admin/import/glpi</c> plutôt que de dépendre
    /// d'une configuration fichier.
    /// </param>
    /// <param name="selection">
    /// Catégories à importer (par défaut toutes, pour l'appel machine-à-machine
    /// <c>POST /admin/import/glpi</c> qui ne passe pas par l'étape d'analyse/sélection de l'UI).
    /// Un poste GLPI source non sélectionné ("Ordinateurs" décoché) doit déjà avoir été importé
    /// lors d'un run précédent (<see cref="Computer.SourceGlpiId"/>) pour que ses moniteurs/agents/
    /// composants puissent être rattachés : voir la résolution de <c>glpiComputerIdToLocalId</c>
    /// ci-dessous, qui retombe sur les postes déjà connus en base plutôt que de les réimporter.
    /// </param>
    public async Task<GlpiImportResult> RunAsync(
        CancellationToken cancellationToken = default,
        string? connectionStringOverride = null,
        GlpiImportSelection? selection = null)
    {
        selection ??= new GlpiImportSelection();

        DateTime startedAt = DateTime.UtcNow;
        GlpiImportResult result = new();

        string connectionString = connectionStringOverride ?? options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "La chaîne de connexion GLPI n'est pas configurée (section \"GlpiImport:ConnectionString\").");
        }

        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        bool needsManufacturers = selection.ImportComputers || selection.ImportMonitors || selection.ImportPrinters
            || selection.ImportPeripherals || selection.ImportBatteries;
        Dictionary<int, string> manufacturers = needsManufacturers
            ? await LoadLookupAsync(connection, "glpi_manufacturers", "name", cancellationToken)
            : [];

        bool needsStates = selection.ImportComputers || selection.ImportMonitors || selection.ImportPrinters || selection.ImportPeripherals;
        string statesLabelColumn = await PreferredLabelColumnAsync(connection, "glpi_states", cancellationToken);
        Dictionary<int, string> states = needsStates
            ? await LoadLookupAsync(connection, "glpi_states", statesLabelColumn, cancellationToken)
            : [];

        Dictionary<int, int> glpiComputerIdToLocalId = [];
        Dictionary<string, int> statusCache = [];

        if (selection.ImportComputers)
        {
            Dictionary<int, string> computerTypes = await LoadLookupAsync(connection, "glpi_computertypes", "name", cancellationToken);
            Dictionary<int, string> computerModels = await LoadLookupAsync(connection, "glpi_computermodels", "name", cancellationToken);

            string locationsLabelColumn = await PreferredLabelColumnAsync(connection, "glpi_locations", cancellationToken);
            Dictionary<int, string> locations = await LoadLookupAsync(connection, "glpi_locations", locationsLabelColumn, cancellationToken);

            Dictionary<int, (string Name, string? Version)> operatingSystems = [];
            try
            {
                operatingSystems = await LoadOperatingSystemsAsync(connection, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des systèmes d'exploitation ignoré : {ex.Message}");
            }

            await foreach (GlpiComputerRow row in ReadComputersAsync(connection, cancellationToken))
            {
                Computer? computer = await db.Set<Computer>().FirstOrDefaultAsync(c => c.SourceGlpiId == row.Id, cancellationToken);
                bool isNew = computer is null;
                computer ??= new Computer { Name = row.Name ?? $"glpi-{row.Id}", SourceGlpiId = row.Id };

                if (!string.IsNullOrWhiteSpace(row.Name))
                {
                    computer.Name = row.Name;
                }

                computer.SerialNumber = row.Serial ?? computer.SerialNumber;

                computer.Manufacturer = row.ManufacturersId.HasValue && manufacturers.TryGetValue(row.ManufacturersId.Value, out string? manufacturerName)
                    ? manufacturerName
                    : computer.Manufacturer;

                string? typeName = row.ComputerTypesId.HasValue && computerTypes.TryGetValue(row.ComputerTypesId.Value, out string? t) ? t : null;
                string? modelName = row.ComputerModelsId.HasValue && computerModels.TryGetValue(row.ComputerModelsId.Value, out string? m) ? m : null;
                List<string> modelParts = [];
                if (!string.IsNullOrWhiteSpace(typeName)) { modelParts.Add(typeName); }
                if (!string.IsNullOrWhiteSpace(modelName)) { modelParts.Add(modelName); }
                if (modelParts.Count > 0)
                {
                    computer.Model = string.Join(" ", modelParts);
                }

                computer.Site = row.LocationsId.HasValue && locations.TryGetValue(row.LocationsId.Value, out string? locationName)
                    ? locationName
                    : computer.Site;

                if (row.StatesId.HasValue && states.TryGetValue(row.StatesId.Value, out string? stateName) && !string.IsNullOrWhiteSpace(stateName))
                {
                    computer.StatusId = await ResolveStatusIdAsync(stateName, statusCache, cancellationToken);
                }

                computer.LastInventoryAt = row.DateMod ?? computer.LastInventoryAt;

                if (operatingSystems.TryGetValue(row.Id, out (string Name, string? Version) os))
                {
                    computer.OperatingSystem = os.Name;
                    computer.OsVersion = os.Version;
                }

                if (isNew)
                {
                    db.Set<Computer>().Add(computer);
                    result.ComputersCreated++;
                }
                else
                {
                    result.ComputersUpdated++;
                }

                await db.SaveChangesAsync(cancellationToken);
                glpiComputerIdToLocalId[row.Id] = computer.Id;
            }
        }
        else if (selection.AnyComputerDependentCategorySelected)
        {
            glpiComputerIdToLocalId = await db.Set<Computer>()
                .Where(c => c.SourceGlpiId != null)
                .ToDictionaryAsync(c => c.SourceGlpiId!.Value, c => c.Id, cancellationToken);
        }

        if (selection.ImportAgents)
        {
            try
            {
                await ImportAgentsAsync(connection, glpiComputerIdToLocalId, result, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des agents ignoré : {ex.Message}");
            }
        }

        if (selection.ImportComponents)
        {
            try
            {
                result.ComponentsImported += await ImportComponentsAsync(connection, glpiComputerIdToLocalId, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des composants matériels ignoré : {ex.Message}");
            }
        }

        if (selection.ImportMonitors)
        {
            try
            {
                result.MonitorsImported += await ImportMonitorsAsync(connection, manufacturers, states, glpiComputerIdToLocalId, statusCache, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des moniteurs ignoré : {ex.Message}");
            }
        }

        if (selection.ImportSoftwares)
        {
            try
            {
                result.SoftwaresImported += await ImportSoftwaresAsync(connection, glpiComputerIdToLocalId, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des logiciels ignoré : {ex.Message}");
            }
        }

        if (selection.ImportPrinters)
        {
            try
            {
                result.PrintersImported += await ImportPeripheralItemsAsync(
                    connection, manufacturers, states, glpiComputerIdToLocalId, statusCache,
                    PeripheralKind.Printer, glpiItemType: "Printer", table: "glpi_printers", cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des imprimantes ignoré : {ex.Message}");
            }
        }

        if (selection.ImportPeripherals)
        {
            try
            {
                result.PeripheralsImported += await ImportPeripheralItemsAsync(
                    connection, manufacturers, states, glpiComputerIdToLocalId, statusCache,
                    PeripheralKind.Other, glpiItemType: "Peripheral", table: "glpi_peripherals", cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des périphériques ignoré : {ex.Message}");
            }
        }

        if (selection.ImportVolumes)
        {
            try
            {
                result.VolumesImported += await ImportVolumesAsync(connection, glpiComputerIdToLocalId, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des volumes ignoré : {ex.Message}");
            }
        }

        if (selection.ImportBatteries)
        {
            try
            {
                result.BatteriesImported += await ImportBatteriesAsync(connection, manufacturers, glpiComputerIdToLocalId, cancellationToken);
            }
            catch (MySqlException ex)
            {
                result.Warnings.Add($"Import des batteries ignoré : {ex.Message}");
            }
        }

        result.Duration = DateTime.UtcNow - startedAt;
        return result;
    }

    private async Task ImportAgentsAsync(
        MySqlConnection connection,
        Dictionary<int, int> glpiComputerIdToLocalId,
        GlpiImportResult result,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, deviceid, name, last_contact, version, items_id
            FROM glpi_agents
            WHERE itemtype = 'Computer'
            """;

        List<GlpiAgentRow> rows = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new GlpiAgentRow(
                    reader.GetString("deviceid"),
                    reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                    reader.IsDBNull(reader.GetOrdinal("last_contact")) ? null : reader.GetDateTime("last_contact"),
                    reader.IsDBNull(reader.GetOrdinal("version")) ? null : reader.GetString("version"),
                    reader.GetInt32("items_id")));
            }
        }

        foreach (GlpiAgentRow row in rows)
        {
            // La base GLPI source ne connaît pas le header GLPI-Agent-ID (introduit par le
            // protocole JSON) : "deviceid" est la seule identité stable disponible ici, on la
            // réutilise comme AgentUuid. Si l'agent contacte ensuite via le protocole JSON avec
            // un UUID différent, un second enregistrement sera créé — limite acceptée de l'import.
            GlpiAgent? agent = await db.Set<GlpiAgent>().FirstOrDefaultAsync(a => a.AgentUuid == row.DeviceId, cancellationToken);
            bool isNew = agent is null;
            agent ??= new GlpiAgent { AgentUuid = row.DeviceId, DeviceId = row.DeviceId };

            agent.Hostname = row.Name ?? agent.Hostname;
            agent.AgentVersion = row.Version ?? agent.AgentVersion;
            agent.LastContactAt = row.LastContact ?? agent.LastContactAt;

            if (isNew)
            {
                db.Set<GlpiAgent>().Add(agent);
                result.AgentsCreated++;
            }
            else
            {
                result.AgentsUpdated++;
            }

            await db.SaveChangesAsync(cancellationToken);

            if (glpiComputerIdToLocalId.TryGetValue(row.GlpiComputerId, out int localComputerId))
            {
                Computer? computer = await db.Set<Computer>().FirstOrDefaultAsync(c => c.Id == localComputerId, cancellationToken);
                if (computer is not null)
                {
                    computer.AgentId = agent.Id;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }

    private async Task<int> ImportComponentsAsync(
        MySqlConnection connection,
        Dictionary<int, int> glpiComputerIdToLocalId,
        CancellationToken cancellationToken)
    {
        int total = 0;

        total += await ImportComponentGroupAsync(
            connection, glpiComputerIdToLocalId, ComponentType.Cpu,
            linkTable: "glpi_items_deviceprocessors", refTable: "glpi_deviceprocessors", linkFk: "deviceprocessors_id",
            designationCandidates: ["designation"], capacityCandidates: ["frequency", "frequence"],
            cancellationToken: cancellationToken);

        total += await ImportComponentGroupAsync(
            connection, glpiComputerIdToLocalId, ComponentType.Ram,
            linkTable: "glpi_items_devicememories", refTable: "glpi_devicememories", linkFk: "devicememories_id",
            designationCandidates: ["designation"], capacityCandidates: ["size", "frequence"],
            cancellationToken: cancellationToken);

        total += await ImportComponentGroupAsync(
            connection, glpiComputerIdToLocalId, ComponentType.Disk,
            linkTable: "glpi_items_deviceharddrives", refTable: "glpi_deviceharddrives", linkFk: "deviceharddrives_id",
            designationCandidates: ["designation"], capacityCandidates: ["capacity"],
            cancellationToken: cancellationToken);

        total += await ImportComponentGroupAsync(
            connection, glpiComputerIdToLocalId, ComponentType.NetworkCard,
            linkTable: "glpi_items_devicenetworkcards", refTable: "glpi_devicenetworkcards", linkFk: "devicenetworkcards_id",
            designationCandidates: ["designation"], capacityCandidates: ["bandwidth"],
            cancellationToken: cancellationToken);

        return total;
    }

    private async Task<int> ImportComponentGroupAsync(
        MySqlConnection connection,
        Dictionary<int, int> glpiComputerIdToLocalId,
        ComponentType componentType,
        string linkTable,
        string refTable,
        string linkFk,
        string[] designationCandidates,
        string[] capacityCandidates,
        CancellationToken cancellationToken)
    {
        HashSet<string> linkColumns = await GetColumnsAsync(connection, linkTable, cancellationToken);
        HashSet<string> refColumns = await GetColumnsAsync(connection, refTable, cancellationToken);

        string designationSelect = SelectFirstExisting(linkColumns, refColumns, "designation", designationCandidates);
        string capacitySelect = SelectFirstExisting(linkColumns, refColumns, "capacity", capacityCandidates);

        string sql = $"""
            SELECT link.items_id AS items_id, {designationSelect}, {capacitySelect}
            FROM `{linkTable}` link
            JOIN `{refTable}` ref ON ref.id = link.`{linkFk}`
            WHERE link.itemtype = 'Computer'
            """;

        Dictionary<int, List<(string Designation, string? Capacity)>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            int designationOrdinal = -1;
            int capacityOrdinal = -1;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (designationOrdinal == -1)
                {
                    designationOrdinal = reader.GetOrdinal("designation");
                    capacityOrdinal = reader.GetOrdinal("capacity");
                }

                int itemsId = reader.GetInt32("items_id");
                string designation = reader.IsDBNull(designationOrdinal)
                    ? "(inconnu)"
                    : reader.GetValue(designationOrdinal).ToString() ?? "(inconnu)";
                string? capacity = reader.IsDBNull(capacityOrdinal) ? null : reader.GetValue(capacityOrdinal).ToString();

                if (!byComputer.TryGetValue(itemsId, out List<(string Designation, string? Capacity)>? list))
                {
                    list = [];
                    byComputer[itemsId] = list;
                }

                list.Add((designation, capacity));
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<(string Designation, string? Capacity)>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerComponent> existing = await db.Set<ComputerComponent>()
                .Where(c => c.ComputerId == localComputerId && c.Type == componentType)
                .ToListAsync(cancellationToken);
            db.Set<ComputerComponent>().RemoveRange(existing);

            foreach ((string Designation, string? Capacity) item in entry.Value)
            {
                db.Set<ComputerComponent>().Add(new ComputerComponent
                {
                    ComputerId = localComputerId,
                    Type = componentType,
                    Designation = item.Designation,
                    Capacity = item.Capacity
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    /// <summary>
    /// Moniteurs rattachés à un poste via <c>glpi_computers_items</c> (itemtype='Monitor') :
    /// remplacés en bloc par poste à chaque import (suppression puis réinsertion), au même titre
    /// que <see cref="ImportComponentGroupAsync"/> — <see cref="Models.ComputerPeripheral"/> n'a
    /// pas d'équivalent à <see cref="Computer.SourceGlpiId"/> pour un rapprochement fin par élément.
    /// </summary>
    private async Task<int> ImportMonitorsAsync(
        MySqlConnection connection,
        Dictionary<int, string> manufacturers,
        Dictionary<int, string> states,
        Dictionary<int, int> glpiComputerIdToLocalId,
        Dictionary<string, int> statusCache,
        CancellationToken cancellationToken)
    {
        string typesLabelColumn = await PreferredLabelColumnAsync(connection, "glpi_monitortypes", cancellationToken);
        Dictionary<int, string> monitorTypes = await LoadLookupAsync(connection, "glpi_monitortypes", typesLabelColumn, cancellationToken);

        string modelsLabelColumn = await PreferredLabelColumnAsync(connection, "glpi_monitormodels", cancellationToken);
        Dictionary<int, string> monitorModels = await LoadLookupAsync(connection, "glpi_monitormodels", modelsLabelColumn, cancellationToken);

        const string sql = """
            SELECT ci.computers_id AS computer_id, m.name, m.serial, m.manufacturers_id, m.monitortypes_id, m.monitormodels_id, m.states_id
            FROM glpi_computers_items ci
            JOIN glpi_monitors m ON m.id = ci.items_id
            WHERE ci.itemtype = 'Monitor' AND m.is_deleted = 0 AND m.is_template = 0
            """;

        Dictionary<int, List<GlpiMonitorRow>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                int computerId = reader.GetInt32("computer_id");
                GlpiMonitorRow row = new(
                    reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                    reader.IsDBNull(reader.GetOrdinal("serial")) ? null : reader.GetString("serial"),
                    reader.IsDBNull(reader.GetOrdinal("manufacturers_id")) ? null : reader.GetInt32("manufacturers_id"),
                    reader.IsDBNull(reader.GetOrdinal("monitortypes_id")) ? null : reader.GetInt32("monitortypes_id"),
                    reader.IsDBNull(reader.GetOrdinal("monitormodels_id")) ? null : reader.GetInt32("monitormodels_id"),
                    reader.IsDBNull(reader.GetOrdinal("states_id")) ? null : reader.GetInt32("states_id"));

                if (!byComputer.TryGetValue(computerId, out List<GlpiMonitorRow>? list))
                {
                    list = [];
                    byComputer[computerId] = list;
                }

                list.Add(row);
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<GlpiMonitorRow>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerPeripheral> existing = await db.Set<ComputerPeripheral>()
                .Where(p => p.ComputerId == localComputerId && p.Kind == PeripheralKind.Monitor)
                .ToListAsync(cancellationToken);
            db.Set<ComputerPeripheral>().RemoveRange(existing);

            foreach (GlpiMonitorRow row in entry.Value)
            {
                List<string> designationParts = [];
                if (row.MonitorTypesId.HasValue && monitorTypes.TryGetValue(row.MonitorTypesId.Value, out string? typeName) && !string.IsNullOrWhiteSpace(typeName))
                {
                    designationParts.Add(typeName);
                }
                if (row.MonitorModelsId.HasValue && monitorModels.TryGetValue(row.MonitorModelsId.Value, out string? modelName) && !string.IsNullOrWhiteSpace(modelName))
                {
                    designationParts.Add(modelName);
                }

                string designation = !string.IsNullOrWhiteSpace(row.Name)
                    ? row.Name
                    : designationParts.Count > 0 ? string.Join(" ", designationParts) : "(moniteur)";

                int? statusId = null;
                if (row.StatesId.HasValue && states.TryGetValue(row.StatesId.Value, out string? stateName) && !string.IsNullOrWhiteSpace(stateName))
                {
                    statusId = await ResolveStatusIdAsync(stateName, statusCache, cancellationToken);
                }

                db.Set<ComputerPeripheral>().Add(new ComputerPeripheral
                {
                    ComputerId = localComputerId,
                    Kind = PeripheralKind.Monitor,
                    Designation = designation,
                    Manufacturer = row.ManufacturersId.HasValue && manufacturers.TryGetValue(row.ManufacturersId.Value, out string? manufacturerName) ? manufacturerName : null,
                    Serial = row.Serial,
                    StatusId = statusId
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    /// <summary>Logiciels installés (content.softwares) : voir glpi_items_softwareversions/glpi_softwareversions/glpi_softwares. Remplacés en bloc par poste, comme les composants matériels.</summary>
    private async Task<int> ImportSoftwaresAsync(
        MySqlConnection connection,
        Dictionary<int, int> glpiComputerIdToLocalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT isv.items_id AS computer_id, s.name AS software_name, sv.name AS version_name, mf.name AS manufacturer_name
            FROM glpi_items_softwareversions isv
            JOIN glpi_softwareversions sv ON sv.id = isv.softwareversions_id
            JOIN glpi_softwares s ON s.id = sv.softwares_id
            LEFT JOIN glpi_manufacturers mf ON mf.id = s.manufacturers_id
            WHERE isv.itemtype = 'Computer' AND isv.is_deleted = 0
            """;

        Dictionary<int, List<(string Name, string? Version, string? Publisher)>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                int computerId = reader.GetInt32("computer_id");
                string name = ReadNullableString(reader, "software_name") ?? "(inconnu)";
                string? version = ReadNullableString(reader, "version_name");
                string? publisher = ReadNullableString(reader, "manufacturer_name");

                if (!byComputer.TryGetValue(computerId, out List<(string Name, string? Version, string? Publisher)>? list))
                {
                    list = [];
                    byComputer[computerId] = list;
                }

                list.Add((name, version, publisher));
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<(string Name, string? Version, string? Publisher)>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerSoftware> existing = await db.Set<ComputerSoftware>()
                .Where(s => s.ComputerId == localComputerId)
                .ToListAsync(cancellationToken);
            db.Set<ComputerSoftware>().RemoveRange(existing);

            foreach ((string Name, string? Version, string? Publisher) item in entry.Value)
            {
                db.Set<ComputerSoftware>().Add(new ComputerSoftware
                {
                    ComputerId = localComputerId,
                    Name = item.Name,
                    Version = item.Version,
                    Publisher = item.Publisher
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    /// <summary>
    /// Imprimantes (<c>glpi_printers</c>) et autres périphériques (<c>glpi_peripherals</c>) rattachés
    /// à un poste via <c>glpi_computers_items</c>, importés vers <see cref="ComputerPeripheral"/> avec
    /// respectivement <see cref="PeripheralKind.Printer"/> et <see cref="PeripheralKind.Other"/> — même
    /// méthode générique que pour les moniteurs, mais sans détail type/modèle (nom seul).
    /// </summary>
    private async Task<int> ImportPeripheralItemsAsync(
        MySqlConnection connection,
        Dictionary<int, string> manufacturers,
        Dictionary<int, string> states,
        Dictionary<int, int> glpiComputerIdToLocalId,
        Dictionary<string, int> statusCache,
        PeripheralKind kind,
        string glpiItemType,
        string table,
        CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT ci.computers_id AS computer_id, t.name, t.serial, t.manufacturers_id, t.states_id
            FROM glpi_computers_items ci
            JOIN `{table}` t ON t.id = ci.items_id
            WHERE ci.itemtype = @itemType AND t.is_deleted = 0 AND t.is_template = 0
            """;

        Dictionary<int, List<GlpiPeripheralRow>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        {
            command.Parameters.AddWithValue("@itemType", glpiItemType);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int computerId = reader.GetInt32("computer_id");
                GlpiPeripheralRow row = new(
                    ReadNullableString(reader, "name"),
                    ReadNullableString(reader, "serial"),
                    ReadNullableInt(reader, "manufacturers_id"),
                    ReadNullableInt(reader, "states_id"));

                if (!byComputer.TryGetValue(computerId, out List<GlpiPeripheralRow>? list))
                {
                    list = [];
                    byComputer[computerId] = list;
                }

                list.Add(row);
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<GlpiPeripheralRow>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerPeripheral> existing = await db.Set<ComputerPeripheral>()
                .Where(p => p.ComputerId == localComputerId && p.Kind == kind)
                .ToListAsync(cancellationToken);
            db.Set<ComputerPeripheral>().RemoveRange(existing);

            foreach (GlpiPeripheralRow row in entry.Value)
            {
                int? statusId = null;
                if (row.StatesId.HasValue && states.TryGetValue(row.StatesId.Value, out string? stateName) && !string.IsNullOrWhiteSpace(stateName))
                {
                    statusId = await ResolveStatusIdAsync(stateName, statusCache, cancellationToken);
                }

                db.Set<ComputerPeripheral>().Add(new ComputerPeripheral
                {
                    ComputerId = localComputerId,
                    Kind = kind,
                    Designation = !string.IsNullOrWhiteSpace(row.Name) ? row.Name : "(inconnu)",
                    Manufacturer = row.ManufacturersId.HasValue && manufacturers.TryGetValue(row.ManufacturersId.Value, out string? manufacturerName) ? manufacturerName : null,
                    Serial = row.Serial,
                    StatusId = statusId
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    /// <summary>Volumes/partitions (content.drives) : voir glpi_items_disks (GLPI 9.5+, absent des versions plus anciennes — se dégrade alors à 0 via TryCountAsync/le catch MySqlException de l'appelant).</summary>
    private async Task<int> ImportVolumesAsync(
        MySqlConnection connection,
        Dictionary<int, int> glpiComputerIdToLocalId,
        CancellationToken cancellationToken)
    {
        Dictionary<int, string> filesystems = await LoadLookupAsync(connection, "glpi_filesystems", "name", cancellationToken);

        const string sql = """
            SELECT items_id AS computer_id, name, device, mountpoint, filesystems_id, totalsize, freesize
            FROM glpi_items_disks
            WHERE itemtype = 'Computer'
            """;

        Dictionary<int, List<GlpiVolumeRow>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                int computerId = reader.GetInt32("computer_id");
                GlpiVolumeRow row = new(
                    ReadNullableString(reader, "name"),
                    ReadNullableString(reader, "device"),
                    ReadNullableString(reader, "mountpoint"),
                    ReadNullableInt(reader, "filesystems_id"),
                    ReadNullableLong(reader, "totalsize"),
                    ReadNullableLong(reader, "freesize"));

                if (!byComputer.TryGetValue(computerId, out List<GlpiVolumeRow>? list))
                {
                    list = [];
                    byComputer[computerId] = list;
                }

                list.Add(row);
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<GlpiVolumeRow>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerVolume> existing = await db.Set<ComputerVolume>()
                .Where(v => v.ComputerId == localComputerId)
                .ToListAsync(cancellationToken);
            db.Set<ComputerVolume>().RemoveRange(existing);

            foreach (GlpiVolumeRow row in entry.Value)
            {
                db.Set<ComputerVolume>().Add(new ComputerVolume
                {
                    ComputerId = localComputerId,
                    Name = !string.IsNullOrWhiteSpace(row.Name) ? row.Name : row.MountPoint ?? "(volume)",
                    Partition = row.Device,
                    MountPoint = row.MountPoint,
                    FileSystem = row.FilesystemsId.HasValue && filesystems.TryGetValue(row.FilesystemsId.Value, out string? fs) ? fs : null,
                    TotalSizeMb = row.TotalSize,
                    FreeSizeMb = row.FreeSize
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    /// <summary>
    /// Batteries (content.batteries) : voir glpi_items_devicebatteries/glpi_devicebatteries. Les
    /// colonnes designation/chemistry/voltage/capacity/real_capacity/manufacturing_date/serial
    /// existent tantôt sur la table de liaison, tantôt sur la table de référence selon la version
    /// de GLPI — même détection via information_schema que <see cref="ImportComponentGroupAsync"/>.
    /// </summary>
    private async Task<int> ImportBatteriesAsync(
        MySqlConnection connection,
        Dictionary<int, string> manufacturers,
        Dictionary<int, int> glpiComputerIdToLocalId,
        CancellationToken cancellationToken)
    {
        HashSet<string> linkColumns = await GetColumnsAsync(connection, "glpi_items_devicebatteries", cancellationToken);
        HashSet<string> refColumns = await GetColumnsAsync(connection, "glpi_devicebatteries", cancellationToken);

        string designationSelect = SelectFirstExisting(linkColumns, refColumns, "designation", ["designation"]);
        string chemistrySelect = SelectFirstExisting(linkColumns, refColumns, "chemistry", ["chemistry"]);
        string voltageSelect = SelectFirstExisting(linkColumns, refColumns, "voltage", ["voltage"]);
        string capacitySelect = SelectFirstExisting(linkColumns, refColumns, "capacity", ["capacity"]);
        string realCapacitySelect = SelectFirstExisting(linkColumns, refColumns, "real_capacity", ["real_capacity"]);
        string manufacturingDateSelect = SelectFirstExisting(linkColumns, refColumns, "manufacturing_date", ["manufacturing_date", "date"]);
        string serialSelect = SelectFirstExisting(linkColumns, refColumns, "serial", ["serial"]);
        string manufacturersIdSelect = refColumns.Contains("manufacturers_id") ? "ref.`manufacturers_id` AS manufacturers_id" : "NULL AS manufacturers_id";

        string sql = $"""
            SELECT link.items_id AS computer_id, {designationSelect}, {chemistrySelect}, {voltageSelect}, {capacitySelect},
                   {realCapacitySelect}, {manufacturingDateSelect}, {serialSelect}, {manufacturersIdSelect}
            FROM glpi_items_devicebatteries link
            JOIN glpi_devicebatteries ref ON ref.id = link.devicebatteries_id
            WHERE link.itemtype = 'Computer'
            """;

        Dictionary<int, List<GlpiBatteryRow>> byComputer = [];

        await using (MySqlCommand command = new(sql, connection))
        await using (MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                int computerId = reader.GetInt32("computer_id");
                GlpiBatteryRow row = new(
                    ReadNullableString(reader, "designation"),
                    ReadNullableString(reader, "chemistry"),
                    ReadNullableInt(reader, "voltage"),
                    ReadNullableInt(reader, "capacity"),
                    ReadNullableInt(reader, "real_capacity"),
                    ReadNullableString(reader, "manufacturing_date"),
                    ReadNullableString(reader, "serial"),
                    ReadNullableInt(reader, "manufacturers_id"));

                if (!byComputer.TryGetValue(computerId, out List<GlpiBatteryRow>? list))
                {
                    list = [];
                    byComputer[computerId] = list;
                }

                list.Add(row);
            }
        }

        int imported = 0;

        foreach (KeyValuePair<int, List<GlpiBatteryRow>> entry in byComputer)
        {
            if (!glpiComputerIdToLocalId.TryGetValue(entry.Key, out int localComputerId))
            {
                continue;
            }

            List<ComputerBattery> existing = await db.Set<ComputerBattery>()
                .Where(b => b.ComputerId == localComputerId)
                .ToListAsync(cancellationToken);
            db.Set<ComputerBattery>().RemoveRange(existing);

            foreach (GlpiBatteryRow row in entry.Value)
            {
                db.Set<ComputerBattery>().Add(new ComputerBattery
                {
                    ComputerId = localComputerId,
                    Name = !string.IsNullOrWhiteSpace(row.Designation) ? row.Designation : "(batterie)",
                    Manufacturer = row.ManufacturersId.HasValue && manufacturers.TryGetValue(row.ManufacturersId.Value, out string? manufacturerName) ? manufacturerName : null,
                    Serial = row.Serial,
                    Chemistry = row.Chemistry,
                    VoltageMv = row.Voltage,
                    CapacityMwh = row.Capacity,
                    RealCapacityMwh = row.RealCapacity,
                    ManufactureDate = row.ManufacturingDate
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    private static string? ReadNullableString(MySqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal).ToString();
    }

    private static int? ReadNullableInt(MySqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static long? ReadNullableLong(MySqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal));
    }

    private static async Task<int> TryCountAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await using MySqlCommand command = new(sql, connection);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? 0 : Convert.ToInt32(value);
        }
        catch (MySqlException)
        {
            // Table absente sur cette version de GLPI (ex: pas de glpi_computers_items) : compte à 0
            // plutôt qu'une erreur qui bloquerait l'analyse pour les autres catégories.
            return 0;
        }
    }

    private static string SelectFirstExisting(HashSet<string> linkColumns, HashSet<string> refColumns, string alias, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (linkColumns.Contains(candidate))
            {
                return $"link.`{candidate}` AS `{alias}`";
            }
        }

        foreach (string candidate in candidates)
        {
            if (refColumns.Contains(candidate))
            {
                return $"ref.`{candidate}` AS `{alias}`";
            }
        }

        return $"NULL AS `{alias}`";
    }

    private static async Task<HashSet<string>> GetColumnsAsync(MySqlConnection connection, string table, CancellationToken cancellationToken)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        const string sql = "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table";

        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@table", table);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<string> PreferredLabelColumnAsync(MySqlConnection connection, string table, CancellationToken cancellationToken)
    {
        HashSet<string> columns = await GetColumnsAsync(connection, table, cancellationToken);
        return columns.Contains("completename") ? "completename" : "name";
    }

    private static async Task<Dictionary<int, string>> LoadLookupAsync(
        MySqlConnection connection, string table, string labelColumn, CancellationToken cancellationToken)
    {
        Dictionary<int, string> result = [];
        string sql = $"SELECT id, `{labelColumn}` FROM `{table}`";

        await using MySqlCommand command = new(sql, connection);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            int id = reader.GetInt32(0);
            string label = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            result[id] = label;
        }

        return result;
    }

    private static async Task<Dictionary<int, (string Name, string? Version)>> LoadOperatingSystemsAsync(
        MySqlConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<int, (string Name, string? Version)> result = [];
        const string sql = """
            SELECT io.items_id, os.name AS os_name, osv.name AS os_version_name
            FROM glpi_items_operatingsystems io
            LEFT JOIN glpi_operatingsystems os ON os.id = io.operatingsystems_id
            LEFT JOIN glpi_operatingsystemversions osv ON osv.id = io.operatingsystemversions_id
            WHERE io.itemtype = 'Computer'
            """;

        await using MySqlCommand command = new(sql, connection);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            int itemsId = reader.GetInt32("items_id");
            int osNameOrdinal = reader.GetOrdinal("os_name");
            int osVersionOrdinal = reader.GetOrdinal("os_version_name");
            string? osName = reader.IsDBNull(osNameOrdinal) ? null : reader.GetString(osNameOrdinal);
            string? osVersion = reader.IsDBNull(osVersionOrdinal) ? null : reader.GetString(osVersionOrdinal);

            if (!string.IsNullOrWhiteSpace(osName))
            {
                result[itemsId] = (osName, osVersion);
            }
        }

        return result;
    }

    private static async IAsyncEnumerable<GlpiComputerRow> ReadComputersAsync(
        MySqlConnection connection, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, serial, manufacturers_id, computertypes_id, computermodels_id, states_id, locations_id, date_mod
            FROM glpi_computers
            WHERE is_deleted = 0 AND is_template = 0
            """;

        await using MySqlCommand command = new(sql, connection);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new GlpiComputerRow(
                reader.GetInt32("id"),
                reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                reader.IsDBNull(reader.GetOrdinal("serial")) ? null : reader.GetString("serial"),
                reader.IsDBNull(reader.GetOrdinal("manufacturers_id")) ? null : reader.GetInt32("manufacturers_id"),
                reader.IsDBNull(reader.GetOrdinal("computertypes_id")) ? null : reader.GetInt32("computertypes_id"),
                reader.IsDBNull(reader.GetOrdinal("computermodels_id")) ? null : reader.GetInt32("computermodels_id"),
                reader.IsDBNull(reader.GetOrdinal("states_id")) ? null : reader.GetInt32("states_id"),
                reader.IsDBNull(reader.GetOrdinal("locations_id")) ? null : reader.GetInt32("locations_id"),
                reader.IsDBNull(reader.GetOrdinal("date_mod")) ? null : reader.GetDateTime("date_mod"));
        }
    }

    /// <summary>
    /// Résout (ou crée à la volée) l'Id du DropdownItem de type Status (voir Models/DropdownItem.cs)
    /// portant le nom d'état GLPI source — préserve la nomenclature exacte de l'installation
    /// source plutôt que de la forcer dans un jeu de statuts fixe. <paramref name="cache"/> évite
    /// une requête répétée pour un même nom d'état au sein d'un même import (states_id est déjà
    /// dédupliqué par computer, mais partagé par de nombreux ordinateurs).
    /// </summary>
    private async Task<int> ResolveStatusIdAsync(string stateName, Dictionary<string, int> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(stateName, out int cachedId))
        {
            return cachedId;
        }

        DropdownItem? item = await db.Set<DropdownItem>()
            .FirstOrDefaultAsync(i => i.Type == DropdownType.Status && i.Name == stateName, cancellationToken);

        if (item is null)
        {
            item = new DropdownItem { Type = DropdownType.Status, Name = stateName };
            db.Set<DropdownItem>().Add(item);
            await db.SaveChangesAsync(cancellationToken);
        }

        cache[stateName] = item.Id;
        return item.Id;
    }

    private sealed record GlpiComputerRow(
        int Id,
        string? Name,
        string? Serial,
        int? ManufacturersId,
        int? ComputerTypesId,
        int? ComputerModelsId,
        int? StatesId,
        int? LocationsId,
        DateTime? DateMod);

    private sealed record GlpiAgentRow(
        string DeviceId,
        string? Name,
        DateTime? LastContact,
        string? Version,
        int GlpiComputerId);

    private sealed record GlpiMonitorRow(
        string? Name,
        string? Serial,
        int? ManufacturersId,
        int? MonitorTypesId,
        int? MonitorModelsId,
        int? StatesId);

    private sealed record GlpiPeripheralRow(
        string? Name,
        string? Serial,
        int? ManufacturersId,
        int? StatesId);

    private sealed record GlpiVolumeRow(
        string? Name,
        string? Device,
        string? MountPoint,
        int? FilesystemsId,
        long? TotalSize,
        long? FreeSize);

    private sealed record GlpiBatteryRow(
        string? Designation,
        string? Chemistry,
        int? Voltage,
        int? Capacity,
        int? RealCapacity,
        string? ManufacturingDate,
        string? Serial,
        int? ManufacturersId);
}
