namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Verrou sur un champ d'un élément : l'inventaire ne le met plus à jour, la saisie manuelle fait
/// foi. Équivalent de <c>glpi_lockedfields</c> dans GLPI.
///
/// Sans verrou, un champ corrigé à la main est réécrit à la remontée suivante de l'agent — quelques
/// heures plus tard, silencieusement (seul l'historique du poste en garde la trace). C'est le seul
/// moyen de faire coexister une valeur constatée par l'agent et une valeur décidée par un
/// administrateur.
///
/// <see cref="ItemType"/> plutôt qu'une table par type d'actif : le mécanisme vaut pour tout objet
/// alimenté par l'inventaire, même si seule la fiche Ordinateur l'expose pour l'instant. Pas
/// d'<c>IEntityScoped</c> : un verrou suit l'élément qu'il protège, dont le cloisonnement décide
/// déjà de la visibilité.
/// </summary>
public class LockedField
{
    public int Id { get; set; }

    /// <summary>Type d'élément, au sens GLPI — « Computer » pour l'instant.</summary>
    public required string ItemType { get; set; }

    public int ItemId { get; set; }

    /// <summary>Clé du champ verrouillé, telle que définie par <c>ComputerLockableFields</c>.</summary>
    public required string Field { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Qui a posé le verrou, pour que l'historique du poste reste lisible.</summary>
    public string? LockedBy { get; set; }
}
