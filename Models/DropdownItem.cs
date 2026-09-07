using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Catégorie d'Intitulé GLPI (front/dropdown.php) ou de Composant GLPI (front/devices.php) :
/// liste de valeurs de référence proposées à la saisie sur les fiches, ou catalogue de modèles de
/// composants matériel. Sous-ensemble des dizaines de catégories réelles de GLPI, restreint aux
/// champs effectivement portés par Computer dans GlpiNg (pas de matériel réseau/imprimantes/
/// moniteurs, pas de câblage/internet : ces domaines n'existent pas ici). Voir
/// DropdownTypeCatalog.Group pour la répartition Intitulés/Composants — les nouvelles valeurs
/// doivent être ajoutées en fin d'énumération (jamais insérées : Type est persisté comme entier).
/// </summary>
public enum DropdownType
{
    Manufacturer,
    ComputerType,
    ComputerModel,
    OperatingSystem,
    OperatingSystemVersion,
    Location,
    Status,

    // Catégories de Composants (front/devices.php) : catalogue de modèles de matériel, distinct
    // des Intitulés ci-dessus — voir DropdownTypeCatalog.Group.
    Processor,
    Memory,
    HardDrive,
    NetworkCard,
    GraphicCard,
    SoundCard,
    Drive,
    PciDevice,
    Camera,
    PowerSupply,
    Battery,
    Case,
    Motherboard,
    GenericDevice,
    Controller,
    Firmware,
    Sensor,
    SimCard
}

/// <summary>
/// Valeur d'un Intitulé : simple couple (Type, Name) avec commentaire optionnel. Contrairement à
/// DictionaryRule (qui normalise une valeur brute d'inventaire avant enregistrement), un
/// DropdownItem est une valeur de référence proposée à la saisie manuelle sur une fiche (voir
/// Computers/Detail.razor, champs Fabricant/Type/Modèle/Système d'exploitation/Version) — les deux
/// mécanismes sont indépendants et peuvent coexister sur un même champ.
/// </summary>
public class DropdownItem : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public DropdownType Type { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }

    /// <summary>
    /// Couleur d'affichage du badge associé à cette valeur — un hex "#rrggbb" ou le littéral
    /// "transparent" ; null si non définie (badge de secours neutre, voir StatusBadge.razor).
    /// Seul DropdownType.Status l'exploite pour l'instant (voir DropdownList.razor), mais le champ
    /// reste générique sur DropdownItem plutôt que sur un sous-type dédié.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Lieu parent (arborescence Site &gt; Bâtiment &gt; Salle, comme dans GLPI) — seul
    /// DropdownType.Location l'exploite pour l'instant (voir DropdownList.razor), même principe
    /// que Color ci-dessus : champ générique sur DropdownItem plutôt qu'un sous-type dédié.
    /// </summary>
    public int? ParentId { get; set; }
    public DropdownItem? Parent { get; set; }
}
