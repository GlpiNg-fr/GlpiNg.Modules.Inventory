using GlpiNg.Modules.Abstractions.Localization;
﻿namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Regroupement d'un DropdownType : Intitulés (front/dropdown.php, valeurs de référence
/// proposées à la saisie sur les fiches) ou Composants (front/devices.php, catalogue de modèles
/// de matériel). Détermine quelle route/menu (/config/dropdowns vs /config/components) affiche
/// la catégorie — voir DropdownList.razor.
/// </summary>
public enum DropdownGroup
{
    Labels,
    Components
}

/// <summary>
/// Libellés, slugs d'URL et icônes de DropdownType, centralisés pour rester cohérents entre le
/// menu, le sous-menu d'onglets et les pages d'Intitulés/Composants (Components/Pages/Dropdowns)
/// — même principe que DictionaryRuleTypeCatalog pour DictionaryRuleType.
/// </summary>
public static class DropdownTypeCatalog
{
    public static readonly IReadOnlyList<DropdownType> Labels =
    [
        DropdownType.Manufacturer,
        DropdownType.ComputerType,
        DropdownType.ComputerModel,
        DropdownType.OperatingSystem,
        DropdownType.OperatingSystemVersion,
        DropdownType.Location,
        DropdownType.Status
    ];

    /// <summary>Catégories de Composants (front/devices.php GLPI), dans le même ordre que le menu GLPI d'origine.</summary>
    public static readonly IReadOnlyList<DropdownType> Components =
    [
        DropdownType.Processor,
        DropdownType.Memory,
        DropdownType.HardDrive,
        DropdownType.NetworkCard,
        DropdownType.GraphicCard,
        DropdownType.SoundCard,
        DropdownType.Motherboard,
        DropdownType.PowerSupply,
        DropdownType.Battery,
        DropdownType.Drive,
        DropdownType.PciDevice,
        DropdownType.Camera,
        DropdownType.Case,
        DropdownType.Controller,
        DropdownType.GenericDevice,
        DropdownType.Firmware,
        DropdownType.Sensor,
        DropdownType.SimCard
    ];

    /// <summary>Toutes les catégories (Intitulés + Composants) — utilisé uniquement par TryParseSlug, qui doit résoudre un slug quel que soit son groupe.</summary>
    private static readonly IReadOnlyList<DropdownType> All = [.. Labels, .. Components];

    /// <summary>
    /// Catégorie de catalogue correspondant à un type de composant remonté par l'inventaire.
    ///
    /// Les deux énumérations ne se recouvrent pas exactement : un modem n'a pas de catégorie à lui
    /// côté GLPI et rejoint les composants génériques. Une catégorie sans équivalent rend
    /// <c>null</c> — le composant existe alors sur la fiche du poste sans entrer au catalogue.
    /// </summary>
    public static DropdownType? ForComponent(ComponentType type) => type switch
    {
        ComponentType.Cpu => DropdownType.Processor,
        ComponentType.Ram => DropdownType.Memory,
        ComponentType.Disk => DropdownType.HardDrive,
        ComponentType.NetworkCard => DropdownType.NetworkCard,
        ComponentType.Gpu => DropdownType.GraphicCard,
        ComponentType.Motherboard => DropdownType.Motherboard,
        ComponentType.Controller => DropdownType.Controller,
        ComponentType.SoundCard => DropdownType.SoundCard,
        ComponentType.Modem => DropdownType.GenericDevice,
        ComponentType.Firmware => DropdownType.Firmware,
        _ => null,
    };

    /// <summary>Types de composants alimentant une catégorie de catalogue — l'inverse de
    /// <see cref="ForComponent"/>, pour reconstruire une catégorie depuis le parc.</summary>
    public static IReadOnlyList<ComponentType> ComponentTypesFor(DropdownType type) =>
        [.. Enum.GetValues<ComponentType>().Where(candidate => ForComponent(candidate) == type)];

