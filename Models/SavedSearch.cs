using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Mode de calcul du nombre de résultats affiché pour une recherche sauvegardée, au sens GLPI ("Compter").</summary>
public enum SavedSearchCountMode
{
    Auto,
    Yes,
    No
}

/// <summary>
/// Recherche (critères + tri) enregistrée par un utilisateur sur la liste d'un type d'élément
/// (ItemType, ex. "Computer"), accessible depuis le bouton "Recherches sauvegardées" de la liste
/// concernée et gérée de façon centralisée depuis Outils > Recherches sauvegardées — calqué sur
/// GLPI (glpi_savedsearches / front/savedsearch.php).
/// CriteriaJson/SortJson sérialisent la forme de critères/tri propre à la page qui les a créées
/// (ex. Computers/Index.SearchCriterion[] et SortCriterion[] — voir Index.razor.cs).
/// </summary>
public class SavedSearch : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Type d'élément sur lequel porte la recherche (ex. "Computer", "Monitor"), au sens GLPI.</summary>
    public required string ItemType { get; set; }

    /// <summary>
    /// Propriétaire de la recherche. Référence "lâche" vers GlpiUser.Id (pas de clé étrangère :
    /// ce module ne dépend pas des types du host, voir GlpiNg.Modules.Inventory.csproj).
    /// Null si la recherche a été créée sans utilisateur authentifié résolu.
    /// </summary>
    public int? OwnerUserId { get; set; }

    /// <summary>Nom d'affichage du propriétaire, capturé à l'enregistrement (même raison que OwnerUserId : pas de jointure possible vers GlpiUser depuis ce module).</summary>
    public string? OwnerName { get; set; }

    /// <summary>Visible et utilisable par tous (comme GLPI) plutôt que réservée à son propriétaire.</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Recherche appliquée automatiquement à l'arrivée sur la page pour son propriétaire
    /// ("Définir par défaut" dans GLPI). Un seul true à la fois par (OwnerUserId, ItemType).
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Nombre de résultats au moment de l'enregistrement (mode "Compter: Auto" de GLPI).</summary>
    public int ResultCount { get; set; }

    /// <summary>Mode de calcul du compteur de résultats affiché ("Compter" dans le formulaire GLPI).</summary>
    public SavedSearchCountMode CountMode { get; set; } = SavedSearchCountMode.Auto;

    public required string CriteriaJson { get; set; }
    public required string SortJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
