using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Périphérique du parc (souris, imprimante, casque, lecteur de badge, ...) : équivalent de
/// glpi_peripherals dans GLPI. Contrairement à <see cref="Computer"/>, qui est alimenté par
/// l'inventaire automatique (agent GLPI / import MySQL), un Peripheral est un actif géré
/// manuellement depuis l'UI (voir Components/Pages/Peripherals), au même titre que GlpiGroup
/// ou GlpiEntity côté hôte.
/// </summary>
public class Peripheral : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Statut de l'élément (voir Models/DropdownItem.cs, DropdownType.Status) — même liste d'Intitulés que Computer.StatusId et ComputerPeripheral.StatusId ("État" dans GLPI, partagé entre types d'actifs).</summary>
    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }

    /// <summary>Type de périphérique (ex: "Souris", "Imprimante", "Casque"), texte libre.</summary>
    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Brand { get; set; }
    public string? SerialNumber { get; set; }
    public string? InventoryNumber { get; set; }
    public string? Uuid { get; set; }

    public string? Site { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }

    public string? TechnicianInCharge { get; set; }
    public string? AssignedUser { get; set; }
    public string? Contact { get; set; }
    public string? ContactNumber { get; set; }

    /// <summary>"Type de gestion" dans GLPI : true = gestion globale (un seul enregistrement
    /// pour plusieurs exemplaires identiques), false = gestion unitaire (un enregistrement par
    /// exemplaire physique).</summary>
    public bool IsGlobalManagement { get; set; }

    public string? Comment { get; set; }

    /// <summary>Ordinateur auquel ce périphérique est actuellement connecté (onglet
    /// "Connexions" de la fiche GLPI), optionnel.</summary>
    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<PeripheralHistoryEntry> HistoryEntries { get; set; } = [];
}

/// <summary>Ligne du journal de modifications d'un périphérique (onglet "Historique" de la fiche).</summary>
public class PeripheralHistoryEntry
{
    public int Id { get; set; }
    public int PeripheralId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
