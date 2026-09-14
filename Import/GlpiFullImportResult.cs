using GlpiNg.Modules.Abstractions.Import;

namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Combine les résultats des imports déclenchés d'un seul coup par <c>POST /admin/import/glpi</c>
/// (appel machine-à-machine) : le parc (<see cref="GlpiImportResult"/>, module Inventory),
/// l'Administration et la configuration générale (<see cref="GlpiAdminImportResult"/>), et la base
/// de connaissances (<see cref="GlpiKnowledgeBaseImportResult"/>) — ces deux derniers implémentés
/// par l'hôte, voir <see cref="Abstractions.Import.IGlpiAdminImportService"/> et
/// <see cref="Abstractions.Import.IGlpiKnowledgeBaseImportService"/>.
/// </summary>
public class GlpiFullImportResult
{
    public required GlpiImportResult Inventory { get; set; }
    public required GlpiAdminImportResult Administration { get; set; }
    public required GlpiKnowledgeBaseImportResult KnowledgeBase { get; set; }
}
