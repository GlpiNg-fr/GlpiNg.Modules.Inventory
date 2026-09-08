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
    /// <param name="statusResolver">
    /// Traduit un nom de statut en identifiant d'intitulé. Fourni par l'appelant, qui a pré-résolu
    /// les noms utilisés par les règles actives : la résolution demande un accès base, que ce
    /// moteur — volontairement synchrone et sans dépendance — ne peut pas faire lui-même. Absent,
    /// les actions sur le statut sont ignorées.
    /// </param>
    /// <param name="now">
    /// Instant de référence des critères d'ancienneté. Paramétrable pour que l'évaluation d'un lot
    /// de postes reste cohérente d'un bout à l'autre du traitement.
    /// </param>
    public static void Apply(
        Computer computer,
        bool isNew,
        IReadOnlyList<ComputerRule> rules,
        Func<string, int?>? statusResolver = null,
        DateTime? now = null)
        => Apply(computer, isNew ? ComputerRuleAppliesTo.OnCreate : ComputerRuleAppliesTo.OnUpdate, rules, statusResolver, now);

    /// <summary>
    /// Applique les règles correspondant à un moment donné. L'exécution périodique
    /// (<see cref="ComputerRuleAppliesTo.OnSchedule"/>) passe par ici : c'est le seul moment où une
    /// condition d'ancienneté peut être vérifiée, un inventaire venant par définition d'avoir lieu.
    /// </summary>
    public static void Apply(
        Computer computer,
        ComputerRuleAppliesTo moment,
        IReadOnlyList<ComputerRule> rules,
        Func<string, int?>? statusResolver = null,
        DateTime? now = null)
    {
        DateTime reference = now ?? DateTime.UtcNow;

        foreach (ComputerRule rule in rules
                     .Where(r => r.IsActive && (r.AppliesTo & moment) != 0)
                     .OrderBy(r => r.SortOrder))
        {
            if (!Matches(computer, rule, reference, out Match? regexMatch))
            {
                continue;
            }

            foreach (ComputerRuleAction action in rule.Actions)
            {
                ApplyAction(computer, action, regexMatch, statusResolver);
            }
        }
    }

    private static bool Matches(Computer computer, ComputerRule rule, DateTime now, out Match? lastRegexMatch)
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
            bool matches = EvaluateSingle(computer, criterion, now, ref lastRegexMatch);
            result = rule.LogicalOperator == ComputerRuleLogicalOperator.Or ? result || matches : result && matches;
        }

        return result;
    }

    private static bool EvaluateSingle(Computer computer, ComputerRuleCriterion criterion, DateTime now, ref Match? lastRegexMatch)
    {
        if (!ComputerRuleFieldCatalog.ByKey.TryGetValue(criterion.Field, out ComputerRuleFieldDefinition? field))
        {
            return false;
        }

        if (field.Kind == ComputerRuleFieldKind.Date)
        {
            return EvaluateDate(field.GetDate?.Invoke(computer), criterion, now);
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

    /// <summary>
    /// Critère d'ancienneté. Une date absente n'est jamais « plus ancienne que » : un poste jamais
    /// inventorié n'a pas un contact vieux, il n'en a pas. Le traiter autrement ferait basculer
    /// d'un coup tout le parc jamais contacté au premier passage de la règle.
    /// </summary>
    private static bool EvaluateDate(DateTime? value, ComputerRuleCriterion criterion, DateTime now)
    {
        if (criterion.Operator == ComputerRuleCriterionOperator.Exists)
        {
            return value is not null;
        }

        if (criterion.Operator == ComputerRuleCriterionOperator.DoesNotExist)
        {
            return value is null;
        }

        if (value is not DateTime date || !double.TryParse(criterion.Value, out double hours))
        {
            return false;
        }

        TimeSpan age = now - date;

        return criterion.Operator switch
        {
            ComputerRuleCriterionOperator.OlderThanHours => age.TotalHours > hours,
            ComputerRuleCriterionOperator.WithinLastHours => age.TotalHours <= hours,
            _ => false,
        };
    }

    private static void ApplyAction(Computer computer, ComputerRuleAction action, Match? regexMatch, Func<string, int?>? statusResolver)
    {
        if (!ComputerRuleFieldCatalog.ByKey.TryGetValue(action.Field, out ComputerRuleFieldDefinition? field))
        {
            return;
        }

        if (field.IsReadOnly)
        {
            return;
        }

        if (field.Kind == ComputerRuleFieldKind.Status)
        {
            // Sans résolveur, l'action est ignorée plutôt qu'appliquée à moitié : mieux vaut un
            // statut inchangé qu'un statut vidé.
            if (statusResolver is not null && action.Value is { Length: > 0 } statusName
                && statusResolver(statusName) is { } statusId)
            {
                computer.StatusId = statusId;
            }

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
