using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Type d'actif pouvant être l'extrémité d'un <see cref="Cable"/> — limité aux types
/// de parc "physiques" qui existent déjà dans ce module au moment de l'écriture (voir
/// CableEndpointCatalog pour la résolution nom/route par type).</summary>
public enum CableEndpointType
{
    Computer,
    NetworkEquipment,
    Peripheral,
    Phone,
    Printer,
    PassiveEquipment,
}

/// <summary>
/// Câble du parc reliant deux actifs : équivalent de glpi_cables dans GLPI. Référence polymorphe
/// non contrainte vers ses deux extrémités (Type + Id, pas de clé étrangère EF), sur le même
/// principe que <c>DeploymentPackageTarget</c> (GlpiNg.Modules.Deployment) — ici tous les types
/// d'extrémité éligibles vivent dans ce même module Inventory, donc pas besoin de l'abstraction
/// d'annuaire inter-module utilisée par Deployment ; la résolution se fait directement via
/// CableEndpointCatalog.
/// </summary>
public class Cable : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }

    public int? StatusId { get; set; }
    public DropdownItem? StatusItem { get; set; }

    /// <summary>Type de câble (ex: "RJ45", "Fibre optique", "USB"), texte libre.</summary>
    public string? Type { get; set; }
    public string? Color { get; set; }
    public string? Comment { get; set; }

    public CableEndpointType EndpointAType { get; set; }
    public int EndpointAId { get; set; }

    public CableEndpointType? EndpointBType { get; set; }
    public int? EndpointBId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<CableHistoryEntry> HistoryEntries { get; set; } = [];
}

/// <summary>Ligne du journal de modifications d'un câble (onglet "Historique" de la fiche).</summary>
public class CableHistoryEntry
{
    public int Id { get; set; }
    public int CableId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
