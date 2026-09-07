namespace GlpiNg.Modules.Inventory.Search;

/// <summary>
/// Un critère de recherche. Classe mutable et non record : l'UI lie directement ses propriétés
/// aux champs du formulaire.
///
/// Sérialisée telle quelle dans <c>SavedSearch.CriteriaJson</c> — les noms de propriétés font donc
/// partie du format de stockage et ne doivent pas être renommés sans reprise des données.
/// </summary>
public sealed class SearchCriterion
{
    /// <summary>Liaison avec le critère précédent : "AND" ou "OR". Ignoré sur le premier critère.</summary>
    public string Link { get; set; } = "AND";

    public string FieldKey { get; set; } = SearchField.AllFieldsKey;

    public string Operator { get; set; } = "contains";

    /// <summary>Valeur saisie, toujours en texte : c'est le type du champ qui dit comment la lire.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Un critère sans valeur n'est pas appliqué, sauf l'opérateur « est vide » qui n'en attend pas.</summary>
    public bool IsActive => Operator == SearchOperators.Empty || !string.IsNullOrWhiteSpace(Value);
}

/// <summary>Un niveau de tri. Même remarque que <see cref="SearchCriterion"/> sur la sérialisation.</summary>
public sealed class SortCriterion
{
    public string Field { get; set; } = "name";

    public bool Descending { get; set; }
}
