using GlpiNg.Modules.Abstractions.Localization;
﻿using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Libellés partagés entre la liste et la fiche de règle (ComputerRuleList/ComputerRuleDetail), pour rester cohérent entre les deux.</summary>
public static class ComputerRuleLabels
{
    public static bool IsValuelessOperator(ComputerRuleCriterionOperator op) =>
        op is ComputerRuleCriterionOperator.Exists or ComputerRuleCriterionOperator.DoesNotExist;

    /// <summary>Opérateurs d'ancienneté : la valeur saisie est un nombre d'heures, pas un texte.</summary>
    public static bool IsDurationOperator(ComputerRuleCriterionOperator op) =>
        op is ComputerRuleCriterionOperator.OlderThanHours or ComputerRuleCriterionOperator.WithinLastHours;

    /// <summary>
    /// Opérateurs proposés pour un champ donné : comparer une date avec « contient » n'aurait pas
    /// de sens, et proposer « remonte à plus de » sur un champ texte non plus.
    /// </summary>
    public static IEnumerable<ComputerRuleCriterionOperator> OperatorsFor(string fieldKey)
    {
        bool isDate = ComputerRuleFieldCatalog.ByKey.TryGetValue(fieldKey, out ComputerRuleFieldDefinition? field)
                      && field.Kind == ComputerRuleFieldKind.Date;

        return isDate
            ?
            [
                ComputerRuleCriterionOperator.OlderThanHours,
                ComputerRuleCriterionOperator.WithinLastHours,
                ComputerRuleCriterionOperator.Exists,
                ComputerRuleCriterionOperator.DoesNotExist,
            ]
            : Enum.GetValues<ComputerRuleCriterionOperator>().Where(op => !IsDurationOperator(op));
    }

    public static string OperatorLabel(ComputerRuleCriterionOperator op) => op switch
    {
        ComputerRuleCriterionOperator.Is => "est",
        ComputerRuleCriterionOperator.IsNot => Tr.T("n'est pas"),
        ComputerRuleCriterionOperator.Contains => "contient",
        ComputerRuleCriterionOperator.NotContains => Tr.T("ne contient pas"),
        ComputerRuleCriterionOperator.StartsWith => Tr.T("commence par"),
        ComputerRuleCriterionOperator.EndsWith => Tr.T("finit par"),
        ComputerRuleCriterionOperator.MatchesRegex => "expression régulière vérifie",
        ComputerRuleCriterionOperator.Exists => "existe",
        ComputerRuleCriterionOperator.DoesNotExist => Tr.T("n'existe pas"),
        ComputerRuleCriterionOperator.OlderThanHours => Tr.T("remonte à plus de (heures)"),
        ComputerRuleCriterionOperator.WithinLastHours => Tr.T("remonte à moins de (heures)"),
        _ => op.ToString()
    };

    public static string ActionTypeLabel(ComputerRuleActionType type) => type switch
    {
        ComputerRuleActionType.Assign => Tr.T("Affecter"),
        ComputerRuleActionType.Append => Tr.T("Ajouter à la fin"),
        ComputerRuleActionType.RegexResult => "Résultat d'une expression régulière",
        _ => type.ToString()
    };

    public static string AppliesToLabel(ComputerRuleAppliesTo appliesTo) => appliesTo switch
    {
        ComputerRuleAppliesTo.OnCreateAndUpdate => Tr.T("Ajout / Mise à jour"),
        ComputerRuleAppliesTo.OnCreate => Tr.T("Ajout"),
        ComputerRuleAppliesTo.OnUpdate => Tr.T("Mise à jour"),
        ComputerRuleAppliesTo.OnSchedule => Tr.T("Exécution périodique"),
        ComputerRuleAppliesTo.OnCreateAndUpdate | ComputerRuleAppliesTo.OnSchedule => Tr.T("Ajout / Mise à jour / Périodique"),
        ComputerRuleAppliesTo.OnUpdate | ComputerRuleAppliesTo.OnSchedule => Tr.T("Mise à jour / Périodique"),
        _ => appliesTo.ToString()
    };

    /// <summary>Résumé d'un critère sur une ligne, comme dans la liste des règles de GLPI (ex. "Fabricant contient Dell").</summary>
    public static string CriterionSummary(ComputerRuleCriterion criterion) => IsValuelessOperator(criterion.Operator)
        ? $"{ComputerRuleFieldCatalog.Label(criterion.Field)} {OperatorLabel(criterion.Operator)}"
        : $"{ComputerRuleFieldCatalog.Label(criterion.Field)} {OperatorLabel(criterion.Operator)} {criterion.Value}";

    /// <summary>Résumé d'une action sur une ligne, comme dans la liste des règles de GLPI (ex. "Site Affecter Siège social").</summary>
    public static string ActionSummary(ComputerRuleAction action) =>
        $"{ComputerRuleFieldCatalog.Label(action.Field)} {ActionTypeLabel(action.ActionType)} {action.Value}";
}
