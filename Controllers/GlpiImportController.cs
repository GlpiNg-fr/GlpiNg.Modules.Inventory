using GlpiNg.Modules.Abstractions.Import;
using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GlpiNg.Modules.Inventory.Controllers;

/// <summary>
/// Déclenche l'import de données depuis une base GLPI MySQL source (configuration
/// "GlpiImport:ConnectionString" — voir appsettings/user-secrets) : parc
/// (<see cref="GlpiMySqlImportService"/>), Administration/configuration générale
/// (<see cref="IGlpiAdminImportService"/>) et base de connaissances
/// (<see cref="IGlpiKnowledgeBaseImportService"/>, ces deux derniers implémentés par l'hôte),
/// d'un seul coup et toutes catégories confondues.
///
/// Pour la base de connaissances, les quatre catégories sont demandées explicitement plutôt que
/// laissées au constructeur par défaut : la sélection de cet import démarre vide, parce que la
/// page la remplit depuis l'analyse de la base source. Ici, il n'y a pas d'analyse — un appelant
/// machine-à-machine veut tout, et le dire ici vaut mieux qu'un défaut discret qui changerait le
/// comportement des deux appelants à la fois.
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
    IGlpiKnowledgeBaseImportService knowledgeBaseImportService,
    IOptions<GlpiImportOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GlpiFullImportResult>> Run(CancellationToken cancellationToken)
    {
        GlpiImportResult inventoryResult = await importService.RunAsync(cancellationToken);
        GlpiAdminImportResult adminResult = await adminImportService.RunAsync(
            options.Value.ConnectionString, new GlpiAdminImportSelection(), progress: null, cancellationToken);

        // Après l'administration : les auteurs et les cibles de visibilité des articles renvoient
        // aux comptes, groupes, profils et entités qu'elle vient de créer.
        GlpiKnowledgeBaseImportResult knowledgeBaseResult = await knowledgeBaseImportService.RunAsync(
            options.Value.ConnectionString,
            new GlpiKnowledgeBaseImportSelection
            {
                ImportCategories = true,
                ImportArticles = true,
                ImportTargets = true,
                ImportRevisions = true,
            },
            progress: null,
            cancellationToken);

        return Ok(new GlpiFullImportResult
        {
            Inventory = inventoryResult,
            Administration = adminResult,
            KnowledgeBase = knowledgeBaseResult,
        });
    }
}
