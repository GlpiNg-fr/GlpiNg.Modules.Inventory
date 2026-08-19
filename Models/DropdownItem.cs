namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Catégorie d'Intitulé GLPI (front/dropdown.php) : liste de valeurs de référence proposées à
/// la saisie sur les fiches. Sous-ensemble des dizaines de catégories réelles de GLPI, restreint
/// aux champs effectivement portés par Computer dans GlpiNg (pas de matériel réseau/imprimantes/
/// moniteurs, pas de câblage/internet : ces domaines n'existent pas ici).
/// </summary>
public enum DropdownType
{
    Manufacturer,
    ComputerType,
    ComputerModel,
    OperatingSystem,
    OperatingSystemVersion,
    Location,
    Status
}

/// <summary>
/// Valeur d'un Intitulé : simple couple (Type, Name) avec commentaire optionnel. Contrairement à
/// DictionaryRule (qui normalise une valeur brute d'inventaire avant enregistrement), un
/// DropdownItem est une valeur de référence proposée à la saisie manuelle sur une fiche (voir
/// Computers/Detail.razor, champs Fabricant/Type/Modèle/Système d'exploitation/Version) — les deux
/// mécanismes sont indépendants et peuvent coexister sur un même champ.
/// </summary>
public class DropdownItem
{
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
