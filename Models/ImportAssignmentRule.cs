using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Opérateur logique combinant tous les critères d'une règle (même principe que ComputerRuleLogicalOperator).</summary>
public enum ImportAssignmentRuleLogicalOperator
{
    And,
    Or
}

/// <summary>
/// Règle d'affectation à l'import : évaluée à chaque inventaire GLPI-Agent reçu, sur des
/// informations connues avant même la résolution de l'ordinateur (nom, série, domaine, tag
/// d'agent, adresses IP des interfaces réseau) — voir ImportAssignmentRuleEngine, appliqué
/// depuis InventoryImportService. Équivalent de "RuleImportEntity" dans GLPI (onglet
/// Administration > GLPI Inventory > Règles > "Règles sur l'entité ordinateur").
///
/// Contrairement à <see cref="ComputerRule"/> (règles métier sur les champs d'un Computer déjà
/// résolu, TOUTES les règles actives correspondantes s'appliquent), le moteur ici s'arrête à la
/// première règle vérifiée — comme dans GLPI ("Le moteur s'arrête à la première règle vérifiée").
///
/// Adapté à ce que GlpiNg modélise réellement : pas de notion d'Entité (multi-tenant) ni de
/// Groupe responsable sur Computer, donc pas d'actions "Affecter à une entité"/"Sous-entités"/
/// "Groupe responsable" comme dans GLPI — seuls le Lieu (DropdownItem existant), le technicien
/// responsable (Computer.AssignedUser, déjà un champ texte libre) et le refus d'import sont
/// proposés.
/// </summary>
public class ImportAssignmentRule : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Ordre d'évaluation parmi les autres règles (les plus petits d'abord) — la première règle vérifiée l'emporte.</summary>
    public int SortOrder { get; set; }

    public ImportAssignmentRuleLogicalOperator LogicalOperator { get; set; } = ImportAssignmentRuleLogicalOperator.And;

    public List<ImportAssignmentRuleCriterion> Criteria { get; set; } = [];
    public List<ImportAssignmentRuleAction> Actions { get; set; } = [];
}

/// <summary>Opérateurs de critère — même liste que sur l'instance GLPI réelle (RuleImportEntity), y compris la négation de l'expression régulière absente de ComputerRuleCriterionOperator.</summary>
public enum ImportAssignmentRuleCriterionOperator
{
    Is,
    IsNot,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    MatchesRegex,
    NotMatchesRegex,
    Exists,
    DoesNotExist
}

/// <summary>
/// Un critère de règle : teste une information de l'inventaire en cours (désignée par sa clé
/// dans ImportAssignmentRuleFieldCatalog) — voir ImportAssignmentRuleContext pour les valeurs
/// disponibles. Le champ "Adresse IP" est multi-valué (une par interface réseau) : un critère le
/// portant est vérifié si au moins une interface correspond (toutes pour un opérateur de négation).
/// </summary>
public class ImportAssignmentRuleCriterion
{
    public int Id { get; set; }
    public int ImportAssignmentRuleId { get; set; }

    public required string Field { get; set; }
    public ImportAssignmentRuleCriterionOperator Operator { get; set; } = ImportAssignmentRuleCriterionOperator.Contains;

    /// <summary>Valeur ou expression régulière comparée au champ. Inutilisé pour Exists/DoesNotExist.</summary>
    public string? Value { get; set; }
}

public enum ImportAssignmentRuleActionType
{
    /// <summary>Affecte le Lieu (voir Models/DropdownItem.cs, DropdownType.Location) portant ce nom — créé à la volée s'il n'existe pas encore, comme InventoryImportService.ResolveStatusIdAsync pour les statuts.</summary>
    AssignLocation,

    /// <summary>Affecte Computer.AssignedUser (technicien/utilisateur responsable) à cette valeur.</summary>
    AssignTechnician,

    /// <summary>Rejette l'inventaire : aucun Computer n'est créé ni mis à jour, un RefusedImportLog est journalisé à la place — voir Models/RefusedImportLog.cs.</summary>
    RefuseImport
}

/// <summary>Une action de règle, déclenchée quand la règle correspond. RefuseImport n'utilise pas Value.</summary>
public class ImportAssignmentRuleAction
{
    public int Id { get; set; }
    public int ImportAssignmentRuleId { get; set; }

    public ImportAssignmentRuleActionType ActionType { get; set; } = ImportAssignmentRuleActionType.AssignLocation;
    public string? Value { get; set; }
}
