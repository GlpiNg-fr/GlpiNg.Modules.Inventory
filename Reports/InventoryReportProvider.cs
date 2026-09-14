using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Reports;

/// <summary>
/// Rapports du domaine Parc/Inventaire, proposés par la page <c>/tools/reports</c> de l'hôte —
/// voir <see cref="IReportProvider"/> pour le montage. Ils lisent la base par le <c>DbContext</c>
/// de base, donc sous le cloisonnement par entité de l'utilisateur courant : un rapport ne compte
/// jamais que ce que celui qui le demande a le droit de voir.
///
/// Le découpage suit les rapports de GLPI (front/report*.php) plutôt que les tables : « par
/// défaut », « état des matériels », « par lieu », « matériel », « réseau ». Ceux de contrats, de
/// licences et financiers n'ont pas d'équivalent ici, faute des modèles correspondants (voir
/// « Écarts avec GLPI » dans le README) ; en échange, l'inventaire automatique en permet deux que
/// GLPI n'a pas en standard : les systèmes d'exploitation installés et la fraîcheur des remontées.
/// </summary>
public sealed partial class InventoryReportProvider(IDbContextFactory<DbContext> dbFactory) : IReportProvider
{
    internal const string ParcGlobalKey = "parc-global";
    internal const string ParcStatusKey = "parc-status";
    internal const string ParcLocationKey = "parc-location";
    internal const string ParcHardwareKey = "parc-hardware";
    internal const string ParcAgeKey = "parc-age";
    internal const string SoftwareInstalledKey = "software-installed";
    internal const string SoftwareOsKey = "software-os";
    internal const string NetworkOverviewKey = "network-overview";
    internal const string InventoryFreshnessKey = "inventory-freshness";

    private static readonly ReportDefinition[] Definitions =
    [
        new(ParcGlobalKey, "Rapport par défaut",
            "Nombre d'actifs par type et par statut, sur l'ensemble du parc visible.",
            "ti-clipboard-data", "Parc"),
        new(ParcStatusKey, "État des matériels",
            "Croisement type d'actif × statut : ce qui est en service, en stock, en panne ou au rebut.",
            "ti-status-change", "Parc"),
        new(ParcLocationKey, "Matériels par lieu",
            "Répartition géographique du parc, lieu par lieu.",
            "ti-map-pin", "Parc"),
        new(ParcHardwareKey, "Matériel par fabricant et modèle",
            "Parc matériel regroupé par fabricant puis par modèle, filtrable par type d'actif.",
            "ti-device-desktop-analytics", "Parc"),
        new(ParcAgeKey, "Entrées dans le parc par année",
            "Nombre d'actifs entrés dans le parc chaque année, par type.",
            "ti-calendar-stats", "Parc"),
        new(SoftwareInstalledKey, "Logiciels installés",
            "Logiciels remontés par l'inventaire : nombre de versions, d'installations et part du parc.",
            "ti-apps", "Logiciels"),
        new(SoftwareOsKey, "Systèmes d'exploitation",
            "Répartition des postes par système d'exploitation, puis par version.",
            "ti-brand-windows", "Logiciels"),
        new(NetworkOverviewKey, "Rapport réseau",
            "Matériels réseau par type et par fabricant, et répartition des interfaces par sous-réseau.",
            "ti-network", "Réseau"),
        new(InventoryFreshnessKey, "Fraîcheur des inventaires",
            "Ancienneté de la dernière remontée d'inventaire, et postes silencieux depuis trop longtemps.",
            "ti-clock-exclamation", "Inventaire"),
    ];

    public IReadOnlyList<ReportDefinition> GetReports() => Definitions;

