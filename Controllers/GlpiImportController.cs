using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GlpiNg.Modules.Inventory.Controllers;

/// <summary>
/// Déclenche l'import de données depuis une base GLPI MySQL source (configuration
/// "GlpiImport:ConnectionString" — voir appsettings/user-secrets).
///
/// Protégé par un jeton OAuth2 Bearer portant le scope "api" ou "inventory" (policy
/// "OAuthApiAccess" définie dans Program.cs) plutôt que par le FallbackPolicy global
/// (RequireAuthenticatedUser, cookie de session) : un appelant machine-à-machine (script,
/// intégration CI) n'a pas de session applicative, seulement un client OAuth enregistré via
/// /oauth-clients — voir Controllers.OAuthController pour l'émission du jeton
/// (POST /oauth2/token, grant client_credentials).
/// </summary>
[ApiController]
[Route("admin/import/glpi")]
[Authorize(AuthenticationSchemes = "Bearer", Policy = "OAuthApiAccess")]
public class GlpiImportController(GlpiMySqlImportService importService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GlpiImportResult>> Run(CancellationToken cancellationToken)
    {
        GlpiImportResult result = await importService.RunAsync(cancellationToken);
        return Ok(result);
    }
}
