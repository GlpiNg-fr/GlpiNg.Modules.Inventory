namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Registre des types d'éléments (au sens GLPI ItemType) sur lesquels une recherche peut être
/// enregistrée dans ce module, utilisé par la page centrale Outils > Recherches sauvegardées
/// pour afficher un libellé et retrouver la liste correspondante.
/// </summary>
public static class SavedSearchItemTypes
{
    public static readonly (string ItemType, string Label, string Icon, string Route)[] All =
    [
        ("Computer", "Ordinateur", "ti-device-desktop", "/parc/computer"),
        ("Monitor", "Moniteur", "ti-device-tv", "/parc/monitors"),
        ("Software", "Logiciel", "ti-apps", "/parc/software"),
        ("Peripheral", "Périphérique", "ti-mouse", "/parc/peripherals"),
        ("NetworkEquipment", "Matériel réseau", "ti-router", "/parc/network-equipments"),
        ("Printer", "Imprimante", "ti-printer", "/parc/printers"),
        ("Phone", "Téléphone", "ti-phone", "/parc/phones"),
        ("Rack", "Baie", "ti-server-2", "/parc/racks"),
        ("Enclosure", "Châssis", "ti-layout-grid", "/parc/enclosures"),
        ("Pdu", "PDU", "ti-plug", "/parc/pdus"),
        ("PassiveEquipment", "Équipement passif", "ti-plug-connected", "/parc/passive-equipments"),
        ("Cable", "Câble", "ti-cable", "/parc/cables"),
        ("Agent", "Agent", "ti-cpu", "/tools/deployments/agent"),
    ];

    public static string LabelFor(string itemType) =>
        All.FirstOrDefault(entry => entry.ItemType == itemType).Label is { Length: > 0 } label ? label : itemType;

    public static string IconFor(string itemType) =>
        All.FirstOrDefault(entry => entry.ItemType == itemType).Icon is { Length: > 0 } icon ? icon : "ti-bookmark";

    public static string? RouteFor(string itemType) =>
        All.FirstOrDefault(entry => entry.ItemType == itemType).Route;
}
