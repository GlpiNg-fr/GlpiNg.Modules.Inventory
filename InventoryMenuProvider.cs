using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Contribue le groupe "Parc" (tout le parc matériel/logiciel), l'entrée "Recherches
/// sauvegardées" du groupe "Outils", les entrées "Inventaire"/"Dictionnaires"/"Import GLPI"/"Règles" du
/// groupe "Administration", ainsi que les entrées "Intitulés"/"Composants" du groupe "Configuration" —
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
            new("Matériels réseau", Icon: "ti-router"),
            new("Périphériques", "/parc/peripherals", "ti-mouse"),
            new("Imprimantes", Icon: "ti-printer"),
            new("Cartouches", Icon: "ti-droplet"),
            new("Consommables", Icon: "ti-package"),
            new("Téléphones", Icon: "ti-phone"),
            new("Baies", Icon: "ti-server-2"),
            new("Châssis", Icon: "ti-layout-grid"),
            new("PDU", Icon: "ti-plug"),
            new("Équipements passifs", Icon: "ti-plug-connected"),
            new("Actifs non gérés", Icon: "ti-help"),
            new("Câbles", Icon: "ti-cable"),
            new("Carte SIM éléments", Icon: "ti-sim-card"),
            new("Global", Icon: "ti-world"),
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
        ]),
        new("configuration", "ti-settings", "Configuration",
        [
            new("Intitulés", "/config/dropdowns", "ti-edit"),
            new("Composants", "/config/components", "ti-cpu"),
        ]),
    ];
}
