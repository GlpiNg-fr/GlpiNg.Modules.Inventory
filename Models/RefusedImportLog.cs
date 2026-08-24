namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Trace un inventaire rejeté par une <see cref="ImportAssignmentRule"/> (action RefuseImport) :
/// aucun Computer n'a été créé ni mis à jour pour cette requête. Équivalent de l'historique des
/// équipements refusés de GLPI (Administration > GLPI Inventory > Règles > "Matériel ignoré
/// durant l'import").
/// </summary>
public class RefusedImportLog
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string RuleName { get; set; }
    public string? ComputerName { get; set; }
    public string? SerialNumber { get; set; }
    public string? Domain { get; set; }
    public string? Tag { get; set; }
    public string? IpAddress { get; set; }
    public string? AgentIdentifier { get; set; }
}
