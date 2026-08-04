using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Contribue le groupe "Parc" (tout le parc matériel/logiciel), les entrées
/// Recherches sauvegardées/Agents/Déploiements du groupe "Outils", ainsi que l'entrée
/// "Inventaire" du groupe "Administration" — qui relèvent toutes du domaine Inventory
/// (voir Models/GlpiAgent.cs et le protocole GLPI-Agent exposé par ce module).
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
            new("Agents", "/tools/agents", "ti-cpu"),
            new("Déploiements", "/deployments", "ti-rocket"),
        ]),
        new("administration", "ti-shield", "Administration",
        [
            new("Inventaire", "/admin/inventory", "ti-clipboard-list"),
        ]),
    ];
}
