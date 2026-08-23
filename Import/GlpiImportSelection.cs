namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Catégories choisies par l'admin pour un import GLPI MySQL (voir <c>/admin/import/glpi</c>),
/// via les cases à cocher affichées après <see cref="GlpiMySqlImportService.AnalyzeAsync"/>.
/// Le constructeur par défaut (tout à <c>true</c>) est celui utilisé par l'endpoint
/// <c>POST /admin/import/glpi</c> (appel machine-à-machine, sans étape d'analyse préalable).
/// </summary>
public class GlpiImportSelection
{
    public bool ImportComputers { get; set; } = true;
    public bool ImportMonitors { get; set; } = true;
    public bool ImportAgents { get; set; } = true;

    /// <summary>Composants matériels (CPU, RAM, disques, cartes réseau) — importés comme un seul bloc, voir ImportComponentsAsync.</summary>
    public bool ImportComponents { get; set; } = true;

    public bool ImportSoftwares { get; set; } = true;
    public bool ImportPrinters { get; set; } = true;
    public bool ImportPeripherals { get; set; } = true;
    public bool ImportVolumes { get; set; } = true;
    public bool ImportBatteries { get; set; } = true;

    /// <summary>
    /// Vrai si au moins une catégorie rattachée à un poste (donc pas "Ordinateurs" lui-même) est
    /// sélectionnée — sert à décider si <c>glpiComputerIdToLocalId</c> doit être reconstruit
    /// depuis les postes déjà connus en base quand "Ordinateurs" est décoché.
    /// </summary>
    public bool AnyComputerDependentCategorySelected =>
        ImportMonitors || ImportAgents || ImportComponents || ImportSoftwares || ImportPrinters || ImportPeripherals || ImportVolumes || ImportBatteries;

    public bool AnySelected => ImportComputers || AnyComputerDependentCategorySelected;
}
