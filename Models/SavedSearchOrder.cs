namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Position d'affichage d'une <see cref="SavedSearch"/> dans le panneau "Recherches
/// sauvegardées", propre à un utilisateur donné (glisser-déposer sur les lignes du panneau —
/// voir Computers/Index.razor.cs). Un même SavedSearch public peut donc être classé
/// différemment par chaque utilisateur qui le voit ; d'où une table séparée plutôt qu'un champ
/// Position directement sur SavedSearch.
/// </summary>
public class SavedSearchOrder
{
    public int Id { get; set; }
    public required int UserId { get; set; }
    public required int SavedSearchId { get; set; }
    public int Position { get; set; }
}
