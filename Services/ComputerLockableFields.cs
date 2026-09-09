namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Champs d'un ordinateur qu'un verrou peut protéger : ceux, et seulement ceux, que l'inventaire
/// écrit. Verrouiller un champ que l'agent ne touche jamais n'aurait aucun effet, et laisser
/// l'écran le proposer laisserait croire le contraire.
///
/// Les clés reprennent celles de <see cref="ComputerRuleFieldCatalog"/> quand le champ y figure :
/// les deux mécanismes désignent les mêmes champs, ils n'ont pas de raison de les nommer
/// différemment.
/// </summary>
public static class ComputerLockableFields
{
    /// <summary>Type d'élément de ces verrous, au sens de <see cref="Models.LockedField.ItemType"/>.</summary>
    public const string ItemType = "Computer";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("Name", "Nom"),
        ("SerialNumber", "Numéro de série"),
        ("Manufacturer", "Fabricant"),
        ("Model", "Modèle"),
        ("OperatingSystem", "Système d'exploitation"),
        ("OsVersion", "Version de l'OS"),
        ("OsKernelVersion", "Version du noyau"),
        ("ChassisType", "Type de châssis"),
        ("HardwareUuid", "UUID matériel"),
        ("Domain", "Domaine"),
        ("VmSystem", "Virtualisation"),
        ("LastLoggedUser", "Dernier utilisateur connecté"),
        ("TotalMemoryMb", "Mémoire totale"),
        ("RemoteManagement", "Prise en main à distance"),
    ];

    public static readonly IReadOnlySet<string> Keys = All.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);

    public static string Label(string key) => All.FirstOrDefault(field => field.Key == key).Label ?? key;

    /// <summary>
    /// Le statut n'est volontairement pas verrouillable : il n'est plus affecté qu'à la création du
    /// poste (voir InventorySettings.DefaultComputerStatus), donc aucun inventaire ultérieur ne
    /// l'écrase — un verrou n'aurait rien à protéger.
    /// </summary>
    public static bool IsLockable(string key) => Keys.Contains(key);
}
