using System.Text.RegularExpressions;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Résultat de l'évaluation : au plus une règle d'affectation à l'import s'applique (la première vérifiée), voir ImportAssignmentRuleEngine.Evaluate.</summary>
public sealed record ImportAssignmentRuleResult(bool Refuse, string? LocationName, string? Technician, string? MatchedRuleName)
{
    public static readonly ImportAssignmentRuleResult NoMatch = new(false, null, null, null);
}

/// <summary>
/// Applique les <see cref="ImportAssignmentRule"/> actives à un contexte d'inventaire, en
/// s'arrêtant à la première règle vérifiée — contrairement à <see cref="ComputerRuleEngine"/>
/// (qui applique TOUTES les règles correspondantes), ce moteur reproduit le comportement de
/// RuleImportEntity dans GLPI ("Le moteur s'arrête à la première règle vérifiée").
/// </summary>
public static class ImportAssignmentRuleEngine
{
    public static ImportAssignmentRuleResult Evaluate(ImportAssignmentRuleContext context, IReadOnlyList<ImportAssignmentRule> rules)
    {
        foreach (ImportAssignmentRule rule in rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder))
        {
            if (!Matches(context, rule))
            {
                continue;
            }

            bool refuse = false;
            string? locationName = null;
            string? technician = null;

            foreach (ImportAssignmentRuleAction action in rule.Actions)
            {
                switch (action.ActionType)
                {
                    case ImportAssignmentRuleActionType.RefuseImport:
                        refuse = true;
                        break;
                    case ImportAssignmentRuleActionType.AssignLocation:
                        locationName = action.Value;
                        break;
                    case ImportAssignmentRuleActionType.AssignTechnician:
                        technician = action.Value;
                        break;
                }
            }

            return new ImportAssignmentRuleResult(refuse, locationName, technician, rule.Name);
        }

        return ImportAssignmentRuleResult.NoMatch;
    }

    private static bool Matches(ImportAssignmentRuleContext context, ImportAssignmentRule rule)
    {
        if (rule.Criteria.Count == 0)
        {
            return false;
        }

        // Combine tous les critères avec l'unique opérateur logique de la règle (ET/OU), pas un
        // lien par critère — même principe que ComputerRuleEngine/DictionaryRuleEngine.
        bool result = rule.LogicalOperator == ImportAssignmentRuleLogicalOperator.And;
        foreach (ImportAssignmentRuleCriterion criterion in rule.Criteria)
        {
            bool matches = EvaluateSingle(context, criterion);
            result = rule.LogicalOperator == ImportAssignmentRuleLogicalOperator.Or ? result || matches : result && matches;
        }

        return result;
    }

    private static bool EvaluateSingle(ImportAssignmentRuleContext context, ImportAssignmentRuleCriterion criterion)
    {
        if (!ImportAssignmentRuleFieldCatalog.ByKey.TryGetValue(criterion.Field, out ImportAssignmentRuleFieldDefinition? field))
        {
            return false;
        }

        IReadOnlyList<string?> values = field.GetValues(context);

        return criterion.Operator switch
        {
            ImportAssignmentRuleCriterionOperator.Exists => values.Any(v => !string.IsNullOrWhiteSpace(v)),
            ImportAssignmentRuleCriterionOperator.DoesNotExist => values.All(v => string.IsNullOrWhiteSpace(v)),
            // Opérateurs de négation : vérifiés si TOUTES les valeurs (ex. toutes les interfaces
            // réseau) satisfont la négation — équivalent De Morgan de "aucune ne satisfait le
            // positif", pour rester cohérent avec Exists/DoesNotExist ci-dessus sur un champ
            // multi-valué comme l'adresse IP.
            ImportAssignmentRuleCriterionOperator.IsNot =>
                values.All(v => !EvaluatePositive(v, ImportAssignmentRuleCriterionOperator.Is, criterion.Value)),
            ImportAssignmentRuleCriterionOperator.NotContains =>
                values.All(v => !EvaluatePositive(v, ImportAssignmentRuleCriterionOperator.Contains, criterion.Value)),
            ImportAssignmentRuleCriterionOperator.NotMatchesRegex =>
                values.All(v => !EvaluatePositive(v, ImportAssignmentRuleCriterionOperator.MatchesRegex, criterion.Value)),
            _ => values.Any(v => EvaluatePositive(v, criterion.Operator, criterion.Value))
        };
    }

    private static bool EvaluatePositive(string? value, ImportAssignmentRuleCriterionOperator positiveOperator, string? compareValue) => positiveOperator switch
    {
        ImportAssignmentRuleCriterionOperator.Is => string.Equals(value?.Trim(), compareValue?.Trim(), StringComparison.OrdinalIgnoreCase),
        ImportAssignmentRuleCriterionOperator.Contains => Contains(value, compareValue),
        ImportAssignmentRuleCriterionOperator.StartsWith => value is not null && compareValue is not null
            && value.StartsWith(compareValue, StringComparison.OrdinalIgnoreCase),
        ImportAssignmentRuleCriterionOperator.EndsWith => value is not null && compareValue is not null
            && value.EndsWith(compareValue, StringComparison.OrdinalIgnoreCase),
        ImportAssignmentRuleCriterionOperator.MatchesRegex => TryMatchRegex(value, compareValue),
        _ => false
    };

    private static bool Contains(string? value, string? needle) =>
        !string.IsNullOrEmpty(needle) && value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool TryMatchRegex(string? value, string? pattern)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            // Expression régulière invalide ou trop coûteuse saisie par l'administrateur : traitée
            // comme non-correspondante plutôt que de faire échouer tout l'import — même choix que
            // ComputerRuleEngine.TryMatchRegex.
            return false;
        }
    }
}
