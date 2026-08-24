using System.Text.RegularExpressions;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Applique les règles métier actives pour les actifs (<see cref="ComputerRule"/>) à un
/// ordinateur, en mutant directement ses champs (voir <see cref="ComputerRuleFieldCatalog"/>).
/// Évalué en mémoire, sur le modèle de <see cref="DictionaryRuleEngine"/>, mais parcourt
/// TOUTES les règles actives correspondantes dans l'ordre de SortOrder plutôt que de s'arrêter
/// à la première : chaque règle voit le résultat des règles précédentes, comme dans GLPI.
/// </summary>
public static partial class ComputerRuleEngine
{
    public static void Apply(Computer computer, bool isNew, IReadOnlyList<ComputerRule> rules)
    {
        ComputerRuleAppliesTo appliesFlag = isNew ? ComputerRuleAppliesTo.OnCreate : ComputerRuleAppliesTo.OnUpdate;

        foreach (ComputerRule rule in rules
                     .Where(r => r.IsActive && (r.AppliesTo & appliesFlag) != 0)
                     .OrderBy(r => r.SortOrder))
        {
            if (!Matches(computer, rule, out Match? regexMatch))
            {
                continue;
            }

            foreach (ComputerRuleAction action in rule.Actions)
            {
                ApplyAction(computer, action, regexMatch);
            }
        }
    }

    private static bool Matches(Computer computer, ComputerRule rule, out Match? lastRegexMatch)
    {
        lastRegexMatch = null;
        if (rule.Criteria.Count == 0)
        {
            return false;
        }

        // Combine tous les critères avec l'unique opérateur logique de la règle (ET/OU),
        // pas un lien par critère — même principe que DictionaryRuleEngine.
        bool result = rule.LogicalOperator == ComputerRuleLogicalOperator.And;
        foreach (ComputerRuleCriterion criterion in rule.Criteria)
        {
            bool matches = EvaluateSingle(computer, criterion, ref lastRegexMatch);
            result = rule.LogicalOperator == ComputerRuleLogicalOperator.Or ? result || matches : result && matches;
        }

        return result;
    }

    private static bool EvaluateSingle(Computer computer, ComputerRuleCriterion criterion, ref Match? lastRegexMatch)
    {
        if (!ComputerRuleFieldCatalog.ByKey.TryGetValue(criterion.Field, out ComputerRuleFieldDefinition? field))
        {
            return false;
        }

        string? fieldValue = field.GetValue(computer);

        return criterion.Operator switch
        {
            ComputerRuleCriterionOperator.Exists => !string.IsNullOrWhiteSpace(fieldValue),
            ComputerRuleCriterionOperator.DoesNotExist => string.IsNullOrWhiteSpace(fieldValue),
            ComputerRuleCriterionOperator.Contains => Contains(fieldValue, criterion.Value),
            ComputerRuleCriterionOperator.NotContains => !Contains(fieldValue, criterion.Value),
            ComputerRuleCriterionOperator.StartsWith => fieldValue is not null && criterion.Value is not null
                && fieldValue.StartsWith(criterion.Value, StringComparison.OrdinalIgnoreCase),
            ComputerRuleCriterionOperator.EndsWith => fieldValue is not null && criterion.Value is not null
                && fieldValue.EndsWith(criterion.Value, StringComparison.OrdinalIgnoreCase),
            ComputerRuleCriterionOperator.Is => string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            ComputerRuleCriterionOperator.IsNot => !string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            ComputerRuleCriterionOperator.MatchesRegex => TryMatchRegex(fieldValue, criterion.Value, ref lastRegexMatch),
            _ => false
        };
    }

    private static void ApplyAction(Computer computer, ComputerRuleAction action, Match? regexMatch)
    {
        if (!ComputerRuleFieldCatalog.ByKey.TryGetValue(action.Field, out ComputerRuleFieldDefinition? field))
        {
            return;
        }

        string? newValue = action.ActionType switch
        {
            ComputerRuleActionType.Assign => action.Value,
            ComputerRuleActionType.Append => (field.GetValue(computer) ?? string.Empty) + (action.Value ?? string.Empty),
            ComputerRuleActionType.RegexResult => SubstituteRegexGroups(action.Value, regexMatch),
            _ => action.Value
        };

        field.SetValue(computer, newValue);
    }

    private static bool TryMatchRegex(string? fieldValue, string? pattern, ref Match? lastRegexMatch)
    {
        if (string.IsNullOrEmpty(fieldValue) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        try
        {
            Match match = Regex.Match(fieldValue, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            if (!match.Success)
            {
                return false;
            }

            lastRegexMatch = match;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            // Expression régulière invalide ou trop coûteuse saisie par l'administrateur :
            // traitée comme non-correspondante plutôt que de faire échouer tout l'import.
            return false;
        }
    }

    private static bool Contains(string? fieldValue, string? needle) =>
        !string.IsNullOrEmpty(needle) && fieldValue is not null && fieldValue.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Remplace #0 (correspondance entière), #1, #2... par les groupes capturés par le dernier critère MatchesRegex évalué — même convention que DictionaryRuleEngine.</summary>
    private static string? SubstituteRegexGroups(string? actionValue, Match? regexMatch)
    {
        if (actionValue is null || regexMatch is null)
        {
            return actionValue;
        }

        return RegexGroupTokenPattern().Replace(actionValue, m =>
        {
            int index = int.Parse(m.Groups[1].Value);
            return index < regexMatch.Groups.Count ? regexMatch.Groups[index].Value : m.Value;
        });
    }

    [GeneratedRegex(@"#(\d+)")]
    private static partial Regex RegexGroupTokenPattern();
}
