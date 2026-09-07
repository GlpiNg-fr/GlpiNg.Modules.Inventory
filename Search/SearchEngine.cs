using System.Globalization;

namespace GlpiNg.Modules.Inventory.Search;

/// <summary>
/// Applique des critères et un tri à une liste déjà chargée en mémoire, quel que soit le type
/// d'actif. C'est le moteur commun à toutes les listes du parc : une page n'a plus qu'à déclarer
/// ses <see cref="SearchField{T}"/>.
///
/// Filtrage en mémoire et non en SQL : c'est ce que faisaient déjà les listes (elles chargent leur
/// jeu complet puis filtrent/trient/paginent côté client), et le passage en SQL demanderait de
/// traduire chaque accesseur en expression. À revoir si le volume d'une liste le justifie.
/// </summary>
public static class SearchEngine
{
    /// <summary>
    /// Filtre puis trie. Les critères s'enchaînent dans l'ordre, chacun combiné au résultat
    /// courant par son <see cref="SearchCriterion.Link"/> — c'est la sémantique de GLPI, à ceci
    /// près qu'il n'y a pas de parenthésage : "A OU B ET C" se lit de gauche à droite.
    /// </summary>
    public static List<T> Apply<T>(
        IReadOnlyList<T> source,
        IReadOnlyList<SearchField<T>> fields,
        IReadOnlyList<SearchCriterion> criteria,
        IReadOnlyList<SortCriterion> sortCriteria)
    {
        List<T> matched = [.. source];
        bool first = true;

        foreach (SearchCriterion criterion in criteria)
        {
            if (!criterion.IsActive)
            {
                continue;
            }

            List<T> criterionMatches = [.. source.Where(item => Evaluate(item, fields, criterion))];

            if (first)
            {
                matched = criterionMatches;
                first = false;
            }
            else
            {
                matched = criterion.Link == "OR"
                    ? [.. matched.Union(criterionMatches)]
                    : [.. matched.Intersect(criterionMatches)];
            }
        }

        return Sort(matched, fields, sortCriteria);
    }

    public static List<T> Sort<T>(
        List<T> items,
        IReadOnlyList<SearchField<T>> fields,
        IReadOnlyList<SortCriterion> sortCriteria)
    {
        IOrderedEnumerable<T>? ordered = null;

        foreach (SortCriterion criterion in sortCriteria)
        {
            SearchField<T>? field = Find(fields, criterion.Field);
            if (field is null)
            {
                continue;
            }

            IComparable KeySelector(T item) => SortKey(field, item);

            ordered = ordered is null
                ? (criterion.Descending ? items.OrderByDescending(KeySelector) : items.OrderBy(KeySelector))
                : (criterion.Descending ? ordered.ThenByDescending(KeySelector) : ordered.ThenBy(KeySelector));
        }

        return [.. ordered ?? items.AsEnumerable()];
    }

    /// <summary>
    /// Valeurs distinctes d'un champ texte, pour la liste déroulante proposée derrière les
    /// opérateurs « est » / « n'est pas ».
    /// </summary>
    public static IEnumerable<string> DistinctValues<T>(IReadOnlyList<T> source, IReadOnlyList<SearchField<T>> fields, string fieldKey)
    {
        SearchField<T>? field = Find(fields, fieldKey);

        if (field is null || field.Type != SearchFieldType.Text)
        {
            return [];
        }

        return source
            .Select(item => AsText(field.Value(item)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase);
    }

    public static SearchField<T>? Find<T>(IReadOnlyList<SearchField<T>> fields, string key)
        => fields.FirstOrDefault(field => field.Key == key);

    private static bool Evaluate<T>(T item, IReadOnlyList<SearchField<T>> fields, SearchCriterion criterion)
    {
        if (criterion.FieldKey == SearchField.AllFieldsKey)
        {
            string term = criterion.Value.Trim();
            if (term.Length == 0)
            {
                return true;
            }

            // « Tous les champs » balaie tous les champs texte déclarés par la page, plutôt qu'une
            // liste choisie à la main : une page qui déclare un champ le rend interrogeable ici
            // sans rien de plus.
            bool anyMatch = fields
                .Where(field => field.Type == SearchFieldType.Text && field.Key != SearchField.AllFieldsKey)
                .Select(field => AsText(field.Value(item)))
                .Any(value => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);

            return criterion.Operator == "notcontains" ? !anyMatch : anyMatch;
        }

        SearchField<T>? matchedField = Find(fields, criterion.FieldKey);
        if (matchedField is null)
        {
            return true;
        }

        object? raw = matchedField.Value(item);

        return matchedField.Type switch
        {
            SearchFieldType.Number => EvaluateNumber(AsNumber(raw), criterion),
            SearchFieldType.Date => EvaluateDate(AsDate(raw), criterion),
            _ => EvaluateText(AsText(raw), criterion),
        };
    }

    private static bool EvaluateText(string? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == SearchOperators.Empty)
        {
            return string.IsNullOrWhiteSpace(raw);
        }

        string value = raw ?? string.Empty;
        string term = criterion.Value.Trim();

        return criterion.Operator switch
        {
            "contains" => value.Contains(term, StringComparison.OrdinalIgnoreCase),
            "notcontains" => !value.Contains(term, StringComparison.OrdinalIgnoreCase),
            "equals" => string.Equals(value, term, StringComparison.OrdinalIgnoreCase),
            "notequals" => !string.Equals(value, term, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static bool EvaluateNumber(double? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == SearchOperators.Empty)
        {
            return raw is null;
        }

        if (raw is null)
        {
            return false;
        }

        // Valeur non numérique saisie : critère ignoré plutôt que liste vidée sans explication.
        if (!double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.CurrentCulture, out double target)
            && !double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out target))
        {
            return true;
        }

        return criterion.Operator switch
        {
            "equals" => raw.Value == target,
            "notequals" => raw.Value != target,
            "greaterthan" => raw.Value > target,
            "lessthan" => raw.Value < target,
            _ => true,
        };
    }

    private static bool EvaluateDate(DateTime? raw, SearchCriterion criterion)
    {
        if (criterion.Operator == SearchOperators.Empty)
        {
            return raw is null;
        }

        if (raw is null)
        {
            return false;
        }

        if (!DateTime.TryParse(criterion.Value, out DateTime target))
        {
            return true;
        }

        DateTime rawDate = raw.Value.Date;
        DateTime targetDate = target.Date;

        return criterion.Operator switch
        {
            "equals" => rawDate == targetDate,
            "before" => rawDate < targetDate,
            "after" => rawDate > targetDate,
            _ => true,
        };
    }

    private static IComparable SortKey<T>(SearchField<T> field, T item)
    {
        object? raw = field.Value(item);

        return field.Type switch
        {
            // Les valeurs absentes se regroupent en tête d'un tri croissant, comme avant.
            SearchFieldType.Number => AsNumber(raw) ?? double.MinValue,
            SearchFieldType.Date => AsDate(raw) ?? DateTime.MinValue,
            _ => AsText(raw) ?? string.Empty,
        };
    }

    private static string? AsText(object? raw) => raw switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => raw.ToString(),
    };

    private static double? AsNumber(object? raw) => raw switch
    {
        null => null,
        double value => value,
        int value => value,
        long value => value,
        decimal value => (double)value,
        float value => value,
        string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => null,
    };

    private static DateTime? AsDate(object? raw) => raw switch
    {
        null => null,
        DateTime value => value,
        DateTimeOffset value => value.DateTime,
        string text when DateTime.TryParse(text, out DateTime parsed) => parsed,
        _ => null,
    };
}
