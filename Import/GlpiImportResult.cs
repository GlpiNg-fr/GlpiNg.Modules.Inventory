namespace GlpiNg.Modules.Inventory.Import;

/// <summary>Résumé d'une exécution de l'import GLPI.</summary>
public class GlpiImportResult
{
    public int ComputersCreated { get; set; }
    public int ComputersUpdated { get; set; }
    public int AgentsCreated { get; set; }
    public int AgentsUpdated { get; set; }
    public int ComponentsImported { get; set; }
    public List<string> Warnings { get; set; } = [];
    public TimeSpan Duration { get; set; }
}
