using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Vérifie une valeur d'inventaire contre la liste noire (voir Models/ImportBlacklistEntry.cs) :
/// utilisé par InventoryImportService pour ignorer un numéro de série/MAC/IP connu comme
/// factice, comme si le champ était vide, plutôt que de l'enregistrer tel quel.
/// </summary>
public static class ImportBlacklistEngine
{
    public static bool IsBlacklisted(IReadOnlyDictionary<ImportBlacklistType, HashSet<string>> blacklist, ImportBlacklistType type, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && blacklist.TryGetValue(type, out HashSet<string>? values)
        && values.Contains(value);

    public static Dictionary<ImportBlacklistType, HashSet<string>> Index(IEnumerable<ImportBlacklistEntry> entries) =>
        entries
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Value).ToHashSet(StringComparer.OrdinalIgnoreCase));
}
