using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Décrit un champ texte de <see cref="Computer"/> exploitable par <see cref="ComputerRuleEngine"/>,
/// à la fois pour évaluer les critères (<see cref="GetValue"/>) et pour appliquer les actions
/// (<see cref="SetValue"/>) d'une <see cref="ComputerRule"/>.
/// </summary>
public enum ComputerRuleFieldKind
{
    /// <summary>Champ texte : opérateurs de comparaison de chaînes.</summary>
    Text,

    /// <summary>Champ date : opérateurs d'ancienneté (OlderThanHours / WithinLastHours).</summary>
    Date,

    /// <summary>
    /// Statut, résolu vers un intitulé. Lisible comme un texte, mais son affectation passe par un
    /// résolveur fourni par l'appelant — voir <see cref="ComputerRuleEngine.Apply"/>.
    /// </summary>
    Status
}

public sealed class ComputerRuleFieldDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required Func<Computer, string?> GetValue { get; init; }
    public required Action<Computer, string?> SetValue { get; init; }

    public ComputerRuleFieldKind Kind { get; init; } = ComputerRuleFieldKind.Text;

    /// <summary>Renseigné pour <see cref="ComputerRuleFieldKind.Date"/> uniquement.</summary>
    public Func<Computer, DateTime?>? GetDate { get; init; }

    /// <summary>Vrai si le champ ne peut servir qu'en critère, jamais en action.</summary>
    public bool IsReadOnly { get; init; }
}

/// <summary>
/// Catalogue des champs de <see cref="Computer"/> proposés par l'éditeur de règles métier
/// pour les actifs (critères et actions) — ajouter un champ ici suffit à le rendre
/// disponible dans l'UI et dans <see cref="ComputerRuleEngine"/>.
///
/// Trois natures de champ (voir <see cref="ComputerRuleFieldKind"/>) : texte, date, et statut.
/// Le statut est résolu vers un intitulé (DropdownItem), ce qu'un délégué synchrone ne sait pas
/// faire — l'affectation passe donc par un résolveur que l'appelant fournit au moteur, après avoir
/// pré-résolu les noms de statut utilisés par les règles actives. Les autres champs d'intitulé
/// (Lieu) restent hors catalogue tant que le besoin ne se présente pas.
/// </summary>
public static class ComputerRuleFieldCatalog
{
    public static readonly IReadOnlyList<ComputerRuleFieldDefinition> All =
    [
        new() { Key = "Name", Label = "Nom", GetValue = c => c.Name, SetValue = (c, v) => c.Name = string.IsNullOrEmpty(v) ? c.Name : v },
        new() { Key = "SerialNumber", Label = "Numéro de série", GetValue = c => c.SerialNumber, SetValue = (c, v) => c.SerialNumber = v },
        new() { Key = "Manufacturer", Label = "Fabricant", GetValue = c => c.Manufacturer, SetValue = (c, v) => c.Manufacturer = v },
        new() { Key = "Model", Label = "Modèle", GetValue = c => c.Model, SetValue = (c, v) => c.Model = v },
        new() { Key = "OperatingSystem", Label = "Système d'exploitation", GetValue = c => c.OperatingSystem, SetValue = (c, v) => c.OperatingSystem = v },
        new() { Key = "OsVersion", Label = "Version de l'OS", GetValue = c => c.OsVersion, SetValue = (c, v) => c.OsVersion = v },
        new() { Key = "OsKernelVersion", Label = "Version du noyau", GetValue = c => c.OsKernelVersion, SetValue = (c, v) => c.OsKernelVersion = v },
        new() { Key = "ChassisType", Label = "Type de châssis", GetValue = c => c.ChassisType, SetValue = (c, v) => c.ChassisType = v },
        new() { Key = "HardwareUuid", Label = "UUID matériel", GetValue = c => c.HardwareUuid, SetValue = (c, v) => c.HardwareUuid = v },
        new() { Key = "Domain", Label = "Domaine", GetValue = c => c.Domain, SetValue = (c, v) => c.Domain = v },
        new() { Key = "VmSystem", Label = "Virtualisation", GetValue = c => c.VmSystem, SetValue = (c, v) => c.VmSystem = v },
        new() { Key = "LastLoggedUser", Label = "Dernier utilisateur connecté", GetValue = c => c.LastLoggedUser, SetValue = (c, v) => c.LastLoggedUser = v },
        new() { Key = "Site", Label = "Site", GetValue = c => c.Site, SetValue = (c, v) => c.Site = v },
        new() { Key = "Building", Label = "Bâtiment", GetValue = c => c.Building, SetValue = (c, v) => c.Building = v },
        new() { Key = "Room", Label = "Salle", GetValue = c => c.Room, SetValue = (c, v) => c.Room = v },
        new() { Key = "AssignedUser", Label = "Utilisateur affecté", GetValue = c => c.AssignedUser, SetValue = (c, v) => c.AssignedUser = v },

        // Date du dernier inventaire remonté par l'agent : c'est le « dernier contact » d'un poste.
        // Critère seulement — une règle n'a pas à réécrire une date constatée.
        new()
        {
            Key = "LastInventoryAt",
            Label = "Dernier inventaire",
            Kind = ComputerRuleFieldKind.Date,
            IsReadOnly = true,
            GetDate = c => c.LastInventoryAt,
            GetValue = c => c.LastInventoryAt?.ToString("O"),
            SetValue = (_, _) => { },
        },

        // Statut : lisible comme un texte (le nom de l'intitulé), mais son affectation demande de
        // résoudre ce nom vers un DropdownItem, ce qu'un délégué synchrone ne peut pas faire. Le
        // moteur passe donc par un résolveur fourni par l'appelant — d'où SetValue sans effet ici.
        new()
        {
            Key = "Status",
            Label = "Statut",
            Kind = ComputerRuleFieldKind.Status,
            GetValue = c => c.StatusItem?.Name,
            SetValue = (_, _) => { },
        },
    ];

    public static readonly IReadOnlyDictionary<string, ComputerRuleFieldDefinition> ByKey = All.ToDictionary(f => f.Key);

    public static string Label(string fieldKey) => ByKey.TryGetValue(fieldKey, out ComputerRuleFieldDefinition? field) ? field.Label : fieldKey;
}
