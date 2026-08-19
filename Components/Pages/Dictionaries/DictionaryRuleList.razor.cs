using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Dictionaries;

public partial class DictionaryRuleList : ComponentBase
{
    [Parameter]
    public string? TypeSlug { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private DictionaryRuleType _type;
    private bool _typeResolved;
    private List<DictionaryRule> _rules = [];
    private readonly HashSet<int> _selectedIds = [];
    private DictionaryRule _newRule = new() { Name = string.Empty };

    private bool AllSelected => _rules.Count > 0 && _selectedIds.Count == _rules.Count;

    protected override async Task OnParametersSetAsync()
    {
        // Slug absent (route "/admin/dictionaries" sans segment) : bascule sur le premier type par défaut.
        _typeResolved = string.IsNullOrEmpty(TypeSlug)
            ? SetDefaultType()
            : DictionaryRuleTypeCatalog.TryParseSlug(TypeSlug, out _type);

        if (!_typeResolved)
        {
            return;
        }

        _newRule = new DictionaryRule { Name = string.Empty, Type = _type };
        await LoadAsync();
    }

    private bool SetDefaultType()
    {
        _type = DictionaryRuleTypeCatalog.All[0];
        return true;
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _rules = await db.Set<DictionaryRule>()
            .AsNoTracking()
            .Include(r => r.Criteria)
            .Where(r => r.Type == _type)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DictionaryRule rule in _rules)
            {
                _selectedIds.Add(rule.Id);
            }
        }
    }

    private void ToggleSelect(int id, bool selected)
    {
        if (selected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DictionaryRule> toDelete = await db.Set<DictionaryRule>().Where(r => _selectedIds.Contains(r.Id)).ToListAsync();
        db.Set<DictionaryRule>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task ToggleActiveAsync(DictionaryRule rule)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DictionaryRule? tracked = await db.Set<DictionaryRule>().FirstOrDefaultAsync(r => r.Id == rule.Id);
        if (tracked is null) return;

        tracked.IsActive = !tracked.IsActive;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task MoveAsync(DictionaryRule rule, int direction)
    {
        int index = _rules.FindIndex(r => r.Id == rule.Id);
        int swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= _rules.Count) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DictionaryRule? a = await db.Set<DictionaryRule>().FirstOrDefaultAsync(r => r.Id == _rules[index].Id);
        DictionaryRule? b = await db.Set<DictionaryRule>().FirstOrDefaultAsync(r => r.Id == _rules[swapIndex].Id);
        if (a is null || b is null) return;

        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task CreateRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRule.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _newRule.SortOrder = await db.Set<DictionaryRule>().Where(r => r.Type == _type).CountAsync();
        db.Set<DictionaryRule>().Add(_newRule);
        await db.SaveChangesAsync();

        int newId = _newRule.Id;
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newDictionaryRuleModal");
        Nav.NavigateTo($"/admin/dictionaries/{DictionaryRuleTypeCatalog.Slug(_type)}/{newId}");
    }

    private static string ActionSummary(DictionaryRule rule) => rule.ActionType == DictionaryActionType.Ignore
        ? (rule.Type == DictionaryRuleType.Software ? "Ignorer l'import du logiciel" : "Conserver la valeur existante")
        : $"Remplacer par « {rule.ActionValue} »";
}
