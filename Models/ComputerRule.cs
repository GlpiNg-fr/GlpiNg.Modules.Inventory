using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>Opérateur logique combinant tous les critères d'une règle (même principe que DictionaryRuleLogicalOperator).</summary>
public enum ComputerRuleLogicalOperator
{
    And,
    Or
}

/// <summary>À quel moment une règle s'applique, par rapport au cycle de vie de l'ordinateur.</summary>
[Flags]
public enum ComputerRuleAppliesTo
{
    OnCreate = 1,
    OnUpdate = 2,
    OnCreateAndUpdate = OnCreate | OnUpdate
}

/// <summary>
/// Règle métier pour les actifs (ordinateurs) : modifie les champs d'un ordinateur en
/// fonction de ses propres données, à sa création et/ou sa mise à jour par l'inventaire
/// (voir ComputerRuleEngine, appliqué depuis InventoryImportService). Équivalent de
/// "RuleAsset" dans GLPI.
///
/// Contrairement à <see cref="DictionaryRule"/> (une seule action, un seul champ
/// inspecté), une règle porte ici plusieurs critères ET plusieurs actions hétérogènes,
/// comme le moteur de règles générique de GLPI (onglets Règle/Critères/Actions) — c'est
/// précisément le cas que DictionaryRule laisse volontairement de côté (voir sa doc).
/// </summary>
public class ComputerRule : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Ordre d'exécution parmi les autres règles (les plus petits d'abord) — même principe que DictionaryRule.SortOrder.</summary>
    public int SortOrder { get; set; }

    public ComputerRuleLogicalOperator LogicalOperator { get; set; } = ComputerRuleLogicalOperator.And;
    public ComputerRuleAppliesTo AppliesTo { get; set; } = ComputerRuleAppliesTo.OnCreateAndUpdate;

    public List<ComputerRuleCriterion> Criteria { get; set; } = [];
    public List<ComputerRuleAction> Actions { get; set; } = [];
}

/// <summary>Opérateurs de critère — mêmes intitulés que DictionaryCriterionOperator pour rester cohérent.</summary>
public enum ComputerRuleCriterionOperator
{
    Is,
    IsNot,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    MatchesRegex,
    Exists,
    DoesNotExist
}

/// <summary>Un critère de règle : teste la valeur d'un champ de Computer (désigné par sa clé dans ComputerRuleFieldCatalog).</summary>
public class ComputerRuleCriterion
{
    public int Id { get; set; }
    public int ComputerRuleId { get; set; }

    public required string Field { get; set; }
    public ComputerRuleCriterionOperator Operator { get; set; } = ComputerRuleCriterionOperator.Contains;

    /// <summary>Valeur ou expression régulière comparée au champ. Inutilisé pour Exists/DoesNotExist.</summary>
    public string? Value { get; set; }
}

public enum ComputerRuleActionType
{
    /// <summary>Affecte directement la valeur au champ.</summary>
    Assign,

    /// <summary>Ajoute la valeur à la fin du contenu actuel du champ.</summary>
    Append,

    /// <summary>
    /// Affecte le résultat d'une expression régulière : la valeur peut référencer les groupes
    /// capturés (<c>#0</c>, <c>#1</c>...) par le dernier critère MatchesRegex évalué à vrai
    /// pour cette règle — même convention que DictionaryRuleEngine.
    /// </summary>
    RegexResult
}

/// <summary>Une action de règle : modifie un champ de Computer (désigné par sa clé dans ComputerRuleFieldCatalog) quand la règle correspond.</summary>
public class ComputerRuleAction
{
    public int Id { get; set; }
    public int ComputerRuleId { get; set; }

    public required string Field { get; set; }
    public ComputerRuleActionType ActionType { get; set; } = ComputerRuleActionType.Assign;
    public string? Value { get; set; }
}
