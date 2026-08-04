namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Lie uniquement <c>AgentCleanupDays</c> de la section "InventorySettings" de
/// appsettings.json (voir GlpiNg.Web.Models.InventorySettings) : ce module ne référence
/// pas le type hôte, seulement le nom de section, comme <see
/// cref="Import.GlpiImportOptions"/>. Piloté par <see
/// cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> pour profiter du même
/// rechargement à chaud que Swagger (voir SwaggerOptions côté hôte) : modifier la valeur
/// depuis /config → "Parc" met AgentCleanupCronTask à jour sans redémarrage.
/// </summary>
public class AgentCleanupOptions
{
    public const string SectionName = "InventorySettings";

    /// <summary>Nombre de jours d'inactivité au-delà duquel un agent est purgé. 0 ou négatif = désactivé.</summary>
    public int AgentCleanupDays { get; set; } = 1;
}
