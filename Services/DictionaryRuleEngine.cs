using System.Text.RegularExpressions;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>
/// Applique les règles de dictionnaire actives d'un type donné à une valeur brute d'inventaire
/// (voir DictionaryRuleType). Évalué en mémoire, sur le modèle de DeployGroupCriteriaEvaluator
/// (GlpiNg.Modules.Deployment) : parmi les règles actives triées par SortOrder, la première dont
/// les critères correspondent l'emporte et les suivantes ne sont pas évaluées (comportement
/// "le moteur s'arrête à la première règle vérifiée" du dictionnaire GLPI réel).
/// </summary>
public static partial class DictionaryRuleEngine
{
    public readonly record struct Result(string? Value, bool Ignore);

    public static Result Apply(string? name, IReadOnlyList<DictionaryRule> rules) => Apply(name, null, rules);

    /// <param name="publisher">Fabricant du logiciel, uniquement pertinent pour DictionaryRuleType.Software (voir DictionaryCriterionField.Publisher).</param>
    public static Result Apply(string? name, string? publisher, IReadOnlyList<DictionaryRule> rules)
    {
        foreach (DictionaryRule rule in rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder))
        {
            if (!Matches(name, publisher, rule, out Match? regexMatch))
            {
                continue;
            }

            return rule.ActionType == DictionaryActionType.Ignore
                ? new Result(name, true)
                : new Result(SubstituteRegexGroups(rule.ActionValue, regexMatch), false);
        }

        return new Result(name, false);
    }

    private static bool Matches(string? name, string? publisher, DictionaryRule rule, out Match? lastRegexMatch)
    {
        lastRegexMatch = null;
        if (rule.Criteria.Count == 0)
        {
            return false;
        }

        // Combine tous les critères avec l'unique opérateur logique de la règle (ET/OU),
        // pas un lien par critère : c'est ainsi que le dictionnaire GLPI réel fonctionne.
        bool result = rule.LogicalOperator == DictionaryRuleLogicalOperator.And;
        foreach (DictionaryRuleCriterion criterion in rule.Criteria)
        {
            string? fieldValue = criterion.Field == DictionaryCriterionField.Publisher ? publisher : name;
            bool matches = EvaluateSingle(fieldValue, criterion, ref lastRegexMatch);
            result = rule.LogicalOperator == DictionaryRuleLogicalOperator.Or ? result || matches : result && matches;
        }

        return result;
    }

    private static bool EvaluateSingle(string? fieldValue, DictionaryRuleCriterion criterion, ref Match? lastRegexMatch) => criterion.Operator switch
    {
        DictionaryCriterionOperator.Exists => !string.IsNullOrWhiteSpace(fieldValue),
        DictionaryCriterionOperator.DoesNotExist => string.IsNullOrWhiteSpace(fieldValue),
        DictionaryCriterionOperator.Contains => Contains(fieldValue, criterion.Value),
        DictionaryCriterionOperator.NotContains => !Contains(fieldValue, criterion.Value),
        DictionaryCriterionOperator.StartsWith => fieldValue is not null && criterion.Value is not null
            && fieldValue.StartsWith(criterion.Value, StringComparison.OrdinalIgnoreCase),
        DictionaryCriterionOperator.EndsWith => fieldValue is not null && criterion.Value is not null
            && fieldValue.EndsWith(criterion.Value, StringComparison.OrdinalIgnoreCase),
        DictionaryCriterionOperator.Is => string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
        DictionaryCriterionOperator.IsNot => !string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
        DictionaryCriterionOperator.MatchesRegex => TryMatchRegex(fieldValue, criterion.Value, ref lastRegexMatch),
        DictionaryCriterionOperator.NotMatchesRegex => !TryMatchRegex(fieldValue, criterion.Value, ref lastRegexMatch),
        _ => false
    };

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

    /// <summary>Remplace #0 (correspondance entière), #1, #2... par les groupes capturés par le dernier critère MatchesRegex évalué — même convention que le dictionnaire GLPI réel.</summary>
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
