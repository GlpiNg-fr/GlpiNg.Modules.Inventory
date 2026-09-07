namespace GlpiNg.Modules.Inventory.Search;

/// <summary>Nature d'un champ interrogeable : détermine les opérateurs proposés et la façon de comparer.</summary>
public enum SearchFieldType
{
    Text,
    Number,
    Date,
}

/// <summary>
/// Description d'un champ interrogeable, sans savoir en extraire la valeur : c'est ce dont l'UI a
/// besoin (libellé, opérateurs applicables), et ça lui évite d'être générique sur le type d'actif.
/// </summary>
/// <param name="Key">Clé stable, sérialisée dans les recherches sauvegardées — ne pas renommer.</param>
public record SearchField(string Key, string Label, SearchFieldType Type)
{
    /// <summary>
    /// Clé du pseudo-champ « Tous les champs ». Le moteur le résout en balayant tous les champs
    /// texte déclarés, il n'y a donc rien à déclarer pour lui côté page.
    /// </summary>
    public const string AllFieldsKey = "all";

    public static SearchField AllFields(string label = "Tous les champs") => new(AllFieldsKey, label, SearchFieldType.Text);
}

/// <summary>
/// Champ interrogeable d'un type d'actif donné, avec de quoi en lire la valeur.
/// <paramref name="Value"/> renvoie la valeur brute ; c'est <see cref="SearchFieldType"/> qui dit
/// comment la comparer, donc une seule fonction d'accès suffit quel que soit le type.
/// </summary>
public sealed record SearchField<T>(string Key, string Label, SearchFieldType Type, Func<T, object?> Value)
    : SearchField(Key, Label, Type)
{
    /// <summary>
    /// Pseudo-champ « Tous les champs ». Son accesseur n'est jamais appelé : le moteur reconnaît
    /// la clé et balaie les champs texte déclarés à côté.
    /// </summary>
    public static SearchField<T> AllFields(string label = "Tous les champs")
        => new(AllFieldsKey, label, SearchFieldType.Text, _ => null);

    public static SearchField<T> Text(string key, string label, Func<T, object?> value) => new(key, label, SearchFieldType.Text, value);

    public static SearchField<T> Number(string key, string label, Func<T, object?> value) => new(key, label, SearchFieldType.Number, value);

    public static SearchField<T> Date(string key, string label, Func<T, object?> value) => new(key, label, SearchFieldType.Date, value);
}

/// <summary>Opérateurs proposés par type de champ, repris tels quels de la recherche de GLPI.</summary>
public static class SearchOperators
{
    /// <summary>Opérateur qui ne prend pas de valeur : l'UI masque alors le champ de saisie.</summary>
    public const string Empty = "empty";

    private static readonly (string Value, string Label)[] TextOperators =
    [
        ("contains", "contient"),
        ("notcontains", "ne contient pas"),
        ("equals", "est"),
        ("notequals", "n'est pas"),
        (Empty, "est vide"),
    ];

    private static readonly (string Value, string Label)[] NumberOperators =
    [
        ("equals", "est"),
        ("notequals", "n'est pas"),
        ("greaterthan", "supérieur à"),
        ("lessthan", "inférieur à"),
        (Empty, "est vide"),
    ];

    private static readonly (string Value, string Label)[] DateOperators =
    [
        ("equals", "est"),
        ("before", "avant le"),
        ("after", "après le"),
        (Empty, "est vide"),
    ];

    public static (string Value, string Label)[] For(SearchFieldType type) => type switch
    {
        SearchFieldType.Number => NumberOperators,
        SearchFieldType.Date => DateOperators,
        _ => TextOperators,
    };

    /// <summary>
    /// « Tous les champs » n'accepte que la présence ou l'absence du terme : les autres opérateurs
    /// n'ont pas de sens sur une recherche qui balaie plusieurs champs à la fois, et le moteur les
    /// traiterait de toute façon comme « contient ».
    /// </summary>
    public static (string Value, string Label)[] For(SearchField? field)
        => field?.Key == SearchField.AllFieldsKey
            ? [("contains", "contient"), ("notcontains", "ne contient pas")]
            : For(field?.Type ?? SearchFieldType.Text);
}