    public async Task<IReadOnlyList<ReportFilter>> GetFiltersAsync(string reportKey, CancellationToken cancellationToken = default)
    {
        if (!Knows(reportKey))
        {
            return [];
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return reportKey switch
        {
            ParcHardwareKey => await HardwareFiltersAsync(db, cancellationToken),
            ParcAgeKey => AgeFilters(),
            SoftwareInstalledKey => await SoftwareFiltersAsync(db, cancellationToken),
            NetworkOverviewKey => NetworkFilters(),
            InventoryFreshnessKey => FreshnessFilters(),
            _ => [],
        };
    }

    public async Task<ReportResult?> RunAsync(string reportKey, ReportParameters parameters, CancellationToken cancellationToken = default)
    {
        if (!Knows(reportKey))
        {
            return null;
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return reportKey switch
        {
            ParcGlobalKey => await RunParcGlobalAsync(db, cancellationToken),
            ParcStatusKey => await RunParcStatusAsync(db, cancellationToken),
            ParcLocationKey => await RunParcLocationAsync(db, cancellationToken),
            ParcHardwareKey => await RunParcHardwareAsync(db, parameters, cancellationToken),
            ParcAgeKey => await RunParcAgeAsync(db, parameters, cancellationToken),
            SoftwareInstalledKey => await RunSoftwareInstalledAsync(db, parameters, cancellationToken),
            SoftwareOsKey => await RunSoftwareOsAsync(db, cancellationToken),
            NetworkOverviewKey => await RunNetworkOverviewAsync(db, parameters, cancellationToken),
            InventoryFreshnessKey => await RunInventoryFreshnessAsync(db, parameters, cancellationToken),
            _ => null,
        };
    }

    private static bool Knows(string reportKey) => Definitions.Any(definition => definition.Key == reportKey);

    /// <summary>
    /// Un actif, réduit à ce que les rapports de parc en comptent. Les types d'actifs de GlpiNg
    /// n'ont pas tous les mêmes champs (un câble n'a pas de fabricant, un périphérique n'a qu'un
    /// lieu en texte libre) : les ramener tous à cette forme unique est ce qui permet d'écrire
    /// « par type », « par statut », « par lieu » et « par année » une seule fois.
    ///
    /// <c>Location</c> est déjà résolu : l'Intitulé quand l'actif en porte un, sinon le champ
    /// « Site » renseigné par l'inventaire automatique, sinon <c>null</c> — même cascade qu'à
    /// l'affichage d'une fiche.
    /// </summary>
    private sealed record AssetRow(
        string TypeLabel,
        string ListRoute,
        int? StatusId,
        string? Location,
        string? Manufacturer,
        string? Model,
        DateTime CreatedAt);

    /// <summary>
    /// Ordre d'affichage des types d'actifs, calqué sur celui du menu « Parc » : un rapport se lit
    /// à côté de la sidebar, l'ordre alphabétique y serait un dépaysement gratuit.
    /// </summary>
    private static readonly string[] TypeOrder =
    [
        "Ordinateur", "Moniteur", "Matériel réseau", "Périphérique", "Imprimante",
        "Téléphone", "Baie", "Châssis", "PDU", "Équipement passif", "Câble", "Carte SIM",
    ];

    private static int TypeRank(string typeLabel)
    {
        int index = Array.IndexOf(TypeOrder, typeLabel);
        return index < 0 ? TypeOrder.Length : index;
    }

    /// <summary>
    /// Charge tout le parc visible sous la forme commune <see cref="AssetRow"/>.
    ///
    /// Les moniteurs ne sont pas une table d'actifs mais des périphériques remontés par
    /// l'inventaire (voir la page /parc/monitors, qui fait la même jointure) : on passe par leur
    /// poste, ce qui leur donne au passage son lieu et sa date d'entrée, et surtout applique le
    /// cloisonnement par entité — <c>ComputerPeripheral</c> n'étant pas lui-même cloisonné,
    /// l'interroger seul montrerait les écrans de postes que l'utilisateur ne voit pas.
    ///
    /// Cartouches et consommables sont volontairement absents : ce sont des références de stock,
    /// pas des actifs installés, et les compter ici fausserait tous les totaux.
    /// </summary>
    private static async Task<List<AssetRow>> LoadAssetsAsync(DbContext db, CancellationToken cancellationToken)
    {
        Dictionary<int, string> locations = await LoadDropdownNamesAsync(db, DropdownType.Location, cancellationToken);
        List<AssetRow> rows = [];

        rows.AddRange((await db.Set<Computer>().AsNoTracking().Where(item => !item.IsDeleted)
                .Select(item => new { item.StatusId, item.LocationId, item.Site, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Ordinateur", "/parc/computer", item.StatusId,
                ResolveLocation(locations, item.LocationId, item.Site), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<ComputerPeripheral>().AsNoTracking()
                .Where(item => item.Kind == PeripheralKind.Monitor)
                .Join(db.Set<Computer>().AsNoTracking().Where(computer => !computer.IsDeleted),
                    item => item.ComputerId, computer => computer.Id,
                    (item, computer) => new { item.StatusId, item.Manufacturer, item.Designation, computer.LocationId, computer.Site, computer.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Moniteur", "/parc/monitors", item.StatusId,
                ResolveLocation(locations, item.LocationId, item.Site), item.Manufacturer, item.Designation, item.CreatedAt)));

        rows.AddRange((await db.Set<NetworkEquipment>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Matériel réseau", "/parc/network-equipments", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Peripheral>().AsNoTracking()
                .Select(item => new { item.StatusId, item.Site, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Périphérique", "/parc/peripherals", item.StatusId,
                ResolveLocation(locations, null, item.Site), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Printer>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Imprimante", "/parc/printers", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Phone>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Téléphone", "/parc/phones", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Rack>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Baie", "/parc/racks", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Enclosure>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Châssis", "/parc/enclosures", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Pdu>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("PDU", "/parc/pdus", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<PassiveEquipment>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.Manufacturer, item.Model, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Équipement passif", "/parc/passive-equipments", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), item.Manufacturer, item.Model, item.CreatedAt)));

        rows.AddRange((await db.Set<Cable>().AsNoTracking()
                .Select(item => new { item.StatusId, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Câble", "/parc/cables", item.StatusId, null, null, null, item.CreatedAt)));

        rows.AddRange((await db.Set<SimCard>().AsNoTracking()
                .Select(item => new { item.StatusId, item.LocationId, item.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(item => new AssetRow("Carte SIM", "/parc/simcards", item.StatusId,
                ResolveLocation(locations, item.LocationId, null), null, null, item.CreatedAt)));

        return rows;
    }

    private static string? ResolveLocation(Dictionary<int, string> locations, int? locationId, string? site)
        => locationId is int id && locations.TryGetValue(id, out string? name)
            ? name
            : string.IsNullOrWhiteSpace(site) ? null : site;

    private static async Task<Dictionary<int, string>> LoadDropdownNamesAsync(DbContext db, DropdownType type, CancellationToken cancellationToken)
        => await db.Set<DropdownItem>().AsNoTracking()
            .Where(item => item.Type == type)
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

    /// <summary>Libellé d'une valeur manquante dans une colonne de regroupement : un rapport doit
    /// montrer ce qui n'est pas renseigné, pas l'escamoter — c'est souvent ça qu'on vient y chercher.</summary>
    private const string NotSet = "(non renseigné)";

    /// <summary>Libellé de la ligne de total, partagé par tous les rapports de ce module.</summary>
    private const string TotalLabel = "Total";
}
