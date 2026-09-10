using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Version du protocole SNMP utilisée pour interroger une imprimante.
///
/// Dupliqué de <c>GlpiNg.Modules.Deployment.Models.SnmpVersion</c>, et ce n'est pas un oubli : un
/// module ne référence pas un autre module (voir GlpiNg.Modules.Inventory.csproj), et l'imprimante
/// vit ici tandis que les identifiants SNMP des tâches réseau vivent là-bas. Les deux
/// énumérations doivent rester alignées ; le jour où un troisième usage apparaît, elles auront
/// leur place dans Abstractions.
/// </summary>
public enum PrinterSnmpVersion
{
    /// <summary>Aucune interrogation SNMP pour cette imprimante.</summary>
    None = 0,
    V1 = 1,
    V2c = 2,
    V3 = 3,
}

/// <summary>Protocole d'authentification SNMPv3 — miroir de <c>SnmpAuthProtocol</c> du module Déploiement.</summary>
public enum PrinterSnmpAuthProtocol
{
    None = 0,
    Md5 = 1,
    Sha = 2,
    Sha224 = 3,
    Sha256 = 4,
    Sha384 = 5,
    Sha512 = 6,
}

/// <summary>Protocole de chiffrement SNMPv3 — miroir de <c>SnmpPrivProtocol</c> du module Déploiement.</summary>
public enum PrinterSnmpPrivProtocol
{
    None = 0,
    Des = 1,
    Aes128 = 2,
    TripleDes = 3,
    CiscoAes192 = 4,
    CiscoAes256 = 5,
    Aes192Ietf = 6,
    Aes256Ietf = 7,
}

/// <summary>Imprimante du parc : équivalent de glpi_printers dans GLPI. Actif géré manuellement, même principe que <see cref="NetworkEquipment"/>.</summary>
public class Printer : IEntityScoped
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
    public string? Uuid { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }

    /// <summary>Compteur de page initial (relevé au premier enregistrement).</summary>
    public int? InitialPageCount { get; set; }

    /// <summary>Compteur de page actuel.</summary>
    public int? CurrentPageCount { get; set; }

    public string? Comment { get; set; }

    // --- Interrogation SNMP ---------------------------------------------------
    //
    // De quoi relever le niveau des cartouches et les compteurs de pages sans passer par un agent :
    // une imprimante réseau n'en héberge pas, et c'est elle-même qui expose ces valeurs.

    /// <summary>Adresse à laquelle joindre l'imprimante. Sans elle, aucune interrogation possible.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Version du protocole. <see cref="PrinterSnmpVersion.None"/> désactive l'interrogation.</summary>
    public PrinterSnmpVersion SnmpVersion { get; set; } = PrinterSnmpVersion.None;

    /// <summary>Port SNMP, quand l'imprimante n'écoute pas sur le port usuel.</summary>
    public int SnmpPort { get; set; } = 161;

    /// <summary>Communauté, requise en v1 et v2c, ignorée en v3.</summary>
    public string? SnmpCommunity { get; set; }

    /// <summary>Nom de sécurité, requis en v3 : c'est lui qui identifie l'utilisateur SNMP.</summary>
    public string? SnmpUsername { get; set; }

    /// <summary>Protocole d'authentification v3. <c>None</c> vaut noAuthNoPriv.</summary>
    public PrinterSnmpAuthProtocol SnmpAuthProtocol { get; set; } = PrinterSnmpAuthProtocol.None;

    /// <summary>Phrase secrète d'authentification, requise dès que <see cref="SnmpAuthProtocol"/> n'est pas None.</summary>
    public string? SnmpAuthPassphrase { get; set; }

    /// <summary>Protocole de chiffrement v3, qui suppose une authentification (authPriv).</summary>
    public PrinterSnmpPrivProtocol SnmpPrivProtocol { get; set; } = PrinterSnmpPrivProtocol.None;

    /// <summary>Phrase secrète de chiffrement, requise dès que <see cref="SnmpPrivProtocol"/> n'est pas None.</summary>
    public string? SnmpPrivPassphrase { get; set; }

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
