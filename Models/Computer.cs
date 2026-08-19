namespace GlpiNg.Modules.Inventory.Models;

public class Computer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OsVersion { get; set; }

    /// <summary>content.hardware.uuid — identifiant matériel stable, utile pour recoller un inventaire à une fiche existante.</summary>
    public string? HardwareUuid { get; set; }

    /// <summary>content.hardware.chassis_type — ex: "Notebook", "Server", "Desktop".</summary>
    public string? ChassisType { get; set; }

    /// <summary>content.hardware.memory — mémoire système totale en Mo.</summary>
    public int? TotalMemoryMb { get; set; }

    /// <summary>content.hardware.lastloggeduser — dernier utilisateur connecté sur le poste.</summary>
    public string? LastLoggedUser { get; set; }

    /// <summary>content.hardware.vmsystem — technologie de virtualisation ("Physical" si machine physique).</summary>
    public string? VmSystem { get; set; }

    /// <summary>content.hardware.workgroup — domaine Active Directory ou groupe de travail Windows du poste.</summary>
    public string? Domain { get; set; }

    /// <summary>content.operatingsystem.kernel_version.</summary>
    public string? OsKernelVersion { get; set; }

    /// <summary>Statut de l'élément (voir Models/DropdownItem.cs, DropdownType.Status) — optionnel, résolu depuis les Intitulés plutôt qu'une énumération fixe.</summary>
    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }

    /// <summary>Poste mis à la corbeille (suppression logique) : masqué de la liste par défaut, visible via le filtre "Corbeille".</summary>
    public bool IsDeleted { get; set; }

    public string? Site { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }

    /// <summary>Emplacement choisi manuellement depuis les Intitulés (voir Models/DropdownItem.cs,
    /// DropdownType.Location) — optionnel, prioritaire sur Site/Building/Room (renseignés par
    /// l'inventaire automatique) pour l'affichage quand renseigné, voir Detail.razor.cs.LocationLabel.</summary>
    public int? LocationId { get; set; }
    public DropdownItem? LocationItem { get; set; }

    public string? AssignedUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastInventoryAt { get; set; }

    public List<ComputerComponent> Components { get; set; } = [];
    public List<ComputerSoftware> Softwares { get; set; } = [];
    public List<ComputerPeripheral> Peripherals { get; set; } = [];
    public List<ComputerVolume> Volumes { get; set; } = [];
    public List<ComputerBattery> Batteries { get; set; } = [];
    public List<ComputerNetworkPort> NetworkPorts { get; set; } = [];
    public List<ComputerAntivirus> Antiviruses { get; set; } = [];
    public List<ComputerImportHistory> ImportHistories { get; set; } = [];
    public List<ComputerHistoryEntry> HistoryEntries { get; set; } = [];

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

    /// <summary>Statut de l'élément (voir Models/DropdownItem.cs, DropdownType.Status), éditable manuellement — contrairement aux autres champs de cette entité, qui viennent tous de l'inventaire automatique.</summary>
    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }
}

/// <summary>Volume/partition de disque remonté par content.drives.</summary>
public class ComputerVolume
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public required string Name { get; set; }
    public string? Partition { get; set; }
    public string? MountPoint { get; set; }
    public string? FileSystem { get; set; }
    public long? TotalSizeMb { get; set; }
    public long? FreeSizeMb { get; set; }
    public string? Encryption { get; set; }
}

/// <summary>Batterie remontée par content.batteries.</summary>
public class ComputerBattery
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public required string Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Serial { get; set; }
    public string? Chemistry { get; set; }

    /// <summary>Tension en mV.</summary>
    public int? VoltageMv { get; set; }

    /// <summary>Capacité (constructeur) en mWh.</summary>
    public int? CapacityMwh { get; set; }

    /// <summary>Capacité réelle actuelle (diminue avec l'usure de la batterie), en mWh.</summary>
    public int? RealCapacityMwh { get; set; }

    /// <summary>Date de fabrication, telle que remontée par l'agent (format libre).</summary>
    public string? ManufactureDate { get; set; }
}

/// <summary>Logiciel antivirus détecté remonté par content.antivirus.</summary>
public class ComputerAntivirus
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public required string Name { get; set; }
    public string? Company { get; set; }
    public string? Guid { get; set; }
    public string? Version { get; set; }
    public bool? Enabled { get; set; }
    public bool? UpToDate { get; set; }

    /// <summary>Date d'expiration de la licence, telle que remontée par l'agent (format libre).</summary>
    public string? Expiration { get; set; }

    /// <summary>Date de création de la base de signatures, telle que remontée par l'agent (format libre).</summary>
    public string? BaseCreationDate { get; set; }
    public string? BaseVersion { get; set; }
}

/// <summary>Port/interface réseau (configuration IP) remonté par content.networks.</summary>
public class ComputerNetworkPort
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public required string Designation { get; set; }
    public string? Type { get; set; }
    public string? MacAddress { get; set; }
    public string? Manufacturer { get; set; }
    public string? IpAddress { get; set; }
    public string? IpMask { get; set; }
    public string? IpGateway { get; set; }
    public string? IpSubnet { get; set; }
    public string? IpDhcp { get; set; }
    public int? Mtu { get; set; }

    /// <summary>Vitesse de liaison en Mb/s.</summary>
    public int? SpeedMbps { get; set; }

    public string? Status { get; set; }
    public bool IsVirtual { get; set; }
}

/// <summary>
/// Trace un événement d'import d'inventaire pour un ordinateur : quelle règle de
/// correspondance a été appliquée (création vs mise à jour, et sur quel critère) lors
/// du traitement d'une requête "inventory" du protocole GLPI-Agent. Alimenté par
/// InventoryImportService, affiché dans l'onglet "Informations d'import" de la fiche.
/// </summary>
public class ComputerImportHistory
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string RuleName { get; set; }
    public required string Module { get; set; }
    public string? AgentIdentifier { get; set; }
    public string? InputValue { get; set; }
}

/// <summary>
/// Ligne du journal de modifications d'un ordinateur (onglet "Historique" de la fiche) :
/// un changement de champ ou l'ajout/retrait d'un sous-élément (composant, périphérique,
/// logiciel, ...), détecté par InventoryImportService en comparant l'état avant/après import.
/// </summary>
public class ComputerHistoryEntry
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
