using GlpiNg.Modules.Abstractions.Items;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Voir <see cref="IUserItems"/>. Rattachement par nom : les fiches du parc stockent un texte, pas un identifiant.</summary>
public sealed class UserItemsService(IDbContextFactory<DbContext> dbFactory) : IUserItemSource
{
    public IReadOnlyCollection<string> Kinds { get; } = [UserItemKinds.Used, UserItemKinds.Managed];

    public Task<IReadOnlyList<UserItem>> GetAsync(string kind, int userId, string userName, CancellationToken cancellationToken = default) =>
        QueryAsync(userName, assigned: kind == UserItemKinds.Used, cancellationToken);

    private async Task<IReadOnlyList<UserItem>> QueryAsync(string userName, bool assigned, CancellationToken ct)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<UserItem> items = [];

        // ponytail: une requête par type (9 au plus) ; un index sur ces colonnes si le parc devient très grand.
        // Un ordinateur n'a pas de technicien en charge dans GlpiNg : il n'est donc que « utilisé ».
        if (assigned)
        {
            items.AddRange((await db.Set<Computer>().AsNoTracking().Where(x => x.AssignedUser == userName)
                .Select(x => new { x.Id, x.Name }).ToListAsync(ct))
                .Select(x => new UserItem(ItemTypes.Computer, x.Id, x.Name, $"/parc/computer/{x.Id}")));
        }

        items.AddRange(await Load(db.Set<Peripheral>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Peripheral, "peripherals", ct));
        items.AddRange(await Load(db.Set<Printer>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Printer, "printers", ct));
        items.AddRange(await Load(db.Set<Phone>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Phone, "phones", ct));
        items.AddRange(await Load(db.Set<NetworkEquipment>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.NetworkEquipment, "network-equipments", ct));
        items.AddRange(await Load(db.Set<Rack>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Rack, "racks", ct));
        items.AddRange(await Load(db.Set<Enclosure>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Enclosure, "enclosures", ct));
        items.AddRange(await Load(db.Set<Pdu>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.Pdu, "pdus", ct));
        items.AddRange(await Load(db.Set<PassiveEquipment>().Where(x => (assigned ? x.AssignedUser : x.TechnicianInCharge) == userName).Select(x => new { x.Id, x.Name }), ItemTypes.PassiveEquipment, "passive-equipments", ct));

        return items;
    }

    private static async Task<List<UserItem>> Load<T>(IQueryable<T> rows, string itemType, string route, CancellationToken ct)
        where T : class
    {
        // L'anonyme {Id, Name} est lu par réflexion : évite huit sélecteurs identiques, pour un parc de cette taille.
        var list = await rows.AsNoTracking().ToListAsync(ct);
        var id = typeof(T).GetProperty("Id")!;
        var name = typeof(T).GetProperty("Name")!;
        return [.. list.Select(r => new UserItem(itemType, (int)id.GetValue(r)!, (string)name.GetValue(r)!, $"/parc/{route}/{id.GetValue(r)}"))];
    }
}
