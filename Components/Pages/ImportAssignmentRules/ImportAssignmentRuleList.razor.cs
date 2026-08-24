using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.ImportAssignmentRules;

public partial class ImportAssignmentRuleList : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<ImportAssignmentRule> _rules = [];
    private readonly HashSet<int> _selectedIds = [];
    private ImportAssignmentRule _newRule = new() { Name = string.Empty };

    private bool AllSelected => _rules.Count > 0 && _selectedIds.Count == _rules.Count;

    protected override async Task OnInitializedAsync()
    {
        _newRule = new ImportAssignmentRule { Name = string.Empty };
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _rules = await db.Set<ImportAssignmentRule>()
            .AsNoTracking()
            .Include(r => r.Criteria)
            .Include(r => r.Actions)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (ImportAssignmentRule rule in _rules)
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
        List<ImportAssignmentRule> toDelete = await db.Set<ImportAssignmentRule>().Where(r => _selectedIds.Contains(r.Id)).ToListAsync();
        db.Set<ImportAssignmentRule>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task ToggleActiveAsync(ImportAssignmentRule rule)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        ImportAssignmentRule? tracked = await db.Set<ImportAssignmentRule>().FirstOrDefaultAsync(r => r.Id == rule.Id);
        if (tracked is null) return;

        tracked.IsActive = !tracked.IsActive;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task MoveAsync(ImportAssignmentRule rule, int direction)
    {
        int index = _rules.FindIndex(r => r.Id == rule.Id);
        int swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= _rules.Count) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        ImportAssignmentRule? a = await db.Set<ImportAssignmentRule>().FirstOrDefaultAsync(r => r.Id == _rules[index].Id);
        ImportAssignmentRule? b = await db.Set<ImportAssignmentRule>().FirstOrDefaultAsync(r => r.Id == _rules[swapIndex].Id);
        if (a is null || b is null) return;

        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task CreateRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRule.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _newRule.SortOrder = await db.Set<ImportAssignmentRule>().CountAsync();
        db.Set<ImportAssignmentRule>().Add(_newRule);
        await db.SaveChangesAsync();

        int newId = _newRule.Id;
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newImportAssignmentRuleModal");
        Nav.NavigateTo($"/admin/import-rules/{newId}");
    }
}
