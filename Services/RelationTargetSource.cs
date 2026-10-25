using GlpiNg.Modules.Abstractions.Items;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using Types = GlpiNg.Modules.Abstractions.Items.ItemTypes;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Objets du parc comme cibles de rattachement (analyse d'impact) — voir <see cref="IRelationTargetSource"/>.</summary>
public sealed class RelationTargetSource(IDbContextFactory<DbContext> dbFactory) : IRelationTargetSource
{
    public IReadOnlyCollection<string> SupportedTypes { get; } =
    [
        Types.Computer, Types.Monitor, Types.Peripheral, Types.Printer, Types.Phone, Types.NetworkEquipment,
        Types.Rack, Types.Enclosure, Types.Pdu, Types.PassiveEquipment, Types.Cable,
    ];

    private sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public Task<IReadOnlyList<RelationTarget>> SearchAsync(string itemType, string? term, int limit, CancellationToken cancellationToken = default)
    {
        return QueryAsync(itemType, term?.Trim() ?? string.Empty, null, limit, cancellationToken);
    }

    public Task<IReadOnlyList<RelationTarget>> ResolveAsync(string itemType, IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        QueryAsync(itemType, null, ids, int.MaxValue, cancellationToken);

    private async Task<IReadOnlyList<RelationTarget>> QueryAsync(
        string itemType, string? like, IReadOnlyCollection<int>? ids, int limit, CancellationToken ct)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(ct);

        // Une seule forme de requête par type : Id + nom, filtrés par nom (recherche) ou par identifiants (résolution).
        (IQueryable<Row> rows, string route) = itemType switch
        {
            Types.Computer => (db.Set<Computer>().Select(x => new Row { Id = x.Id, Name = x.Name }), "computer"),
            Types.Monitor => (db.Set<ComputerPeripheral>().Where(x => x.Kind == PeripheralKind.Monitor).Select(x => new Row { Id = x.Id, Name = x.Designation }), "monitors"),
            Types.Peripheral => (db.Set<Peripheral>().Select(x => new Row { Id = x.Id, Name = x.Name }), "peripherals"),
            Types.Printer => (db.Set<Printer>().Select(x => new Row { Id = x.Id, Name = x.Name }), "printers"),
            Types.Phone => (db.Set<Phone>().Select(x => new Row { Id = x.Id, Name = x.Name }), "phones"),
            Types.NetworkEquipment => (db.Set<NetworkEquipment>().Select(x => new Row { Id = x.Id, Name = x.Name }), "network-equipments"),
            Types.Rack => (db.Set<Rack>().Select(x => new Row { Id = x.Id, Name = x.Name }), "racks"),
            Types.Enclosure => (db.Set<Enclosure>().Select(x => new Row { Id = x.Id, Name = x.Name }), "enclosures"),
            Types.Pdu => (db.Set<Pdu>().Select(x => new Row { Id = x.Id, Name = x.Name }), "pdus"),
            Types.PassiveEquipment => (db.Set<PassiveEquipment>().Select(x => new Row { Id = x.Id, Name = x.Name }), "passive-equipments"),
            Types.Cable => (db.Set<Cable>().Select(x => new Row { Id = x.Id, Name = x.Name }), "cables"),
            _ => (Enumerable.Empty<Row>().AsQueryable(), string.Empty),
        };

        if (route.Length == 0)
        {
            return [];
        }

        if (ids is not null)
        {
            rows = rows.Where(r => ids.Contains(r.Id));
        }

        if (like is not null)
        {
            rows = rows.Where(r => r.Name.Contains(like));
        }

        List<Row> list = await rows.OrderBy(r => r.Name).Take(limit).ToListAsync(ct);
        return [.. list.Select(r => new RelationTarget(itemType, r.Id, r.Name, $"/parc/{route}/{r.Id}"))];
    }
}
