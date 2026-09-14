using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Reports;

/// <summary>
/// Rapports assis sur ce que les agents remontent des postes : logiciels installés et systèmes
/// d'exploitation. Ni <c>ComputerSoftware</c> ni les autres tables filles ne sont cloisonnées par
/// entité — c'est leur poste qui l'est — d'où la jointure systématique vers <c>Computer</c> :
/// interroger la table fille seule compterait les logiciels de postes que l'utilisateur ne voit
/// pas.
/// </summary>
public sealed partial class InventoryReportProvider
{
    /// <summary>Une installation de logiciel rapportée à son poste.</summary>
    private sealed record SoftwareInstallRow(int ComputerId, string Name, string? Publisher, string? Version);

    private static IQueryable<SoftwareInstallRow> SoftwareInstalls(DbContext db)
        => db.Set<ComputerSoftware>().AsNoTracking()
            .Join(db.Set<Computer>().AsNoTracking().Where(computer => !computer.IsDeleted),
                software => software.ComputerId,
                computer => computer.Id,
                (software, computer) => new SoftwareInstallRow(computer.Id, software.Name, software.Publisher, software.Version));

    private static async Task<IReadOnlyList<ReportFilter>> SoftwareFiltersAsync(DbContext db, CancellationToken cancellationToken)
    {
        List<string?> publishers = await SoftwareInstalls(db)
            .Select(install => install.Publisher)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<ReportFilterOption> options = [.. publishers
            .Where(publisher => !string.IsNullOrWhiteSpace(publisher))
            .Select(publisher => publisher!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(publisher => publisher, StringComparer.CurrentCultureIgnoreCase)
            .Select(publisher => new ReportFilterOption(publisher, publisher))];

        return
        [
            ReportFilter.Select("publisher", "Éditeur", options),
            // Un parc remonte facilement plusieurs milliers de logiciels distincts, dont une longue
            // traîne installée une seule fois. Le seuil est le moyen de ne garder que ce qui est
            // déployé pour de bon ; 1 (tout montrer) reste le défaut, pour ne rien cacher sans
            // qu'on l'ait demandé.
            ReportFilter.Number("min", "Installations minimum", 1),
        ];
    }

    private static async Task<ReportResult> RunSoftwareInstalledAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        string? publisher = parameters.GetString("publisher");
        int minimum = Math.Max(1, parameters.GetInt("min") ?? 1);

        IQueryable<SoftwareInstallRow> query = SoftwareInstalls(db);

        if (publisher is not null)
        {
            query = query.Where(install => install.Publisher != null && install.Publisher == publisher);
        }

        // Agrégation en mémoire plutôt qu'en SQL, comme le fait déjà la page /parc/software : le
        // volume est celui d'un parc (une ligne par logiciel et par poste) et le regroupement porte
        // sur des chaînes libres dont la casse varie d'un agent à l'autre — un GROUP BY SQL les
        // compterait séparément selon la collation de la base.
        List<SoftwareInstallRow> installs = await query.ToListAsync(cancellationToken);

        int computerCount = await db.Set<Computer>().AsNoTracking().CountAsync(computer => !computer.IsDeleted, cancellationToken);

        List<ReportRow> rows = installs
            .GroupBy(install => (Name: install.Name.Trim(), Publisher: Label(install.Publisher)), TupleNameComparer)
            .Where(group => group.Count() >= minimum)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key.Name),
                ReportCell.Of(group.Key.Publisher),
                ReportCell.Of(group.Select(install => Label(install.Version)).Distinct(StringComparer.CurrentCultureIgnoreCase).Count()),
                ReportCell.Of(group.Count()),
                ReportCell.Of(group.Select(install => install.ComputerId).Distinct().Count()),
                ReportCell.Percent(group.Select(install => install.ComputerId).Distinct().Count(), computerCount),
            ]))
            .ToList();

        return ReportResult.Of(new ReportTable("Logiciels installés",
        [
            new("Logiciel"),
            new("Éditeur"),
            new("Versions", ReportColumnKind.Number),
            new("Installations", ReportColumnKind.Number),
            new("Postes", ReportColumnKind.Number),
            new("Part des postes", ReportColumnKind.Share),
        ], rows,
            "Aucun logiciel ne correspond aux critères choisis — les logiciels viennent de l'inventaire des agents, un parc importé sans section « softwares » n'en a aucun."));
    }

    /// <summary>
    /// Comparateur des couples (logiciel, éditeur) : deux agents peuvent écrire « Mozilla Firefox »
    /// et « MOZILLA FIREFOX » pour le même logiciel, et ce sont bien deux fois le même qu'on veut
    /// compter. Le premier libellé rencontré est celui qui s'affiche, comme pour n'importe quel
    /// regroupement insensible à la casse.
    /// </summary>
    private static readonly IEqualityComparer<(string Name, string Publisher)> TupleNameComparer =
        new NameComparer();

    private sealed class NameComparer : IEqualityComparer<(string Name, string Publisher)>
    {
        public bool Equals((string Name, string Publisher) left, (string Name, string Publisher) right)
            => string.Equals(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(left.Publisher, right.Publisher, StringComparison.CurrentCultureIgnoreCase);

        public int GetHashCode((string Name, string Publisher) value)
            => HashCode.Combine(
                value.Name.ToUpperInvariant(),
                value.Publisher.ToUpperInvariant());
    }

    private static async Task<ReportResult> RunSoftwareOsAsync(DbContext db, CancellationToken cancellationToken)
    {
        var computers = await db.Set<Computer>().AsNoTracking()
            .Where(computer => !computer.IsDeleted)
            .Select(computer => new { computer.OperatingSystem, computer.OsVersion })
            .ToListAsync(cancellationToken);

        int total = computers.Count;

        List<ReportRow> byOs = computers
            .GroupBy(computer => Label(computer.OperatingSystem), StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        if (byOs.Count > 0)
        {
            byOs.Add(TotalRow(total, total, columnsBefore: 0));
        }

        List<ReportRow> byVersion = computers
            .GroupBy(computer => (Os: Label(computer.OperatingSystem), Version: Label(computer.OsVersion)))
            .OrderBy(group => group.Key.Os, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(group => group.Count())
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key.Os),
                ReportCell.Of(group.Key.Version),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        return ReportResult.Of(
            new ReportTable("Postes par système d'exploitation",
            [
                new("Système d'exploitation"),
                new("Postes", ReportColumnKind.Number),
                new("Part du parc", ReportColumnKind.Share),
            ], byOs, "Aucun poste visible dans le périmètre courant."),
            new ReportTable("Détail par version",
            [
                new("Système d'exploitation"),
                new("Version"),
                new("Postes", ReportColumnKind.Number),
                new("Part du parc", ReportColumnKind.Share),
            ], byVersion, "Aucun poste visible dans le périmètre courant."));
    }
}
