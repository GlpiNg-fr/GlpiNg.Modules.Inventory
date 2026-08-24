using GlpiNg.Modules.Abstractions.Import;
using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GlpiNg.Modules.Inventory.Controllers;

/// <summary>
/// Déclenche l'import de données depuis une base GLPI MySQL source (configuration
/// "GlpiImport:ConnectionString" — voir appsettings/user-secrets) : parc (<see cref="GlpiMySqlImportService"/>)
/// et Administration/configuration générale (<see cref="IGlpiAdminImportService"/>, implémenté par
/// l'hôte) d'un seul coup, toutes catégories confondues (voir les constructeurs par défaut de
/// <see cref="GlpiImportSelection"/> et <see cref="GlpiAdminImportSelection"/>).
///
/// Protégé par un jeton OAuth2 Bearer portant le scope "api" ou "inventory" (policy
/// "OAuthApiAccess" définie dans Program.cs) plutôt que par le FallbackPolicy global
/// (RequireAuthenticatedUser, cookie de session) : un appelant machine-à-machine (script,
/// intégration CI) n'a pas de session applicative, seulement un client OAuth enregistré via
/// /config/oauth-clients — voir Controllers.OAuthController pour l'émission du jeton
/// (POST /oauth2/token, grant client_credentials).
/// </summary>
[ApiController]
[Route("admin/import/glpi")]
[Authorize(AuthenticationSchemes = "Bearer", Policy = "OAuthApiAccess")]
public class GlpiImportController(
    GlpiMySqlImportService importService,
    IGlpiAdminImportService adminImportService,
    IOptions<GlpiImportOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GlpiFullImportResult>> Run(CancellationToken cancellationToken)
    {
        GlpiImportResult inventoryResult = await importService.RunAsync(cancellationToken);
        GlpiAdminImportResult adminResult = await adminImportService.RunAsync(
            options.Value.ConnectionString, new GlpiAdminImportSelection(), cancellationToken);

        return Ok(new GlpiFullImportResult { Inventory = inventoryResult, Administration = adminResult });
    }
}
