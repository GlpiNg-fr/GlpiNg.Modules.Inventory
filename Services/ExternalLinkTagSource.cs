using GlpiNg.Modules.Abstractions.ExternalLinks;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Valeurs des balises de <see cref="ExternalLinkTags"/> pour chaque type d'actif, à passer au
/// panneau « Liens externes » de sa fiche.
///
/// Une surcharge par type plutôt qu'une lecture générique par réflexion : les modèles ne
/// partagent pas d'interface commune, et surtout chaque surcharge dit noir sur blanc ce que ce
/// type-là peut offrir. Une balise absente ici est une balise que l'écran de configuration ne
/// doit pas laisser espérer — voir <c>SupportedTags</c>.
/// </summary>
public static class ExternalLinkTagSource
{
    public const string ComputerItemType = "Computer";
    public const string MonitorItemType = "Monitor";
    public const string PrinterItemType = "Printer";
    public const string NetworkEquipmentItemType = "NetworkEquipment";
    public const string PeripheralItemType = "Peripheral";
    public const string PhoneItemType = "Phone";
    public const string RackItemType = "Rack";
    public const string EnclosureItemType = "Enclosure";
    public const string PduItemType = "Pdu";
    public const string PassiveEquipmentItemType = "PassiveEquipment";
    public const string SimCardItemType = "SimCard";
    public const string CableItemType = "Cable";
    public const string CartridgeItemItemType = "CartridgeItem";
    public const string ConsumableItemItemType = "ConsumableItem";

    /// <summary>
    /// Types proposés dans l'écran de configuration : exactement ceux dont la fiche affiche le
    /// panneau « Liens externes ». Proposer un type de plus reviendrait à laisser configurer un
    /// lien qui ne s'afficherait nulle part — c'est cette liste-ci et les surcharges
    /// <c>For(...)</c> ci-dessous qui doivent rester en phase.
    /// </summary>
    public static readonly IReadOnlyList<(string ItemType, string Label, string Icon)> Catalog =
    [
        (ComputerItemType, "Ordinateur", "ti-device-desktop"),
        (MonitorItemType, "Moniteur", "ti-device-tv"),
        (PrinterItemType, "Imprimante", "ti-printer"),
        (NetworkEquipmentItemType, "Matériel réseau", "ti-router"),
        (PeripheralItemType, "Périphérique", "ti-mouse"),
        (PhoneItemType, "Téléphone", "ti-phone"),
        (RackItemType, "Baie", "ti-server-2"),
        (EnclosureItemType, "Châssis", "ti-layout-grid"),
        (PduItemType, "PDU", "ti-plug"),
        (PassiveEquipmentItemType, "Équipement passif", "ti-plug-connected"),
        (SimCardItemType, "Carte SIM", "ti-sim-card"),
        (CableItemType, "Câble", "ti-cable"),
        (CartridgeItemItemType, "Cartouche", "ti-droplet"),
        (ConsumableItemItemType, "Consommable", "ti-package"),
    ];

    public static string LabelFor(string itemType) =>
        Catalog.FirstOrDefault(entry => entry.ItemType == itemType).Label is { Length: > 0 } label
            ? label
            : itemType;

    public static string IconFor(string itemType) =>
        Catalog.FirstOrDefault(entry => entry.ItemType == itemType).Icon is { Length: > 0 } icon
            ? icon
            : "ti-external-link";

