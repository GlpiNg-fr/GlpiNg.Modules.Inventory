using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Un Lieu tel qu'il s'affiche dans un select : son identifiant et son chemin complet.</summary>
public sealed record LocationOption(int Id, string Path);

/// <summary>
/// Mise en forme de l'arborescence des Lieux (<see cref="DropdownType.Location"/>) pour l'affichage.
///
/// Un tri alphabétique sur le seul nom de l'élément mélange les niveaux : « CAD », « Calcul »,
/// « Ingénierie », « Loire », « Office », « Usine » ne laisse pas voir que CAD et Calcul sont sous
/// Ingénierie. Pire depuis que deux Lieux homonymes peuvent coexister sous des parents différents
/// (voir l'index unique de DropdownItem) : deux « Office » deviennent indiscernables.
/// </summary>
public static class LocationHierarchy
{
    private const int MaxDepth = 20;

    /// <summary>
    /// Réordonne une liste de Lieux <b>déjà triée par nom</b> en ordre d'arborescence : chaque
    /// parent immédiatement suivi de ses enfants, eux-mêmes dans l'ordre alphabétique hérité de
    /// l'appelant.
    /// </summary>
    public static List<DropdownItem> Sort(IReadOnlyCollection<DropdownItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // 0 comme clé "racine" : sans danger, les Id générés par la base démarrent à 1.
        Dictionary<int, List<DropdownItem>> byParent = items
            .GroupBy(item => item.ParentId ?? 0)
            .ToDictionary(group => group.Key, group => group.ToList());

        List<DropdownItem> result = [];
        void Walk(int parentId, int depthGuard)
        {
            if (depthGuard > MaxDepth || !byParent.TryGetValue(parentId, out List<DropdownItem>? children)) return;
            foreach (DropdownItem child in children)
            {
                result.Add(child);
                Walk(child.Id, depthGuard + 1);
            }
        }
        Walk(0, 0);

        // Garde-fou : un ParentId invalide (cycle, ou pointant vers un Id absent) laisserait des
        // éléments hors de l'arbre parcouru — on les rattache en fin de liste plutôt que de les perdre.
        if (result.Count != items.Count)
        {
            result.AddRange(items.Except(result));
        }

        return result;
    }

    /// <summary>
    /// Liste prête pour un <c>&lt;select&gt;</c> : ordre d'arborescence, et chemin complet
    /// (« Ingénierie &gt; Office ») comme libellé.
    ///
    /// L'ordre alphabétique des frères vient de l'appelant ; il est réappliqué ici pour que la
    /// méthode reste juste même si la liste reçue n'était pas triée.
    /// </summary>
    public static List<LocationOption> BuildOptions(IEnumerable<DropdownItem> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        List<DropdownItem> byName = [.. locations.OrderBy(location => location.Name, StringComparer.CurrentCultureIgnoreCase)];
        Dictionary<int, DropdownItem> byId = byName
            .GroupBy(location => location.Id)
            .ToDictionary(group => group.Key, group => group.First());

        return [.. Sort(byName).Select(location => new LocationOption(location.Id, PathLabel(location, byId)))];
    }

    /// <summary>
    /// Chemin complet d'un Lieu (« Site &gt; Bâtiment &gt; Salle »), en remontant les parents dans
    /// la liste déjà chargée — jamais de requête supplémentaire, borné en profondeur comme
    /// garde-fou contre un cycle accidentel.
    /// </summary>
    public static string PathLabel(DropdownItem item, IReadOnlyDictionary<int, DropdownItem> byId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(byId);

        List<string> parts = [item.Name];
        int? parentId = item.ParentId;
        int guard = 0;

        while (parentId is int pid && byId.TryGetValue(pid, out DropdownItem? parent) && guard++ < MaxDepth)
        {
            parts.Insert(0, parent.Name);
            parentId = parent.ParentId;
        }

        return string.Join(" > ", parts);
    }
}
