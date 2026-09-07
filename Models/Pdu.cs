using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>PDU (bandeau d'alimentation) du parc : équivalent de glpi_pdus dans GLPI. Actif géré manuellement, même principe que <see cref="NetworkEquipment"/>.</summary>
public class Pdu : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }

    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }

    public int? LocationId { get; set; }
    public DropdownItem? LocationItem { get; set; }

    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? InventoryNumber { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<PduHistoryEntry> HistoryEntries { get; set; } = [];
}

public class PduHistoryEntry
{
    public int Id { get; set; }
    public int PduId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
