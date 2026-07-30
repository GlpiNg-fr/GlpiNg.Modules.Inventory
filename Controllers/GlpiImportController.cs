using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Mvc;

namespace GlpiNg.Modules.Inventory.Controllers;

/// <summary>
/// Déclenche l'import de données depuis une base GLPI MySQL source (configuration
/// "GlpiImport:ConnectionString" — voir appsettings/user-secrets).
///
/// NB : endpoint d'administration sans authentification dans ce squelette — à
/// protéger (rôle admin) avant toute exposition hors environnement de développement.
/// </summary>
[ApiController]
[Route("admin/import/glpi")]
public class GlpiImportController(GlpiMySqlImportService importService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GlpiImportResult>> Run(CancellationToken cancellationToken)
    {
        GlpiImportResult result = await importService.RunAsync(cancellationToken);
        return Ok(result);
    }
}