    public static Dictionary<string, string?> For(Computer item) => Build(ComputerItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        model: item.Model,
        manufacturer: item.Manufacturer,
        type: item.ChassisType,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        uuid: item.HardwareUuid,
        operatingSystem: item.OperatingSystem,
        domain: item.Domain,
        // Première adresse remontée par l'inventaire réseau : une fiche en porte souvent
        // plusieurs (Wi-Fi, filaire, virtuelles), et une URL n'en veut qu'une.
        ip: item.NetworkPorts.Select(port => port.IpAddress).FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip)),
        mac: item.NetworkPorts.Select(port => port.MacAddress).FirstOrDefault(mac => !string.IsNullOrWhiteSpace(mac)));

    public static Dictionary<string, string?> For(Printer item) => Build(PrinterItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        uuid: item.Uuid,
        comment: item.Comment,
        ip: item.IpAddress);

    public static Dictionary<string, string?> For(NetworkEquipment item) => Build(NetworkEquipmentItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        uuid: item.Uuid,
        comment: item.Comment);

    public static Dictionary<string, string?> For(Phone item) => Build(PhoneItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        uuid: item.Uuid,
        comment: item.Comment);

    public static Dictionary<string, string?> For(Rack item) => Build(RackItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    public static Dictionary<string, string?> For(Enclosure item) => Build(EnclosureItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    public static Dictionary<string, string?> For(Pdu item) => Build(PduItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    public static Dictionary<string, string?> For(PassiveEquipment item) => Build(PassiveEquipmentItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    public static Dictionary<string, string?> For(Peripheral item) => Build(PeripheralItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        model: item.Model,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name,
        user: item.AssignedUser,
        tech: item.TechnicianInCharge,
        uuid: item.Uuid,
        comment: item.Comment);

    /// <summary>L'opérateur tient lieu de fabricant : c'est lui qui identifie la carte côté métier.</summary>
    public static Dictionary<string, string?> For(SimCard item) => Build(SimCardItemType, item.Id,
        name: item.Name,
        serial: item.SerialNumber,
        otherSerial: item.InventoryNumber,
        type: item.Type,
        manufacturer: item.Operator,
        state: item.StatusItem?.Name,
        location: item.LocationItem?.Name);

    public static Dictionary<string, string?> For(Cable item) => Build(CableItemType, item.Id,
        name: item.Name,
        type: item.Type,
        state: item.StatusItem?.Name,
        comment: item.Comment);

    /// <summary>La référence tient lieu de modèle : c'est la désignation commandable de la cartouche.</summary>
    public static Dictionary<string, string?> For(CartridgeItem item) => Build(CartridgeItemItemType, item.Id,
        name: item.Name,
        type: item.Type,
        model: item.Reference,
        manufacturer: item.Manufacturer,
        location: item.LocationItem?.Name,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    /// <summary>Idem pour un consommable : voir <see cref="For(CartridgeItem)"/>.</summary>
    public static Dictionary<string, string?> For(ConsumableItem item) => Build(ConsumableItemItemType, item.Id,
        name: item.Name,
        type: item.Type,
        model: item.Reference,
        manufacturer: item.Manufacturer,
        location: item.LocationItem?.Name,
        tech: item.TechnicianInCharge,
        comment: item.Comment);

    /// <summary>
    /// Un moniteur n'est pas un actif autonome dans GlpiNg : c'est une ligne
    /// <see cref="ComputerPeripheral"/> rattachée à un poste, d'où le nom porté par
    /// <c>Designation</c> et l'absence de lieu propre.
    /// </summary>
    public static Dictionary<string, string?> For(ComputerPeripheral item) => Build(MonitorItemType, item.Id,
        name: item.Designation,
        serial: item.Serial,
        manufacturer: item.Manufacturer,
        state: item.StatusItem?.Name);

    /// <summary>
    /// Balises réellement fournies pour un type donné, pour que l'écran de configuration
    /// n'affiche pas une aide qui promet plus que ce que la fiche donnera.
    /// </summary>
    public static IReadOnlyList<string> SupportedTags(string itemType) => itemType switch
    {
        ComputerItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.Type, ExternalLinkTags.Model, ExternalLinkTags.Manufacturer, ExternalLinkTags.State,
            ExternalLinkTags.Location, ExternalLinkTags.User, ExternalLinkTags.Uuid,
            ExternalLinkTags.OperatingSystem, ExternalLinkTags.Domain, ExternalLinkTags.Ip, ExternalLinkTags.Mac,
        ],
        PrinterItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.OtherSerial, ExternalLinkTags.Type, ExternalLinkTags.Model,
            ExternalLinkTags.Manufacturer, ExternalLinkTags.State, ExternalLinkTags.Location,
            ExternalLinkTags.User, ExternalLinkTags.Tech, ExternalLinkTags.Uuid, ExternalLinkTags.Comment,
            ExternalLinkTags.Ip,
        ],
        MonitorItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.Manufacturer, ExternalLinkTags.State,
        ],
        CableItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Type,
            ExternalLinkTags.State, ExternalLinkTags.Comment,
        ],
        SimCardItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.OtherSerial, ExternalLinkTags.Type, ExternalLinkTags.Manufacturer,
            ExternalLinkTags.State, ExternalLinkTags.Location,
        ],
        CartridgeItemItemType or ConsumableItemItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Type,
            ExternalLinkTags.Model, ExternalLinkTags.Manufacturer, ExternalLinkTags.Location,
            ExternalLinkTags.Tech, ExternalLinkTags.Comment,
        ],
        PeripheralItemType =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.OtherSerial, ExternalLinkTags.Type, ExternalLinkTags.Model,
            ExternalLinkTags.Manufacturer, ExternalLinkTags.State, ExternalLinkTags.User,
            ExternalLinkTags.Tech, ExternalLinkTags.Uuid, ExternalLinkTags.Comment,
        ],
        // Matériel réseau, téléphone, baie, châssis, PDU, équipement passif : même jeu de champs.
        _ =>
        [
            ExternalLinkTags.Id, ExternalLinkTags.ItemType, ExternalLinkTags.Name, ExternalLinkTags.Serial,
            ExternalLinkTags.OtherSerial, ExternalLinkTags.Type, ExternalLinkTags.Model,
            ExternalLinkTags.Manufacturer, ExternalLinkTags.State, ExternalLinkTags.Location,
            ExternalLinkTags.User, ExternalLinkTags.Tech, ExternalLinkTags.Comment,
        ],
    };

    private static Dictionary<string, string?> Build(string itemType, int id,
        string? name = null, string? serial = null, string? otherSerial = null, string? type = null,
        string? model = null, string? manufacturer = null, string? state = null, string? location = null,
        string? user = null, string? tech = null, string? uuid = null, string? comment = null,
        string? ip = null, string? mac = null, string? operatingSystem = null, string? domain = null) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ExternalLinkTags.Id] = id.ToString(),
            [ExternalLinkTags.ItemType] = itemType,
            [ExternalLinkTags.Name] = name,
            [ExternalLinkTags.Serial] = serial,
            [ExternalLinkTags.OtherSerial] = otherSerial,
            [ExternalLinkTags.Type] = type,
            [ExternalLinkTags.Model] = model,
            [ExternalLinkTags.Manufacturer] = manufacturer,
            [ExternalLinkTags.State] = state,
            [ExternalLinkTags.Location] = location,
            [ExternalLinkTags.User] = user,
            [ExternalLinkTags.Tech] = tech,
            [ExternalLinkTags.Uuid] = uuid,
            [ExternalLinkTags.Comment] = comment,
            [ExternalLinkTags.Ip] = ip,
            [ExternalLinkTags.Mac] = mac,
            [ExternalLinkTags.OperatingSystem] = operatingSystem,
            [ExternalLinkTags.Domain] = domain,
        };
}
