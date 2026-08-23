namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Modèle de consommable du parc ("Consommables" dans GLPI, glpi_consumableitems) : même principe
/// que <see cref="CartridgeItem"/> (modèle + unités individuelles suivies en stock), mais sans lien
/// vers une imprimante — un consommable est soit en stock, soit consommé, jamais "en service" sur
/// un actif précis (voir <see cref="Consumable"/>).
/// </summary>
public class ConsumableItem
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Type de consommable (ex: "Papier", "Agrafes"), texte libre.</summary>
    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Reference { get; set; }

    public int? LocationId { get; set; }
    public DropdownItem? LocationItem { get; set; }

    public string? TechnicianInCharge { get; set; }

    /// <summary>Seuil d'alerte, même principe que CartridgeItem.AlertThreshold.</summary>
    public int AlertThreshold { get; set; } = 10;

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<Consumable> Consumables { get; set; } = [];
    public List<ConsumableItemHistoryEntry> HistoryEntries { get; set; } = [];
}

/// <summary>
/// Exemplaire individuel d'un <see cref="ConsumableItem"/> : réceptionné puis consommé, comme
/// glpi_consumables dans GLPI. Stock disponible = unités où <see cref="DateOut"/> est nul.
/// </summary>
public class Consumable
{
    public int Id { get; set; }
    public int ConsumableItemId { get; set; }

    public DateTime DateIn { get; set; } = DateTime.UtcNow;

    /// <summary>Date de consommation/sortie de stock — null tant qu'en stock.</summary>
    public DateTime? DateOut { get; set; }
}

public class ConsumableItemHistoryEntry
{
    public int Id { get; set; }
    public int ConsumableItemId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
