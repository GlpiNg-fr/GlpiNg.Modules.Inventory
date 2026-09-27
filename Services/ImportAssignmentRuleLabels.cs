using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Libellés partagés entre la liste et la fiche de règle d'affectation à l'import (mêmes intitulés que sur l'instance GLPI réelle), pour rester cohérent entre les deux — même principe que ComputerRuleLabels.</summary>
public static class ImportAssignmentRuleLabels
{
    public static bool IsValuelessOperator(ImportAssignmentRuleCriterionOperator op) =>
        op is ImportAssignmentRuleCriterionOperator.Exists or ImportAssignmentRuleCriterionOperator.DoesNotExist;

    public static bool ActionHasValue(ImportAssignmentRuleActionType type) => type != ImportAssignmentRuleActionType.RefuseImport;

    public static string OperatorLabel(ImportAssignmentRuleCriterionOperator op) => op switch
    {
        ImportAssignmentRuleCriterionOperator.Is => "est",
        ImportAssignmentRuleCriterionOperator.IsNot => Tr.T("n'est pas"),
        ImportAssignmentRuleCriterionOperator.Contains => "contient",
        ImportAssignmentRuleCriterionOperator.NotContains => Tr.T("ne contient pas"),
        ImportAssignmentRuleCriterionOperator.StartsWith => Tr.T("commence par"),
        ImportAssignmentRuleCriterionOperator.EndsWith => Tr.T("finit par"),
        ImportAssignmentRuleCriterionOperator.MatchesRegex => "expression rationnelle vérifie",
        ImportAssignmentRuleCriterionOperator.NotMatchesRegex => "expression rationnelle ne vérifie pas",
        ImportAssignmentRuleCriterionOperator.Exists => "existe",
        ImportAssignmentRuleCriterionOperator.DoesNotExist => Tr.T("n'existe pas"),
        _ => op.ToString()
    };

    public static string ActionTypeLabel(ImportAssignmentRuleActionType type) => type switch
    {
        ImportAssignmentRuleActionType.AssignLocation => Tr.T("Affecter un lieu"),
        ImportAssignmentRuleActionType.AssignTechnician => Tr.T("Affecter un technicien responsable"),
        ImportAssignmentRuleActionType.RefuseImport => Tr.T("Refuser l'import"),
        _ => type.ToString()
    };

    /// <summary>Résumé d'un critère sur une ligne (ex. "Domaine contient CORP").</summary>
    public static string CriterionSummary(ImportAssignmentRuleCriterion criterion) => IsValuelessOperator(criterion.Operator)
        ? $"{ImportAssignmentRuleFieldCatalog.Label(criterion.Field)} {OperatorLabel(criterion.Operator)}"
        : $"{ImportAssignmentRuleFieldCatalog.Label(criterion.Field)} {OperatorLabel(criterion.Operator)} {criterion.Value}";

    /// <summary>Résumé d'une action sur une ligne (ex. "Affecter un lieu : Siège social").</summary>
    public static string ActionSummary(ImportAssignmentRuleAction action) => ActionHasValue(action.ActionType)
        ? $"{ActionTypeLabel(action.ActionType)} : {action.Value}"
        : ActionTypeLabel(action.ActionType);
}
