namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Colonnes affichées (clés + ordre) dans la liste d'un type d'élément (ItemType, ex.
/// "Computer"), propre à un utilisateur donné — calqué sur le panneau "Sélectionner les
/// colonnes à afficher" de GLPI (front/displaypreference.form.php), en version personnelle
/// uniquement (pas de "vue globale" administrateur pour l'instant).
/// </summary>
public class TableColumnPreference
{
    public int Id { get; set; }
    public required int UserId { get; set; }
    public required string ItemType { get; set; }

    /// <summary>Liste ordonnée des clés de colonnes (voir Computers/Index.razor.cs, SearchFields), hors "name" toujours affichée en premier.</summary>
    public required string ColumnsJson { get; set; }
}
