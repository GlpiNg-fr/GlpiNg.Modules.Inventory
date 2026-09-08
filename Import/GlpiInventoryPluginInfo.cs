namespace GlpiNg.Modules.Inventory.Import;

/// <summary>
/// Ce qui a été trouvé du plugin d'inventaire de la base GLPI source — GLPI Inventory
/// (<c>glpiinventory</c>) ou son ancêtre FusionInventory (<c>fusioninventory</c>), dont il est un
/// fork avec renommage des tables.
///
/// Le plugin est détecté parce que c'est lui qui porte, côté GLPI, le domaine que GlpiNg
/// réimplémente en propre : déploiement de paquets, tâches, agents, découverte réseau SNMP. Savoir
/// s'il est présent — et ce qu'il contient — dit à l'administrateur ce qu'un import ne reprendra
/// pas, plutôt que de le lui laisser découvrir après coup.
/// </summary>
public class GlpiInventoryPluginInfo
{
    /// <summary>Vrai si le plugin est déclaré dans <c>glpi_plugins</c> ou si ses tables sont présentes.</summary>
    public bool IsPresent { get; set; }

    /// <summary>Répertoire du plugin : <c>glpiinventory</c> ou <c>fusioninventory</c>. Null si détecté par ses seules tables.</summary>
    public string? Directory { get; set; }

    public string? Name { get; set; }

    public string? Version { get; set; }

    /// <summary>
    /// Colonne <c>state</c> de <c>glpi_plugins</c>. 1 = activé côté GLPI ; les autres valeurs
    /// correspondent à un plugin installé mais désactivé, à configurer, ou en cours de nettoyage.
    /// </summary>
    public int? State { get; set; }

    /// <summary>Vrai quand <see cref="State"/> vaut 1. Un plugin désactivé laisse ses tables et ses données en place.</summary>
    public bool IsActive => State == 1;

    /// <summary>Préfixe réellement observé sur les tables (<c>glpi_plugin_glpiinventory_</c> ou <c>..._fusioninventory_</c>).</summary>
    public string? TablePrefix { get; set; }

    /// <summary>Nombre de tables du plugin trouvées dans le schéma.</summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Vrai quand le plugin est déclaré dans <c>glpi_plugins</c> mais que plus aucune de ses tables
    /// n'existe — plugin désinstallé proprement, ou dump partiel. Les compteurs sont alors tous à
    /// zéro et il n'y a rien à reprendre.
    /// </summary>
    public bool IsRegisteredWithoutTables => IsPresent && TableCount == 0;

    // Comptes des seules données qui ont un équivalent dans GlpiNg. Chacun retombe à zéro si la
    // table correspondante n'existe pas sur cette version du plugin — les noms ont bougé entre
    // FusionInventory et GLPI Inventory, et d'une version à l'autre.
    public int DeployPackagesCount { get; set; }
    public int TasksCount { get; set; }
    public int AgentsCount { get; set; }
    public int IpRangesCount { get; set; }
    public int SnmpCredentialsCount { get; set; }
    public int UnmanagedDevicesCount { get; set; }

    /// <summary>Total des données reprenables, pour n'afficher le détail que s'il y a quelque chose à dire.</summary>
    public int DataCount =>
        DeployPackagesCount + TasksCount + AgentsCount + IpRangesCount + SnmpCredentialsCount + UnmanagedDevicesCount;
}
