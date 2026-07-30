using System.Runtime.CompilerServices;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Importe les postes, agents et composants matériels depuis une base GLPI MySQL
/// existante (accès en lecture seule) vers le modèle de données de GlpiNg.
///
/// Idempotent : un poste déjà importé (identifié par <c>glpi_computers.id</c>, stocké
/// dans <see cref="Computer.SourceGlpiId"/>) est mis à jour plutôt que dupliqué à
/// chaque exécution. Les composants matériels d'un poste sont remplacés en bloc à
/// chaque import (suppression puis réinsertion), plutôt que fusionnés un par un.
///
/// Portée couverte : glpi_computers, glpi_manufacturers/computertypes/computermodels/
/// states/locations, glpi_items_operatingsystems, glpi_agents (CPU/RAM/disques/cartes
/// réseau via glpi_items_device* + glpi_device*).
///
/// Point d'attention : le nom exact des colonnes "optionnelles" (fréquence, capacité...)
/// sur les tables de composants varie selon la version de GLPI installée. Plutôt que de
/// figer un nom de colonne au hasard, ce service interroge information_schema pour
/// détecter les colonnes réellement présentes et se dégrade proprement (valeur NULL,
/// jamais d'erreur SQL) si une colonne attendue n'existe pas.
/// </summary>
public class GlpiMySqlImportService(DbContext db, IOptions<GlpiImportOptions> options)
{
    public async Task<GlpiImportResult> RunAsync(CancellationToken cancellationToken = default)
    {
        DateTime startedAt = DateTime.UtcNow;
        GlpiImportResult result = new();

        string connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "La chaîne de connexion GLPI n'est pas configurée (section \"GlpiImport:ConnectionString\").");
        }

        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        Dictionary<int, string> manufacturers = await LoadLookupAsync(connection, "glpi_manufacturers", "name", cancellationToken);
        Dictionary<int, string> computerTypes = await LoadLookupAsync(connection, "glpi_computertypes", "name", cancellationToken);
        Dictionary<int, string> computerModels = await LoadLookupAsync(connection, "glpi_computermodels", "name", cancellationToken);

        string statesLabelColumn = await PreferredLabelColumnAsync(connection, "glpi_states", cancellationToken);
        Dictionary<int, string> states = await LoadLookupAsync(connection, "glpi_states", statesLabelColumn, cancellationToken);

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

        Dictionary<int, int> glpiComputerIdToLocalId = [];

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

            computer.Status = row.StatesId.HasValue && states.TryGetValue(row.StatesId.Value, out string? stateName)
                ? MapStatus(stateName)
                : computer.Status;

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

        try
        {
            await ImportAgentsAsync(connection, glpiComputerIdToLocalId, result, cancellationToken);
        }
        catch (MySqlException ex)
        {
            result.Warnings.Add($"Import des agents ignoré : {ex.Message}");
        }

        try
        {
            result.ComponentsImported += await ImportComponentsAsync(connection, glpiComputerIdToLocalId, cancellationToken);
        }
        catch (MySqlException ex)
        {
            result.Warnings.Add($"Import des composants matériels ignoré : {ex.Message}");
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
    /// Correspondance heuristique nom d'état GLPI -&gt; <see cref="ComputerStatus"/>.
    /// Les noms d'états sont libres dans GLPI (dictionnaire éditable par l'utilisateur) :
    /// cette correspondance est une approximation par mots-clés, pas une donnée fiable —
    /// à ajuster selon la nomenclature réelle de l'installation source.
    /// </summary>
    private static ComputerStatus MapStatus(string? stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return ComputerStatus.InProduction;
        }

        string normalized = stateName.Trim().ToLowerInvariant();

        if (normalized.Contains("stock"))
        {
            return ComputerStatus.InStock;
        }

        if (normalized.Contains("panne") || normalized.Contains("broken") || normalized.Contains("hs"))
        {
            return ComputerStatus.Broken;
        }

        if (normalized.Contains("retir") || normalized.Contains("réform") || normalized.Contains("reform")
            || normalized.Contains("hors service") || normalized.Contains("retired"))
        {
            return ComputerStatus.Retired;
        }

        return ComputerStatus.InProduction;
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
}
