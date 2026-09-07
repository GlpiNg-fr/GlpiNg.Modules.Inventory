using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Catégorie de dictionnaire GLPI (front/ruledictionnary*.php) : chaque type normalise
/// une seule valeur texte remontée par l'inventaire avant son enregistrement. Sous-ensemble
/// des dictionnaires réels de GLPI, restreint aux champs effectivement portés par Computer/
/// ComputerSoftware dans GlpiNg (pas de modèles de moniteur/imprimante/périphérique réseau,
/// pas de service pack/architecture/édition d'OS : ces champs n'existent pas ici).
/// </summary>
public enum DictionaryRuleType
{
    Manufacturer,
    ComputerModel,
    OperatingSystem,
    OperatingSystemVersion,
    Software
}

/// <summary>Champ inspecté par un critère. Publisher n'a de sens que pour DictionaryRuleType.Software (nom + fabricant du logiciel).</summary>
public enum DictionaryCriterionField
{
    Name,
    Publisher
}

/// <summary>Opérateurs de critère, alignés sur ceux du dictionnaire GLPI réel (est/n'est pas/contient/.../expression rationnelle/existe).</summary>
public enum DictionaryCriterionOperator
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

/// <summary>Opérateur logique combinant tous les critères d'une règle (champ "Opérateur logique" unique de GLPI, pas un lien par critère).</summary>
public enum DictionaryRuleLogicalOperator
{
    And,
    Or
}

public enum DictionaryActionType
{
    /// <summary>Remplace la valeur par ActionValue (qui peut référencer #0, #1... les groupes capturés par un critère MatchesRegex — voir DictionaryRuleEngine).</summary>
    Assign,

    /// <summary>
    /// Pour Software : exclut ce logiciel de l'inventaire importé. Pour les autres types :
    /// conserve la valeur déjà enregistrée sur l'ordinateur plutôt que de l'écraser par la
    /// valeur brute remontée par l'agent (contrairement à GLPI, qui peut rejeter l'import
    /// entier de l'actif — simplification délibérée, cohérente avec le sous-ensemble de
    /// champs couverts par ce dictionnaire).
    /// </summary>
    Ignore
}

/// <summary>
/// Règle de dictionnaire : normalise ou ignore une valeur brute d'inventaire (fabricant,
/// modèle, OS, version d'OS, logiciel) avant son enregistrement. Appliquée par
/// DictionaryRuleEngine, sur le modèle du couple DeployComputerGroup/DeployComputerGroupCriterion
/// (GlpiNg.Modules.Deployment) : une règle porte plusieurs critères mais une seule action,
/// alors que le moteur de règles générique de GLPI autorise plusieurs actions hétérogènes
/// par règle au travers de trois onglets (Règle/Critères/Actions) — ici, l'action est un
/// simple champ de la règle elle-même (onglet "Règle" et "Critères" seulement côté UI).
/// </summary>
public class DictionaryRule : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public DictionaryRuleType Type { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Position d'évaluation parmi les règles actives du même Type : la première règle dont les critères correspondent l'emporte (le moteur s'arrête là).</summary>
    public int SortOrder { get; set; }

    public DictionaryRuleLogicalOperator LogicalOperator { get; set; } = DictionaryRuleLogicalOperator.And;

    public DictionaryActionType ActionType { get; set; } = DictionaryActionType.Assign;
    public string? ActionValue { get; set; }

    public List<DictionaryRuleCriterion> Criteria { get; set; } = [];
}

public class DictionaryRuleCriterion
{
    public int Id { get; set; }
    public int DictionaryRuleId { get; set; }
    public DictionaryCriterionField Field { get; set; } = DictionaryCriterionField.Name;
    public DictionaryCriterionOperator Operator { get; set; } = DictionaryCriterionOperator.Contains;
    public string? Value { get; set; }
}