    /// <summary>
    /// Une désignation ne mérite le catalogue que si elle nomme quelque chose. Les valeurs de repli
    /// posées par l'import quand l'agent n'a rien dit en sont écartées : les cataloguer créerait un
    /// « modèle » nommé « inconnu » qu'aucun achat ne pourra jamais rapprocher.
    /// </summary>
    public static bool IsCatalogueableName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length > 1
        && !name.Contains("inconnu", StringComparison.OrdinalIgnoreCase);

    public static DropdownGroup Group(DropdownType type) =>
        Labels.Contains(type) ? DropdownGroup.Labels : DropdownGroup.Components;

    public static string GroupLabel(DropdownGroup group) => group switch
    {
        DropdownGroup.Labels => Tr.T("Intitulés"),
        DropdownGroup.Components => Tr.T("Composants"),
        _ => group.ToString()
    };

    public static string GroupBaseRoute(DropdownGroup group) => group switch
    {
        DropdownGroup.Components => "/config/components",
        _ => "/config/dropdowns"
    };

    public static string Slug(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => "manufacturer",
        DropdownType.ComputerType => "computer-type",
        DropdownType.ComputerModel => "computer-model",
        DropdownType.OperatingSystem => "operating-system",
        DropdownType.OperatingSystemVersion => "operating-system-version",
        DropdownType.Location => "location",
        DropdownType.Status => "status",
        DropdownType.Processor => "processor",
        DropdownType.Memory => "memory",
        DropdownType.HardDrive => "hard-drive",
        DropdownType.NetworkCard => "network-card",
        DropdownType.GraphicCard => "graphic-card",
        DropdownType.SoundCard => "sound-card",
        DropdownType.Drive => "drive",
        DropdownType.PciDevice => "pci-device",
        DropdownType.Camera => "camera",
        DropdownType.PowerSupply => "power-supply",
        DropdownType.Battery => "battery",
        DropdownType.Case => "case",
        DropdownType.Motherboard => "motherboard",
        DropdownType.GenericDevice => "generic-device",
        DropdownType.Controller => "controller",
        DropdownType.Firmware => "firmware",
        DropdownType.Sensor => "sensor",
        DropdownType.SimCard => "sim-card",
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
        DropdownType.Manufacturer => Tr.T("Fabricants"),
        DropdownType.ComputerType => Tr.T("Types d'ordinateurs"),
        DropdownType.ComputerModel => Tr.T("Modèles d'ordinateurs"),
        DropdownType.OperatingSystem => Tr.T("Systèmes d'exploitation"),
        DropdownType.OperatingSystemVersion => Tr.T("Versions de système d'exploitation"),
        DropdownType.Location => Tr.T("Lieux"),
        DropdownType.Status => Tr.T("Statuts des éléments"),
        DropdownType.Processor => Tr.T("Processeurs"),
        DropdownType.Memory => Tr.T("Mémoires"),
        DropdownType.HardDrive => Tr.T("Disques durs"),
        DropdownType.NetworkCard => Tr.T("Cartes réseau"),
        DropdownType.GraphicCard => Tr.T("Cartes graphiques"),
        DropdownType.SoundCard => Tr.T("Cartes son"),
        DropdownType.Drive => Tr.T("Lecteurs"),
        DropdownType.PciDevice => Tr.T("Périphériques PCI"),
        DropdownType.Camera => Tr.T("Caméras"),
        DropdownType.PowerSupply => Tr.T("Alimentations"),
        DropdownType.Battery => Tr.T("Batteries"),
        DropdownType.Case => Tr.T("Boîtiers"),
        DropdownType.Motherboard => Tr.T("Cartes mères"),
        DropdownType.GenericDevice => Tr.T("Composants génériques"),
        DropdownType.Controller => Tr.T("Contrôleurs"),
        DropdownType.Firmware => Tr.T("Firmware"),
        DropdownType.Sensor => Tr.T("Capteurs"),
        DropdownType.SimCard => Tr.T("Cartes SIM"),
        _ => type.ToString()
    };

