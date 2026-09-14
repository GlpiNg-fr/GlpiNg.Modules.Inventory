using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Reports;

/// <summary>
/// Rapport réseau : les matériels réseau déclarés du parc d'un côté, les interfaces que les agents
/// remontent de l'autre. Les deux ne se recouvrent pas — un switch n'héberge pas d'agent, un poste
/// n'est pas un matériel réseau — et c'est précisément leur rapprochement qui dit ce qui est
/// branché où.
/// </summary>
public sealed partial class InventoryReportProvider
{
    private static IReadOnlyList<ReportFilter> NetworkFilters() =>
    [
        // Les interfaces virtuelles (boucle locale, ponts d'hyperviseur, tunnels VPN) sont
        // remontées comme les autres et gonflent les sous-réseaux d'adresses qui ne correspondent à
        // aucune prise. Exclues par défaut : le rapport sert d'abord à savoir ce qui est réellement
        // câblé.
        ReportFilter.Select("virtual", "Interfaces virtuelles",
        [
            new("exclude", "Exclure"),
            new("include", "Inclure"),
        ], "exclude"),
    ];

    private static async Task<ReportResult> RunNetworkOverviewAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        bool includeVirtual = parameters.GetString("virtual") == "include";

        var equipments = await db.Set<NetworkEquipment>().AsNoTracking()
            .Select(equipment => new { equipment.Type, equipment.Manufacturer })
            .ToListAsync(cancellationToken);

        int equipmentCount = equipments.Count;

        List<ReportRow> byType = equipments
            .GroupBy(equipment => Label(equipment.Type), StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Select(equipment => Label(equipment.Manufacturer)).Distinct(StringComparer.CurrentCultureIgnoreCase).Count()),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), equipmentCount),
            ]))
            .ToList();

        if (byType.Count > 0)
        {
            byType.Add(TotalRow(equipmentCount, equipmentCount, columnsBefore: 1));
        }

        IQueryable<ComputerNetworkPort> portsQuery = db.Set<ComputerNetworkPort>().AsNoTracking();

        if (!includeVirtual)
        {
            portsQuery = portsQuery.Where(port => !port.IsVirtual);
        }

        // Jointure vers le poste pour le cloisonnement par entité, comme pour les logiciels : une
        // interface n'a pas d'entité à elle, elle hérite de celle de sa machine.
        var ports = await portsQuery
            .Join(db.Set<Computer>().AsNoTracking().Where(computer => !computer.IsDeleted),
                port => port.ComputerId,
                computer => computer.Id,
                (port, computer) => new { computer.Id, port.IpSubnet, port.IpMask, port.IpAddress })
            .ToListAsync(cancellationToken);

        int portCount = ports.Count;

        List<ReportRow> bySubnet = ports
            .GroupBy(port => (Subnet: Label(port.IpSubnet), Mask: Label(port.IpMask)))
            .OrderBy(group => group.Key.Subnet == NotSet ? 1 : 0)
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key.Subnet, StringComparer.Ordinal)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key.Subnet),
                ReportCell.Of(group.Key.Mask),
                ReportCell.Of(group.Count()),
                ReportCell.Of(group.Select(port => port.Id).Distinct().Count()),
                ReportCell.Of(group.Select(port => port.IpAddress).Where(address => !string.IsNullOrWhiteSpace(address)).Distinct().Count()),
                ReportCell.Percent(group.Count(), portCount),
            ]))
            .ToList();

        return ReportResult.Of(
            new ReportTable("Matériels réseau par type",
            [
                new("Type"),
                new("Fabricants distincts", ReportColumnKind.Number),
                new("Matériels", ReportColumnKind.Number),
                new("Part", ReportColumnKind.Share),
            ], byType,
                "Aucun matériel réseau dans le périmètre courant."),
            new ReportTable("Interfaces des postes par sous-réseau",
            [
                new("Sous-réseau"),
                new("Masque"),
                new("Interfaces", ReportColumnKind.Number),
                new("Postes", ReportColumnKind.Number),
                new("Adresses IP distinctes", ReportColumnKind.Number),
                new("Part des interfaces", ReportColumnKind.Share),
            ], bySubnet,
                "Aucune interface remontée — les interfaces viennent de l'inventaire des agents."));
    }
}
