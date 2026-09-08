namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Résumé, avant import, du contenu de la base GLPI MySQL source (voir
/// <see cref="GlpiMySqlImportService.AnalyzeAsync"/>) : nombre d'éléments par catégorie,
/// utilisé pour afficher les cases à cocher de <c>/admin/import/glpi</c> avec un compte
/// réel plutôt qu'à l'aveugle.
/// </summary>
public class GlpiImportAnalysis
{
    public int ComputersCount { get; set; }
    public int MonitorsCount { get; set; }
    public int AgentsCount { get; set; }
    public int CpuCount { get; set; }
    public int RamCount { get; set; }
    public int DiskCount { get; set; }
    public int NetworkCardCount { get; set; }
    public int SoftwaresCount { get; set; }
    public int PrintersCount { get; set; }
    public int PeripheralsCount { get; set; }
    public int VolumesCount { get; set; }
    public int BatteriesCount { get; set; }

    public int ComponentsCount => CpuCount + RamCount + DiskCount + NetworkCardCount;

    /// <summary>
    /// Plugin d'inventaire détecté sur la base source (GLPI Inventory / FusionInventory). Toujours
    /// renseigné : <see cref="GlpiInventoryPluginInfo.IsPresent"/> dit s'il y a quelque chose.
    /// </summary>
    public GlpiInventoryPluginInfo InventoryPlugin { get; set; } = new();
}
