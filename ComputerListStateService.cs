namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Scoped service (Blazor circuit lifetime) that persists the computer list's
/// filter/sort/page state so it survives navigation to a detail page and back.
/// </summary>
public sealed class ComputerListStateService
{
    public string? CriteriaJson { get; set; }
    public string? SortJson { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public List<int> FilteredIds { get; set; } = [];
    public bool HasState { get; set; }
}
