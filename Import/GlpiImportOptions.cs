namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Configuration de connexion à la base MySQL GLPI source (section "GlpiImport" de
/// appsettings.json / user-secrets). La base est accédée en lecture seule.
/// </summary>
public class GlpiImportOptions
{
    public const string SectionName = "GlpiImport";

    public required string ConnectionString { get; set; }
}
