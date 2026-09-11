using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Modèle de cartouche du parc ("Cartouches" dans GLPI, glpi_cartridgeitems) : contrairement aux
/// types de la Phase A (Imprimante, Téléphone, ...), ce n'est pas un actif avec numéro de série
/// mais un modèle de consommable suivi en stock — chaque exemplaire physique reçu est une
/// <see cref="Cartridge"/> individuelle rattachée à ce modèle (voir sa doc), avec son propre cycle
/// de vie (réceptionnée / mise en service sur une imprimante / retirée).
/// </summary>
public class CartridgeItem : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Type de cartouche (ex: "Toner noir", "Toner couleur"), texte libre.</summary>
    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Reference { get; set; }

    public int? LocationId { get; set; }
    public DropdownItem? LocationItem { get; set; }

    public string? TechnicianInCharge { get; set; }

    /// <summary>Seuil d'alerte ("Seuil d'alerte" dans GLPI) : nombre d'unités en stock en-dessous duquel un réapprovisionnement est signalé (voir Index.razor, colonne "Stock").</summary>
    public int AlertThreshold { get; set; } = 10;

    /// <summary>
    /// OID SNMP auquel une imprimante expose le niveau restant de cette cartouche, pour que le
    /// serveur puisse l'interroger plutôt que d'attendre une saisie.
    ///
    /// La norme range ces niveaux sous <c>prtMarkerSuppliesLevel</c>
    /// (<c>1.3.6.1.2.1.43.11.1.1.9.1.<i>n</i></c>), où <i>n</i> est le rang du consommable dans
    /// l'imprimante : c'est ce rang qui change d'un modèle à l'autre, d'où un OID par référence de
    /// cartouche plutôt qu'un seul pour tout le parc. Vide, le niveau n'est pas relevé.
    /// </summary>
    public string? SnmpLevelOid { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<Cartridge> Cartridges { get; set; } = [];
    public List<CartridgeItemHistoryEntry> HistoryEntries { get; set; } = [];
}

/// <summary>
/// Exemplaire individuel d'un <see cref="CartridgeItem"/> : suit son cycle de vie réel (réceptionné,
/// mis en service sur une imprimante, retiré/jeté), comme glpi_cartridges dans GLPI. Le stock
/// disponible d'un modèle = le nombre de <see cref="Cartridge"/> où <see cref="DateUse"/> et
/// <see cref="DateOut"/> sont tous deux nuls (voir CartridgeItems/Detail.razor.cs, AvailableStock).
/// </summary>
public class Cartridge
{
    public int Id { get; set; }
    public int CartridgeItemId { get; set; }

    /// <summary>Référence dont cette unité est un exemplaire. Vue depuis l'imprimante, c'est elle
    /// qui nomme la cartouche : l'unité n'a qu'un numéro.</summary>
    public CartridgeItem? CartridgeItem { get; set; }

    /// <summary>Imprimante dans laquelle cette cartouche est installée, une fois mise en service (voir DateUse).</summary>
    public int? PrinterId { get; set; }
    public Printer? Printer { get; set; }

    /// <summary>Date de réception en stock.</summary>
    public DateTime DateIn { get; set; } = DateTime.UtcNow;

    /// <summary>Date de mise en service (installation dans une imprimante) — null tant qu'en stock.</summary>
    public DateTime? DateUse { get; set; }

    /// <summary>Date de retrait/mise au rebut — null tant qu'en stock ou en service.</summary>
    public DateTime? DateOut { get; set; }

    /// <summary>
    /// Niveau restant en pourcentage, relevé par SNMP sur l'imprimante (voir
    /// PrinterSnmpPollCronTask et CartridgeItem.SnmpLevelOid). Null tant qu'aucun relevé n'a
    /// abouti — ce qui est le cas d'une cartouche en stock, qui n'est dans aucune imprimante.
    /// </summary>
    public int? LevelPercent { get; set; }

    /// <summary>Date du dernier relevé. Distincte du niveau : un niveau ancien doit pouvoir être
    /// reconnu comme tel plutôt que passer pour une mesure fraîche.</summary>
    public DateTime? LevelReadAt { get; set; }
}

/// <summary>Ligne du journal de modifications d'un modèle de cartouche (onglet "Historique" de la fiche).</summary>
public class CartridgeItemHistoryEntry
{
    public int Id { get; set; }
    public int CartridgeItemId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public required string User { get; set; }
    public required string Field { get; set; }
    public required string Description { get; set; }
}
