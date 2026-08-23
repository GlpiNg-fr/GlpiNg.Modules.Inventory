namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Imprimante du parc : équivalent de glpi_printers dans GLPI. Actif géré manuellement, même principe que <see cref="NetworkEquipment"/>.</summary>
public class Printer
{
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
    public string? Uuid { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }

    /// <summary>Compteur de page initial (relevé au premier enregistrement).</summary>
    public int? InitialPageCount { get; set; }

    /// <summary>Compteur de page actuel.</summary>
    public int? CurrentPageCount { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<PrinterHistoryEntry> HistoryEntries { get; set; } = [];
}

public class PrinterHistoryEntry
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
