using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Résout affichage (libellé, icône, route) et listing pour les types d'extrémité de
/// <see cref="Cable"/> (<see cref="CableEndpointType"/>). Tous les types éligibles vivent dans ce
/// module, donc contrairement à l'annuaire polymorphe de GlpiNg.Modules.Deployment
/// (IDeploymentTargetDirectory, qui doit traverser la frontière module/hôte), cette résolution se
/// fait directement par requête EF sur le DbContext injecté par l'appelant.
/// </summary>
public static class CableEndpointCatalog
{
    public static readonly CableEndpointType[] AllTypes = Enum.GetValues<CableEndpointType>();

    public static string DisplayName(CableEndpointType type) => type switch
    {
        CableEndpointType.Computer => Tr.T("Ordinateur"),
        CableEndpointType.NetworkEquipment => Tr.T("Matériel réseau"),
        CableEndpointType.Peripheral => Tr.T("Périphérique"),
        CableEndpointType.Phone => Tr.T("Téléphone"),
        CableEndpointType.Printer => Tr.T("Imprimante"),
        CableEndpointType.PassiveEquipment => Tr.T("Équipement passif"),
        _ => type.ToString(),
    };

    public static string Icon(CableEndpointType type) => type switch
    {
        CableEndpointType.Computer => "ti-device-desktop",
        CableEndpointType.NetworkEquipment => "ti-router",
        CableEndpointType.Peripheral => "ti-mouse",
        CableEndpointType.Phone => "ti-phone",
        CableEndpointType.Printer => "ti-printer",
        CableEndpointType.PassiveEquipment => "ti-plug-connected",
        _ => "ti-cable",
    };

    public static string Route(CableEndpointType type, int id) => type switch
    {
        CableEndpointType.Computer => $"/parc/computer/{id}",
        CableEndpointType.NetworkEquipment => $"/parc/network-equipments/{id}",
        CableEndpointType.Peripheral => $"/parc/peripherals/{id}",
        CableEndpointType.Phone => $"/parc/phones/{id}",
        CableEndpointType.Printer => $"/parc/printers/{id}",
        CableEndpointType.PassiveEquipment => $"/parc/passive-equipments/{id}",
        _ => "#",
    };

    /// <summary>Liste (Id, Nom) de tous les éléments d'un type d'extrémité, pour le sélecteur du formulaire.</summary>
    public static async Task<List<(int Id, string Name)>> GetItemsAsync(DbContext db, CableEndpointType type) => type switch
    {
        CableEndpointType.Computer => (await db.Set<Computer>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        CableEndpointType.NetworkEquipment => (await db.Set<NetworkEquipment>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        CableEndpointType.Peripheral => (await db.Set<Peripheral>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        CableEndpointType.Phone => (await db.Set<Phone>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        CableEndpointType.Printer => (await db.Set<Printer>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        CableEndpointType.PassiveEquipment => (await db.Set<PassiveEquipment>().AsNoTracking().OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
        _ => [],
    };

    /// <summary>Résout en une passe (groupée par type) le nom affichable d'un ensemble de clés (Type, Id), pour les listes/fiches.</summary>
    public static async Task<Dictionary<(CableEndpointType Type, int Id), string>> ResolveNamesAsync(DbContext db, IEnumerable<(CableEndpointType Type, int Id)> keys)
    {
        Dictionary<(CableEndpointType, int), string> result = [];

        foreach (IGrouping<CableEndpointType, (CableEndpointType Type, int Id)> group in keys.Distinct().GroupBy(k => k.Type))
        {
            HashSet<int> ids = group.Select(k => k.Id).ToHashSet();
            List<(int Id, string Name)> items = group.Key switch
            {
                CableEndpointType.Computer => (await db.Set<Computer>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                CableEndpointType.NetworkEquipment => (await db.Set<NetworkEquipment>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                CableEndpointType.Peripheral => (await db.Set<Peripheral>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                CableEndpointType.Phone => (await db.Set<Phone>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                CableEndpointType.Printer => (await db.Set<Printer>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                CableEndpointType.PassiveEquipment => (await db.Set<PassiveEquipment>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync()).Select(r => (r.Id, r.Name)).ToList(),
                _ => [],
            };

            foreach ((int id, string name) in items)
            {
                result[(group.Key, id)] = name;
            }
        }

        return result;
    }
}
