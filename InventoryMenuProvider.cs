using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Contribue le groupe "Parc" (tout le parc matériel/logiciel), l'entrée "Recherches
/// sauvegardées" du groupe "Outils", les entrées "Inventaire"/"Dictionnaires"/"Import GLPI"/"Règles"/
/// "Règles d'import" du groupe "Administration", ainsi que les entrées "Intitulés"/"Composants" du
/// groupe "Configuration" —
/// qui relèvent toutes du domaine Inventory (voir Models/GlpiAgent.cs et le protocole GLPI-Agent
/// exposé par ce module, ainsi que Models/DropdownItem.cs et DropdownTypeCatalog pour les deux).
/// L'entrée "Déploiements" du même
/// groupe "Outils" est contribuée séparément par DeploymentMenuProvider (module
/// Déploiement, GlpiNg.Modules.Deployment) : les entrées de plusieurs IMenuProvider
/// partageant une clé de groupe sont fusionnées par l'hôte.
/// </summary>
public sealed class InventoryMenuProvider : IMenuProvider
{
    public IReadOnlyList<MenuGroup> GetMenuGroups() =>
    [
        new("parc", "ti-box", "Parc",
        [
            new("Tableau de bord", "/", "ti-layout-dashboard"),
            new("Ordinateurs", "/parc/computer", "ti-device-desktop"),
            new("Moniteurs", "/parc/monitors", "ti-device-tv"),
            new("Logiciels", "/parc/software", "ti-apps"),
            new("Matériels réseau", "/parc/network-equipments", "ti-router"),
            new("Périphériques", "/parc/peripherals", "ti-mouse"),
            new("Imprimantes", "/parc/printers", "ti-printer"),
            new("Cartouches", "/parc/cartridge-items", "ti-droplet"),
            new("Consommables", "/parc/consumable-items", "ti-package"),
            new("Téléphones", "/parc/phones", "ti-phone"),
            new("Baies", "/parc/racks", "ti-server-2"),
            new("Châssis", "/parc/enclosures", "ti-layout-grid"),
            new("PDU", "/parc/pdus", "ti-plug"),
            new("Équipements passifs", "/parc/passive-equipments", "ti-plug-connected"),
            new("Actifs non gérés", Icon: "ti-help"),
            new("Câbles", "/parc/cables", "ti-cable"),
            new("Carte SIM éléments", Icon: "ti-sim-card"),
            new("Global", "/parc/allassets", "ti-world"),
        ]),
        new("outils", "ti-briefcase", "Outils",
        [
            new("Recherches sauvegardées", "/tools/saved-searches", "ti-bookmarks"),
        ]),
        new("administration", "ti-shield", "Administration",
        [
            new("Inventaire", "/admin/inventory", "ti-clipboard-list"),
            new("Dictionnaires", "/admin/dictionaries", "ti-book-2"),
            new("Import GLPI", "/admin/import/glpi", "ti-database-import"),
            new("Règles", "/admin/rules", "ti-adjustments"),
            new("Règles d'import", "/admin/import-rules", "ti-route"),
        ]),
        new("configuration", "ti-settings", "Configuration",
        [
            new("Intitulés", "/config/dropdowns", "ti-edit"),
            new("Composants", "/config/components", "ti-cpu"),
        ]),
    ];
}
