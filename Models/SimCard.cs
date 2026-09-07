using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Carte SIM du parc : équivalent de <c>glpi_simcards</c> dans GLPI. Actif géré manuellement, même
/// principe que <see cref="Phone"/>.
///
/// <see cref="SerialNumber"/> porte l'ICCID, comme la colonne <c>serial</c> de GLPI — c'est
/// l'identifiant gravé sur la carte, et le champ sur lequel on la recherche en pratique.
///
/// Le rattachement à un matériel (<c>Item_DeviceSimcard</c> côté GLPI) n'est pas repris : la carte
/// se relie ici à un porteur par <see cref="PhoneNumber"/> et <see cref="AssignedUser"/>.
/// </summary>
public class SimCard : IEntityScoped
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

    /// <summary>Type de carte (format : mini/micro/nano, prépayée...).</summary>
    public string? Type { get; set; }

    /// <summary>Opérateur téléphonique (<c>manufacturers_id</c> côté GLPI).</summary>
    public string? Operator { get; set; }

    /// <summary>ICCID gravé sur la carte (<c>serial</c> côté GLPI).</summary>
    public string? SerialNumber { get; set; }

    public string? InventoryNumber { get; set; }

    /// <summary>Numéro de la ligne associée.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Identifiant d'abonné mobile (<c>msin</c> côté GLPI).</summary>
    public string? Msin { get; set; }

    public string? Pin { get; set; }
    public string? Pin2 { get; set; }
    public string? Puk { get; set; }
    public string? Puk2 { get; set; }

    /// <summary>Tension d'alimentation en volts (1,8 / 3 / 5 selon les générations).</summary>
    public string? Voltage { get; set; }

    public string? Country { get; set; }

    /// <summary>Voix sur IP autorisée sur la ligne (<c>allow_voip</c> côté GLPI).</summary>
    public bool AllowVoip { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<SimCardHistoryEntry> HistoryEntries { get; set; } = [];
}

public class SimCardHistoryEntry
{
    public int Id { get; set; }
    public int SimCardId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
