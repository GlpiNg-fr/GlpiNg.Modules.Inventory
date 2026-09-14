using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Reports;

/// <summary>
/// Fraîcheur des inventaires : depuis quand chaque poste n'a plus rien remonté, et quels agents se
/// sont tus. Un parc inventorié automatiquement se dégrade en silence — une fiche ancienne ne
/// ressemble pas à une fiche fausse — et c'est le seul rapport qui rende ce vieillissement
/// visible.
/// </summary>
public sealed partial class InventoryReportProvider
{
    /// <summary>Nombre maximal de lignes détaillées : au-delà, le rapport cesse d'être un rapport
    /// et redevient la liste des ordinateurs, qui a déjà sa page et ses filtres.</summary>
    private const int DetailRowLimit = 200;

    private static IReadOnlyList<ReportFilter> FreshnessFilters() =>
    [
        ReportFilter.Number("days", "Sans remontée depuis plus de (jours)", 30),
    ];

    /// <summary>
    /// Tranches d'ancienneté, bornes hautes en jours. Volontairement figées et non paramétrables :
    /// ce sont celles qui séparent un poste éteint pour le week-end (moins de 7 jours), un poste en
    /// congés (moins de 30) et un poste qu'on a perdu de vue (au-delà).
    /// </summary>
    private static readonly (string Label, double MaxDays)[] FreshnessBuckets =
    [
        ("Moins de 24 heures", 1),
        ("1 à 7 jours", 7),
        ("7 à 30 jours", 30),
        ("30 à 90 jours", 90),
        ("Plus de 90 jours", double.PositiveInfinity),
    ];

    private const string NeverInventoried = "Jamais inventorié";

    private static async Task<ReportResult> RunInventoryFreshnessAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        int days = Math.Max(1, parameters.GetInt("days") ?? 30);
        DateTime now = DateTime.UtcNow;

        var computers = await db.Set<Computer>().AsNoTracking()
            .Where(computer => !computer.IsDeleted)
            .Select(computer => new
            {
                computer.Id,
                computer.Name,
                computer.LastInventoryAt,
                AgentName = computer.Agent != null ? computer.Agent.AgentName ?? computer.Agent.DeviceId ?? computer.Agent.AgentUuid : null,
            })
            .ToListAsync(cancellationToken);

        int total = computers.Count;

        Dictionary<string, int> counts = computers
            .GroupBy(computer => BucketLabel(computer.LastInventoryAt, now))
            .ToDictionary(group => group.Key, group => group.Count());

        // Toutes les tranches sont affichées, y compris vides : c'est leur suite qui fait la
        // lecture (« tout est à gauche » ou « la moitié est à droite »), et un trou au milieu la
        // rendrait fausse. Sauf sur un parc vide, où il n'y a rien à lire du tout.
        List<ReportRow> byBucket = total == 0
            ? []
            : [.. FreshnessBuckets
                .Select(bucket => bucket.Label)
                .Append(NeverInventoried)
                .Select(label => new ReportRow(
                [
                    ReportCell.Of(label),
                    ReportCell.Of(counts.GetValueOrDefault(label)),
                    ReportCell.Percent(counts.GetValueOrDefault(label), total),
                ]))];

        if (total > 0)
        {
            byBucket.Add(TotalRow(total, total, columnsBefore: 0));
        }

        var stale = computers
            .Where(computer => computer.LastInventoryAt is null
                || (now - computer.LastInventoryAt.Value).TotalDays > days)
            // Les postes jamais inventoriés d'abord : ce sont eux qui posent question en premier,
            // et une date nulle n'a pas de place naturelle dans un tri par date.
            .OrderBy(computer => computer.LastInventoryAt ?? DateTime.MinValue)
            .ToList();

        List<ReportRow> staleRows = [.. stale
            .Take(DetailRowLimit)
            .Select(computer => new ReportRow(
            [
                ReportCell.Link(computer.Name, $"/parc/computer/{computer.Id}"),
                ReportCell.Date(computer.LastInventoryAt),
                ReportCell.Of(computer.LastInventoryAt is { } last ? $"{(int)(now - last).TotalDays} j" : NeverInventoried),
                ReportCell.Of(computer.AgentName),
            ]))];

        var agents = await db.Set<GlpiAgent>().AsNoTracking()
            .Where(agent => agent.LastContactAt < now.AddDays(-days))
            .OrderBy(agent => agent.LastContactAt)
            .Take(DetailRowLimit)
            .Select(agent => new
            {
                Name = agent.AgentName ?? agent.DeviceId ?? agent.AgentUuid,
                agent.AgentVersion,
                agent.LastContactAt,
                ComputerId = agent.Computer != null ? (int?)agent.Computer.Id : null,
                ComputerName = agent.Computer != null ? agent.Computer.Name : null,
            })
            .ToListAsync(cancellationToken);

        List<ReportRow> agentRows = [.. agents.Select(agent => new ReportRow(
        [
            ReportCell.Of(agent.Name),
            ReportCell.Of(agent.AgentVersion),
            ReportCell.Date(agent.LastContactAt),
            agent.ComputerId is int computerId
                ? ReportCell.Link(agent.ComputerName, $"/parc/computer/{computerId}")
                : ReportCell.Of(null),
        ]))];

        return ReportResult.Of(
            new ReportTable("Ancienneté de la dernière remontée",
            [
                new("Ancienneté"),
                new("Postes", ReportColumnKind.Number),
                new("Part du parc", ReportColumnKind.Share),
            ], byBucket,
                "Aucun poste visible dans le périmètre courant."),
            new ReportTable(DetailTitle($"Postes sans remontée depuis plus de {days} jours", stale.Count),
            [
                new("Poste"),
                new("Dernière remontée"),
                new("Ancienneté", ReportColumnKind.Number),
                new("Agent"),
            ], staleRows,
                $"Tous les postes visibles ont remonté un inventaire dans les {days} derniers jours."),
            new ReportTable($"Agents sans contact depuis plus de {days} jours",
            [
                new("Agent"),
                new("Version"),
                new("Dernier contact"),
                new("Poste"),
            ], agentRows,
                $"Tous les agents connus ont contacté le serveur dans les {days} derniers jours."));
    }

    private static string BucketLabel(DateTime? lastInventoryAt, DateTime now)
    {
        if (lastInventoryAt is not { } last)
        {
            return NeverInventoried;
        }

        double age = (now - last).TotalDays;
        return FreshnessBuckets.First(bucket => age < bucket.MaxDays).Label;
    }

    /// <summary>Dit dans le titre qu'une liste a été tronquée, plutôt que de laisser croire qu'elle est complète.</summary>
    private static string DetailTitle(string title, int matchCount)
        => matchCount > DetailRowLimit ? $"{title} — {DetailRowLimit} plus anciens sur {matchCount}" : title;
}
