namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Libellés, slugs d'URL et icônes de DropdownType, centralisés pour rester cohérents entre le
/// menu, le sous-menu d'onglets et les pages d'Intitulés (Components/Pages/Dropdowns) — même
/// principe que DictionaryRuleTypeCatalog pour DictionaryRuleType.
/// </summary>
public static class DropdownTypeCatalog
{
    public static readonly IReadOnlyList<DropdownType> All =
    [
        DropdownType.Manufacturer,
        DropdownType.ComputerType,
        DropdownType.ComputerModel,
        DropdownType.OperatingSystem,
        DropdownType.OperatingSystemVersion,
        DropdownType.Location,
        DropdownType.Status
    ];

    public static string Slug(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => "manufacturer",
        DropdownType.ComputerType => "computer-type",
        DropdownType.ComputerModel => "computer-model",
        DropdownType.OperatingSystem => "operating-system",
        DropdownType.OperatingSystemVersion => "operating-system-version",
        DropdownType.Location => "location",
        DropdownType.Status => "status",
        _ => type.ToString()
    };

    public static bool TryParseSlug(string? slug, out DropdownType type)
    {
        foreach (DropdownType candidate in All)
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

    public static string Label(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => "Fabricants",
        DropdownType.ComputerType => "Types d'ordinateurs",
        DropdownType.ComputerModel => "Modèles d'ordinateurs",
        DropdownType.OperatingSystem => "Systèmes d'exploitation",
        DropdownType.OperatingSystemVersion => "Versions de système d'exploitation",
        DropdownType.Location => "Lieux",
        DropdownType.Status => "Statuts des éléments",
        _ => type.ToString()
    };

    public static string Description(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => "Fabricants proposés à la saisie sur la fiche Ordinateur (champ Fabricant).",
        DropdownType.ComputerType => "Types de matériel proposés à la saisie sur la fiche Ordinateur (champ Type).",
        DropdownType.ComputerModel => "Modèles proposés à la saisie sur la fiche Ordinateur (champ Modèle).",
        DropdownType.OperatingSystem => "Systèmes d'exploitation proposés à la saisie sur la fiche Ordinateur.",
        DropdownType.OperatingSystemVersion => "Versions de système d'exploitation proposées à la saisie sur la fiche Ordinateur.",
        DropdownType.Location => "Lieux (sites, bâtiments, salles) de l'organisation.",
        DropdownType.Status => "Statuts proposés à la saisie sur les fiches Ordinateur, Écran et Périphérique.",
        _ => string.Empty
    };

    public static string Icon(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => "ti-building-factory-2",
        DropdownType.ComputerType => "ti-device-desktop",
        DropdownType.ComputerModel => "ti-cube",
        DropdownType.OperatingSystem => "ti-brand-windows",
        DropdownType.OperatingSystemVersion => "ti-versions",
        DropdownType.Location => "ti-map-pin",
        DropdownType.Status => "ti-flag",
        _ => "ti-list"
    };
}
