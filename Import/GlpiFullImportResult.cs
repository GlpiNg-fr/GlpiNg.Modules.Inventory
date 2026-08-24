using GlpiNg.Modules.Abstractions.Import;

namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Combine le résultat de l'import parc (<see cref="GlpiImportResult"/>, module Inventory) et de
/// l'import Administration/configuration générale (<see cref="GlpiAdminImportResult"/>, implémenté
/// par l'hôte — voir <see cref="Abstractions.Import.IGlpiAdminImportService"/>) pour la réponse de
/// <c>POST /admin/import/glpi</c> (appel machine-à-machine, qui déclenche les deux d'un coup).
/// </summary>
public class GlpiFullImportResult
{
    public required GlpiImportResult Inventory { get; set; }
    public required GlpiAdminImportResult Administration { get; set; }
}
