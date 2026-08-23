namespace GlpiNg.Modules.Inventory.Import;

/// <summary>Résumé d'une exécution de l'import GLPI.</summary>
public class GlpiImportResult
{
    public int ComputersCreated { get; set; }
    public int ComputersUpdated { get; set; }
    public int AgentsCreated { get; set; }
    public int AgentsUpdated { get; set; }
    public int ComponentsImported { get; set; }
    public int MonitorsImported { get; set; }
    public int SoftwaresImported { get; set; }
    public int PrintersImported { get; set; }
    public int PeripheralsImported { get; set; }
    public int VolumesImported { get; set; }
    public int BatteriesImported { get; set; }
    public List<string> Warnings { get; set; } = [];
    public TimeSpan Duration { get; set; }
}
