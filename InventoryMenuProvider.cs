using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Contribue le groupe "Parc" (tout le parc matériel/logiciel) ainsi que les entrées
/// Agents/Déploiements du groupe "Outils", qui relèvent toutes du domaine Inventory
/// (voir Models/GlpiAgent.cs et le protocole GLPI-Agent exposé par ce module).
/// </summary>
public sealed class InventoryMenuProvider : IMenuProvider
{
    public IReadOnlyList<MenuGroup> GetMenuGroups() =>
    [
        new("parc", "ti-box", "Parc",
        [
            new("Tableau de bord", "/", "ti-layout-dashboard"),
            new("Ordinateurs", "/computers", "ti-device-desktop"),
            new("Moniteurs", "/monitors", "ti-device-tv"),
            new("Logiciels", "/software", "ti-apps"),
            new("Matériels réseau", Icon: "ti-router"),
            new("Périphériques", Icon: "ti-mouse"),
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
            new("Agents", "/agents", "ti-cpu"),
            new("Déploiements", "/deployments", "ti-rocket"),
        ]),
    ];
}
