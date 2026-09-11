namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Libellés, slugs d'URL et icônes de DictionaryRuleType, centralisés pour rester cohérents
/// entre le menu, le sous-menu d'onglets et les pages de règles (Components/Pages/Dictionaries).
/// </summary>
public static class DictionaryRuleTypeCatalog
{
    public static readonly IReadOnlyList<DictionaryRuleType> All =
    [
        DictionaryRuleType.Manufacturer,
        DictionaryRuleType.ComputerModel,
        DictionaryRuleType.OperatingSystem,
        DictionaryRuleType.OperatingSystemVersion,
        DictionaryRuleType.Software
    ];

    public static string Slug(DictionaryRuleType type) => type switch
    {
        DictionaryRuleType.Manufacturer => "manufacturer",
        DictionaryRuleType.ComputerModel => "computer-model",
        DictionaryRuleType.OperatingSystem => "operating-system",
        DictionaryRuleType.OperatingSystemVersion => "operating-system-version",
        DictionaryRuleType.Software => "software",
        _ => type.ToString()
    };

    public static bool TryParseSlug(string? slug, out DictionaryRuleType type)
    {
        foreach (DictionaryRuleType candidate in All)
        {
            if (string.Equals(Slug(candidate), slug, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    public static string Label(DictionaryRuleType type) => type switch
    {
        DictionaryRuleType.Manufacturer => "Fabricants",
        DictionaryRuleType.ComputerModel => "Modèles d'ordinateurs",
        DictionaryRuleType.OperatingSystem => "Systèmes d'exploitation",
        DictionaryRuleType.OperatingSystemVersion => "Versions de système d'exploitation",
        DictionaryRuleType.Software => "Logiciels",
        _ => type.ToString()
    };

    public static string Description(DictionaryRuleType type) => type switch
    {
        DictionaryRuleType.Manufacturer => "Normalise le nom de fabricant remonté par le BIOS des ordinateurs.",
        DictionaryRuleType.ComputerModel => "Normalise le nom de modèle remonté par le BIOS des ordinateurs.",
        DictionaryRuleType.OperatingSystem => "Normalise le nom du système d'exploitation remonté par l'agent.",
        DictionaryRuleType.OperatingSystemVersion => "Normalise la version du système d'exploitation remontée par l'agent.",
        DictionaryRuleType.Software => "Normalise le nom des logiciels installés, ou ignore leur import.",
        _ => string.Empty
    };

    public static string Icon(DictionaryRuleType type) => type switch
    {
        DictionaryRuleType.Manufacturer => "ti-building-factory-2",
        DictionaryRuleType.ComputerModel => "ti-device-desktop",
        DictionaryRuleType.OperatingSystem => "ti-brand-windows",
        DictionaryRuleType.OperatingSystemVersion => "ti-versions",
        DictionaryRuleType.Software => "ti-apps",
        _ => "ti-book-2"
    };

    /// <summary>Seul DictionaryRuleType.Software expose un critère sur le fabricant (DictionaryCriterionField.Publisher) en plus du nom.</summary>
    public static bool SupportsPublisherCriterion(DictionaryRuleType type) => type == DictionaryRuleType.Software;

    /// <summary>
    /// Champs sur lesquels un critère peut porter, pour ce type de dictionnaire.
    ///
    /// Toujours au moins le nom. C'est cette liste qui alimente le sélecteur de champ : présenter
    /// une liste d'un seul élément vaut mieux qu'un champ désactivé, qui se lit comme une panne
    /// alors qu'il n'y a simplement rien d'autre à choisir.
    /// </summary>
    public static IReadOnlyList<DictionaryCriterionField> CriterionFields(DictionaryRuleType type) =>
        SupportsPublisherCriterion(type)
            ? [DictionaryCriterionField.Name, DictionaryCriterionField.Publisher]
            : [DictionaryCriterionField.Name];
}
