namespace GlpiNg.Modules.Inventory.Models;

public enum ComputerStatus
{
    InStock,
    InProduction,
    Broken,
    Retired
}

public class Computer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OsVersion { get; set; }

    public ComputerStatus Status { get; set; } = ComputerStatus.InStock;

    public string? Site { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }

    public string? AssignedUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastInventoryAt { get; set; }

    public List<ComputerComponent> Components { get; set; } = [];
    public List<ComputerSoftware> Softwares { get; set; } = [];
    public List<ComputerPeripheral> Peripherals { get; set; } = [];

    // Lien vers l'agent GLPI qui remonte les infos pour cette machine
    public int? AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }

    /// <summary>
    /// Id de l'ordinateur (glpi_computers.id) dans la base GLPI source, quand ce
    /// poste a été créé par l'import GLPI. Permet un import idempotent (upsert).
    /// </summary>
    public int? SourceGlpiId { get; set; }
}

/// <summary>Logiciel installé remonté par content.softwares.</summary>
public class ComputerSoftware
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public required string Name { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? InstallDate { get; set; }
}

public enum PeripheralKind
{
    Monitor,
    Printer,
    Other
}

/// <summary>Écran, imprimante ou autre périphérique remonté par content.monitors/printers/peripherals.</summary>
public class ComputerPeripheral
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public PeripheralKind Kind { get; set; }
    public required string Designation { get; set; }
    public string? Manufacturer { get; set; }
    public string? Serial { get; set; }
}
