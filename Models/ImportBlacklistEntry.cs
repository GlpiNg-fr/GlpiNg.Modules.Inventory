namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Nature de la valeur factice couverte par une entrée de liste noire — voir ImportBlacklistEntry.</summary>
public enum ImportBlacklistType
{
    IpAddress,
    MacAddress,
    SerialNumber
}

/// <summary>
/// Valeur connue pour être un "placeholder" du constructeur/BIOS plutôt qu'une vraie identité
/// matérielle (ex. numéro de série "SYS-1234567890", MAC "00:00:00:00:00:00", IP "127.0.0.1") :
/// quand l'inventaire remonte une valeur listée ici, InventoryImportService l'ignore comme si le
/// champ était vide plutôt que de l'enregistrer telle quelle. Équivalent de la "Liste noire"
/// de GLPI (Administration > GLPI Inventory > Règles > Liste noire).
///
/// Volontairement une simple table de valeurs (pas de moteur critères/actions) : c'est aussi ce
/// que fait GLPI ici, contrairement à RuleImportEntity (voir ImportAssignmentRule.cs) qui est un
/// vrai moteur de règles.
/// </summary>
public class ImportBlacklistEntry
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ImportBlacklistType Type { get; set; }
    public required string Value { get; set; }
}