    public static string Description(DropdownType type) => type switch
    {
        DropdownType.Manufacturer => Tr.T("Fabricants proposés à la saisie sur la fiche Ordinateur (champ Fabricant)."),
        DropdownType.ComputerType => Tr.T("Types de matériel proposés à la saisie sur la fiche Ordinateur (champ Type)."),
        DropdownType.ComputerModel => Tr.T("Modèles proposés à la saisie sur la fiche Ordinateur (champ Modèle)."),
        DropdownType.OperatingSystem => Tr.T("Systèmes d'exploitation proposés à la saisie sur la fiche Ordinateur."),
        DropdownType.OperatingSystemVersion => Tr.T("Versions de système d'exploitation proposées à la saisie sur la fiche Ordinateur."),
        DropdownType.Location => Tr.T("Lieux (sites, bâtiments, salles) de l'organisation, organisables en arborescence via un lieu parent."),
        DropdownType.Status => Tr.T("Statuts proposés à la saisie sur les fiches Ordinateur, Écran et Périphérique."),
        DropdownType.Processor => Tr.T("Catalogue des modèles de processeurs connus (composants matériel)."),
        DropdownType.Memory => Tr.T("Catalogue des modèles de barrettes mémoire connus (composants matériel)."),
        DropdownType.HardDrive => Tr.T("Catalogue des modèles de disques durs connus (composants matériel)."),
        DropdownType.NetworkCard => Tr.T("Catalogue des modèles de cartes réseau connus (composants matériel)."),
        DropdownType.GraphicCard => Tr.T("Catalogue des modèles de cartes graphiques connus (composants matériel)."),
        DropdownType.SoundCard => Tr.T("Catalogue des modèles de cartes son connus (composants matériel)."),
        DropdownType.Drive => Tr.T("Catalogue des modèles de lecteurs (CD/DVD, bande, ...) connus (composants matériel)."),
        DropdownType.PciDevice => Tr.T("Catalogue des modèles de périphériques PCI connus (composants matériel)."),
        DropdownType.Camera => Tr.T("Catalogue des modèles de caméras connus (composants matériel)."),
        DropdownType.PowerSupply => Tr.T("Catalogue des modèles d'alimentations connus (composants matériel)."),
        DropdownType.Battery => Tr.T("Catalogue des modèles de batteries connus (composants matériel)."),
        DropdownType.Case => Tr.T("Catalogue des modèles de boîtiers connus (composants matériel)."),
        DropdownType.Motherboard => Tr.T("Catalogue des modèles de cartes mères connus (composants matériel)."),
        DropdownType.GenericDevice => Tr.T("Catalogue des composants génériques ne rentrant dans aucune autre catégorie."),
        DropdownType.Controller => Tr.T("Catalogue des modèles de contrôleurs (RAID, USB, ...) connus (composants matériel)."),
        DropdownType.Firmware => Tr.T("Catalogue des firmwares (BIOS, UEFI, ...) connus (composants matériel)."),
        DropdownType.Sensor => Tr.T("Catalogue des modèles de capteurs connus (composants matériel)."),
        DropdownType.SimCard => Tr.T("Catalogue des modèles de cartes SIM connus (composants matériel)."),
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
        // Icônes alignées sur celles déjà utilisées pour ComponentType dans Computers/Detail.razor.cs
        // (Cpu/Ram/Disk/NetworkCard/Gpu/Motherboard) pour rester cohérent visuellement.
        DropdownType.Processor => "ti-cpu",
        DropdownType.Memory => "ti-dimensions",
        DropdownType.HardDrive => "ti-device-floppy",
        DropdownType.NetworkCard => "ti-network",
        DropdownType.GraphicCard => "ti-device-gamepad",
        DropdownType.SoundCard => "ti-volume",
        DropdownType.Drive => "ti-disc",
        DropdownType.PciDevice => "ti-plug-connected",
        DropdownType.Camera => "ti-camera",
        DropdownType.PowerSupply => "ti-plug",
        DropdownType.Battery => "ti-battery",
        DropdownType.Case => "ti-box",
        DropdownType.Motherboard => "ti-circuit-board",
        DropdownType.GenericDevice => "ti-puzzle",
        DropdownType.Controller => "ti-adjustments",
        DropdownType.Firmware => "ti-chip",
        DropdownType.Sensor => "ti-antenna",
        DropdownType.SimCard => "ti-sim-card",
        _ => "ti-list"
    };
}
