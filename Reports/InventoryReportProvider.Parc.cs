using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Reports;

/// <summary>
/// Les rapports de parc proprement dits : combien d'actifs, dans quel état, à quel endroit, de
/// quelle marque, arrivés quand. Tous partent du même chargement
/// (<c>LoadAssetsAsync</c>) et ne diffèrent que par le regroupement.
/// </summary>
public sealed partial class InventoryReportProvider
{
    private static async Task<ReportResult> RunParcGlobalAsync(DbContext db, CancellationToken cancellationToken)
    {
        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);
        Dictionary<int, string> statuses = await LoadDropdownNamesAsync(db, DropdownType.Status, cancellationToken);
        int total = assets.Count;

        List<ReportRow> byType = assets
            .GroupBy(asset => (asset.TypeLabel, asset.ListRoute))
            .OrderBy(group => TypeRank(group.Key.TypeLabel))
            .Select(group => new ReportRow(
            [
                ReportCell.Link(group.Key.TypeLabel, group.Key.ListRoute),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        // Pas de ligne de total sur un tableau vide : elle empêcherait le message d'explication de
        // s'afficher, et « Total : 0 » n'apprend rien à personne.
        if (byType.Count > 0)
        {
            byType.Add(TotalRow(total, total, columnsBefore: 0));
        }

        List<ReportRow> byStatus = assets
            .GroupBy(asset => StatusLabel(statuses, asset.StatusId))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        if (byStatus.Count > 0)
        {
            byStatus.Add(TotalRow(total, total, columnsBefore: 0));
        }

        return ReportResult.Of(
            new ReportTable("Actifs par type",
                [new("Type"), new("Nombre", ReportColumnKind.Number), new("Part du parc", ReportColumnKind.Share)],
                byType,
                "Aucun actif visible dans le périmètre courant."),
            new ReportTable("Actifs par statut",
                [new("Statut"), new("Nombre", ReportColumnKind.Number), new("Part du parc", ReportColumnKind.Share)],
                byStatus,
                "Aucun actif visible dans le périmètre courant."));
    }

    private static async Task<ReportResult> RunParcStatusAsync(DbContext db, CancellationToken cancellationToken)
    {
        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);
        Dictionary<int, string> statuses = await LoadDropdownNamesAsync(db, DropdownType.Status, cancellationToken);

        // Colonnes = statuts réellement portés par au moins un actif, et non tous les Intitulés de
        // type « Statut » : un tableau croisé dont la moitié des colonnes sont vides se lit mal, et
        // les statuts inutilisés se voient déjà dans les Intitulés. « (non renseigné) » ferme la
        // marche, comme partout ailleurs dans ces rapports.
        List<string> statusLabels = [.. assets
            .Select(asset => StatusLabel(statuses, asset.StatusId))
            .Distinct()
            .OrderBy(label => label == NotSet ? 1 : 0)
            .ThenBy(label => label, StringComparer.CurrentCultureIgnoreCase)];

        List<ReportColumn> columns =
        [
            new("Type"),
            .. statusLabels.Select(label => new ReportColumn(label, ReportColumnKind.Number)),
            new(TotalLabel, ReportColumnKind.Number),
        ];

        List<ReportRow> rows = [];

        foreach (IGrouping<(string TypeLabel, string ListRoute), AssetRow> group in assets
                     .GroupBy(asset => (asset.TypeLabel, asset.ListRoute))
                     .OrderBy(group => TypeRank(group.Key.TypeLabel)))
        {
            List<ReportCell> cells = [ReportCell.Link(group.Key.TypeLabel, group.Key.ListRoute)];
            cells.AddRange(statusLabels.Select(label =>
                ReportCell.Of(group.Count(asset => StatusLabel(statuses, asset.StatusId) == label))));
            cells.Add(ReportCell.Of(group.Count()));

            rows.Add(new ReportRow(cells));
        }

        if (rows.Count > 0)
        {
            List<ReportCell> totals = [ReportCell.Of(TotalLabel)];
            totals.AddRange(statusLabels.Select(label =>
                ReportCell.Of(assets.Count(asset => StatusLabel(statuses, asset.StatusId) == label))));
            totals.Add(ReportCell.Of(assets.Count));

            rows.Add(new ReportRow(totals, IsTotal: true));
        }

        return ReportResult.Of(new ReportTable("Type d'actif × statut", columns, rows,
            "Aucun actif visible dans le périmètre courant."));
    }

    private static async Task<ReportResult> RunParcLocationAsync(DbContext db, CancellationToken cancellationToken)
    {
        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);
        int total = assets.Count;

        List<ReportRow> rows = assets
            .GroupBy(asset => asset.Location ?? NotSet)
            .OrderBy(group => group.Key == NotSet ? 1 : 0)
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Ordinateur")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Moniteur")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Imprimante")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Matériel réseau")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel is not ("Ordinateur" or "Moniteur" or "Imprimante" or "Matériel réseau"))),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        if (rows.Count > 0)
        {
            rows.Add(new ReportRow(
            [
                ReportCell.Of(TotalLabel),
                ReportCell.Of(assets.Count(asset => asset.TypeLabel == "Ordinateur")),
                ReportCell.Of(assets.Count(asset => asset.TypeLabel == "Moniteur")),
                ReportCell.Of(assets.Count(asset => asset.TypeLabel == "Imprimante")),
                ReportCell.Of(assets.Count(asset => asset.TypeLabel == "Matériel réseau")),
                ReportCell.Of(assets.Count(asset => asset.TypeLabel is not ("Ordinateur" or "Moniteur" or "Imprimante" or "Matériel réseau"))),
                ReportCell.Of(total),
                ReportCell.Percent(total, total),
            ], IsTotal: true));
        }

        return ReportResult.Of(new ReportTable("Actifs par lieu",
        [
            new("Lieu"),
            new("Ordinateurs", ReportColumnKind.Number),
            new("Moniteurs", ReportColumnKind.Number),
            new("Imprimantes", ReportColumnKind.Number),
            new("Matériels réseau", ReportColumnKind.Number),
            new("Autres", ReportColumnKind.Number),
            new(TotalLabel, ReportColumnKind.Number),
            new("Part du parc", ReportColumnKind.Share),
        ], rows, "Aucun actif visible dans le périmètre courant."));
    }

    /// <summary>
    /// Options des deux listes déroulantes du rapport « matériel ». Elles sortent du parc lui-même
    /// et non d'une liste figée : proposer un fabricant dont aucun actif ne porte le nom n'aurait
    /// d'autre effet que de produire un rapport vide.
    /// </summary>
    private static async Task<IReadOnlyList<ReportFilter>> HardwareFiltersAsync(DbContext db, CancellationToken cancellationToken)
    {
        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);

        List<ReportFilterOption> types = [.. assets
            .Select(asset => asset.TypeLabel)
            .Distinct()
            .OrderBy(TypeRank)
            .Select(type => new ReportFilterOption(type, type))];

        List<ReportFilterOption> manufacturers = [.. assets
            .Select(asset => asset.Manufacturer)
            .Where(manufacturer => !string.IsNullOrWhiteSpace(manufacturer))
            .Select(manufacturer => manufacturer!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(manufacturer => manufacturer, StringComparer.CurrentCultureIgnoreCase)
            .Select(manufacturer => new ReportFilterOption(manufacturer, manufacturer))];

        return
        [
            ReportFilter.Select("type", "Type d'actif", types),
            ReportFilter.Select("manufacturer", "Fabricant", manufacturers),
        ];
    }

    private static async Task<ReportResult> RunParcHardwareAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        string? type = parameters.GetString("type");
        string? manufacturer = parameters.GetString("manufacturer");

        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);

        if (type is not null)
        {
            assets = [.. assets.Where(asset => asset.TypeLabel == type)];
        }

        if (manufacturer is not null)
        {
            assets = [.. assets.Where(asset => string.Equals(asset.Manufacturer?.Trim(), manufacturer, StringComparison.CurrentCultureIgnoreCase))];
        }

        int total = assets.Count;

        List<ReportRow> byManufacturer = assets
            .GroupBy(asset => Label(asset.Manufacturer))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Select(asset => Label(asset.Model)).Distinct(StringComparer.CurrentCultureIgnoreCase).Count()),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        if (byManufacturer.Count > 0)
        {
            byManufacturer.Add(TotalRow(total, total, columnsBefore: 1));
        }

        List<ReportRow> byModel = assets
            .GroupBy(asset => (Manufacturer: Label(asset.Manufacturer), Model: Label(asset.Model), asset.TypeLabel))
            .OrderBy(group => group.Key.Manufacturer, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key.Model, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key.Manufacturer),
                ReportCell.Of(group.Key.Model),
                ReportCell.Of(group.Key.TypeLabel),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        return ReportResult.Of(
            new ReportTable("Par fabricant",
            [
                new("Fabricant"),
                new("Modèles distincts", ReportColumnKind.Number),
                new("Actifs", ReportColumnKind.Number),
                new("Part", ReportColumnKind.Share),
            ], byManufacturer, "Aucun actif ne correspond aux critères choisis."),
            new ReportTable("Détail par modèle",
            [
                new("Fabricant"),
                new("Modèle"),
                new("Type"),
                new("Actifs", ReportColumnKind.Number),
                new("Part", ReportColumnKind.Share),
            ], byModel, "Aucun actif ne correspond aux critères choisis."));
    }

    /// <summary>
    /// Bornes du rapport par année. Aucune valeur par défaut : le rapport porte sur l'historique
    /// complet tant qu'on ne le restreint pas, ce qui est l'usage le plus courant (« depuis quand
    /// et à quel rythme le parc a-t-il grossi ? »).
    /// </summary>
    private static IReadOnlyList<ReportFilter> AgeFilters() =>
    [
        ReportFilter.Date("from", "Entrés depuis le"),
        ReportFilter.Date("to", "Entrés jusqu'au"),
    ];

    private static async Task<ReportResult> RunParcAgeAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        DateTime? from = parameters.GetDate("from");
        DateTime? to = parameters.GetDate("to");

        List<AssetRow> assets = await LoadAssetsAsync(db, cancellationToken);

        // Les dates sont stockées en UTC et saisies dans le fuseau du serveur : la comparaison se
        // fait donc en heure locale, comme l'affichage. Le "jusqu'au" est inclusif — une borne de
        // fin qui exclut le jour qu'on vient de saisir est le genre de détail qui fait douter d'un
        // chiffre pendant une heure.
        List<AssetRow> filtered = [.. assets.Where(asset =>
        {
            DateTime createdAt = asset.CreatedAt.ToLocalTime();
            return (from is null || createdAt.Date >= from.Value.Date)
                && (to is null || createdAt.Date <= to.Value.Date);
        })];

        int total = filtered.Count;

        List<ReportRow> rows = filtered
            .GroupBy(asset => asset.CreatedAt.ToLocalTime().Year)
            .OrderBy(group => group.Key)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key.ToString()),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Ordinateur")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Moniteur")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Imprimante")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel == "Matériel réseau")),
                ReportCell.Of(group.Count(asset => asset.TypeLabel is not ("Ordinateur" or "Moniteur" or "Imprimante" or "Matériel réseau"))),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        if (rows.Count > 0)
        {
            rows.Add(new ReportRow(
            [
                ReportCell.Of(TotalLabel),
                ReportCell.Of(filtered.Count(asset => asset.TypeLabel == "Ordinateur")),
                ReportCell.Of(filtered.Count(asset => asset.TypeLabel == "Moniteur")),
                ReportCell.Of(filtered.Count(asset => asset.TypeLabel == "Imprimante")),
                ReportCell.Of(filtered.Count(asset => asset.TypeLabel == "Matériel réseau")),
                ReportCell.Of(filtered.Count(asset => asset.TypeLabel is not ("Ordinateur" or "Moniteur" or "Imprimante" or "Matériel réseau"))),
                ReportCell.Of(total),
                ReportCell.Percent(total, total),
            ], IsTotal: true));
        }

        return ReportResult.Of(new ReportTable("Entrées dans le parc par année",
        [
            new("Année"),
            new("Ordinateurs", ReportColumnKind.Number),
            new("Moniteurs", ReportColumnKind.Number),
            new("Imprimantes", ReportColumnKind.Number),
            new("Matériels réseau", ReportColumnKind.Number),
            new("Autres", ReportColumnKind.Number),
            new(TotalLabel, ReportColumnKind.Number),
            new("Part", ReportColumnKind.Share),
        ], rows,
            "Aucun actif n'est entré dans le parc sur la période choisie."));
    }

    private static string StatusLabel(Dictionary<int, string> statuses, int? statusId)
        => statusId is int id && statuses.TryGetValue(id, out string? name) ? name : NotSet;

    private static string Label(string? value) => string.IsNullOrWhiteSpace(value) ? NotSet : value.Trim();

    /// <summary>
    /// Ligne de total d'un tableau « libellé, (colonnes intermédiaires), nombre, part ».
    /// <paramref name="columnsBefore"/> est le nombre de colonnes à laisser vides entre le libellé
    /// et le nombre — un total de « modèles distincts » n'aurait pas de sens, la somme des modèles
    /// distincts par fabricant n'étant pas le nombre de modèles distincts du parc.
    /// </summary>
    private static ReportRow TotalRow(int count, int total, int columnsBefore)
    {
        List<ReportCell> cells = [ReportCell.Of(TotalLabel)];
        cells.AddRange(Enumerable.Repeat(new ReportCell(string.Empty), columnsBefore));
        cells.Add(ReportCell.Of(count));
        cells.Add(ReportCell.Percent(count, total));

        return new ReportRow(cells, IsTotal: true);
    }
}
