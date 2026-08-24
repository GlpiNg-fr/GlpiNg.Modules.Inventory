namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Informations d'un inventaire GLPI-Agent disponibles pour évaluer les
/// <see cref="Models.ImportAssignmentRule"/>, construites AVANT toute résolution/création du
/// Computer (voir InventoryImportService.ImportAsync) — directement depuis le contenu brut de
/// la requête et l'agent, pour pouvoir refuser l'import sans avoir touché la base.
///
/// Sous-ensemble volontaire des critères de RuleImportEntity dans GLPI : "Source" (toujours
/// "GLPI-Agent" ici, un seul protocole supporté), "Sous-réseau" (calcul CIDR non implémenté) et
/// "Type d'élément" (toujours "Computer", aucun autre type inventorié par ce protocole) sont
/// des constantes dans GlpiNg et n'apportent rien comme critère — ils sont donc omis plutôt que
/// simulés.
/// </summary>
public sealed record ImportAssignmentRuleContext(
    string? ComputerName,
    string? SerialNumber,
    string? Domain,
    string? Tag,
    IReadOnlyList<string?> IpAddresses);

public sealed class ImportAssignmentRuleFieldDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// Une ou plusieurs valeurs à tester (plusieurs uniquement pour "Adresse IP", une par
    /// interface réseau) — voir ImportAssignmentRuleEngine pour la sémantique "au moins une"
    /// (opérateurs positifs) / "toutes" (opérateurs de négation) qui en découle.
    /// </summary>
    public required Func<ImportAssignmentRuleContext, IReadOnlyList<string?>> GetValues { get; init; }
}

/// <summary>
/// Catalogue des champs proposés par l'éditeur de règles d'affectation à l'import (critères) —
/// ajouter un champ ici suffit à le rendre disponible dans l'UI et dans ImportAssignmentRuleEngine.
/// </summary>
public static class ImportAssignmentRuleFieldCatalog
{
    public static readonly IReadOnlyList<ImportAssignmentRuleFieldDefinition> All =
    [
        new() { Key = "ComputerName", Label = "Nom de l'équipement", GetValues = c => [c.ComputerName] },
        new() { Key = "SerialNumber", Label = "Numéro de série", GetValues = c => [c.SerialNumber] },
        new() { Key = "Domain", Label = "Domaine", GetValues = c => [c.Domain] },
        new() { Key = "Tag", Label = "Tag d'inventaire", GetValues = c => [c.Tag] },
        new() { Key = "IpAddress", Label = "Adresse IP", GetValues = c => c.IpAddresses },
    ];

    public static readonly IReadOnlyDictionary<string, ImportAssignmentRuleFieldDefinition> ByKey = All.ToDictionary(f => f.Key);

    public static string Label(string fieldKey) => ByKey.TryGetValue(fieldKey, out ImportAssignmentRuleFieldDefinition? field) ? field.Label : fieldKey;
}
