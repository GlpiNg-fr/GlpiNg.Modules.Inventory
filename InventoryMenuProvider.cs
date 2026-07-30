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
            new("Tableau de bord", "/"),
            new("Ordinateurs", "/computers"),
            new("Moniteurs"),
            new("Logiciels"),
            new("Matériels réseau"),
            new("Périphériques"),
            new("Imprimantes"),
            new("Cartouches"),
            new("Consommables"),
            new("Téléphones"),
            new("Baies"),
            new("Châssis"),
            new("PDU"),
            new("Équipements passifs"),
            new("Actifs non gérés"),
            new("Câbles"),
            new("Carte SIM éléments"),
            new("Global"),
        ]),
        new("outils", "ti-briefcase", "Outils",
        [
            new("Agents", "/agents"),
            new("Déploiements", "/deployments"),
        ]),
    ];
}
