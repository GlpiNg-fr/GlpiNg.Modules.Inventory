namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Matériel réseau du parc (switch, routeur, borne wifi, ...) : équivalent de
/// glpi_networkequipments dans GLPI. Actif géré manuellement depuis l'UI (voir
/// Components/Pages/NetworkEquipments), au même titre que <see cref="Peripheral"/>.
/// </summary>
public class NetworkEquipment
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Statut de l'élément (voir Models/DropdownItem.cs, DropdownType.Status).</summary>
    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }

    /// <summary>Lieu (voir Models/DropdownItem.cs, DropdownType.Location) — contrairement à Peripheral.Site/Building/Room (texte libre, antérieur aux Intitulés), les nouveaux types de parc utilisent directement le dropdown Lieu, comme Computer.LocationId.</summary>
    public int? LocationId { get; set; }
    public DropdownItem? LocationItem { get; set; }

    /// <summary>Type de matériel réseau (ex: "Switch", "Routeur", "Borne wifi"), texte libre.</summary>
    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? InventoryNumber { get; set; }
    public string? Uuid { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }

    /// <summary>Mémoire embarquée en Mio ("Mémoire (Mio)" dans GLPI).</summary>
    public int? MemoryMb { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<NetworkEquipmentHistoryEntry> HistoryEntries { get; set; } = [];
}

/// <summary>Ligne du journal de modifications d'un matériel réseau (onglet "Historique" de la fiche).</summary>
public class NetworkEquipmentHistoryEntry
{
    public int Id { get; set; }
    public int NetworkEquipmentId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
