using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.ImportAssignmentRules;

public partial class ImportAssignmentRuleDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int RuleId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private ImportAssignmentRule? _rule;
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private ImportAssignmentRuleCriterion _newCriterion = new() { Field = ImportAssignmentRuleFieldCatalog.All[0].Key };
    private ImportAssignmentRuleAction _newAction = new() { ActionType = ImportAssignmentRuleActionType.AssignLocation };

    protected override async Task OnParametersSetAsync()
    {
        if (_rule is not null && _loadedId == RuleId)
        {
            return;
        }

        _loadedId = RuleId;
        _activeTabKey = "main";
        _newCriterion = new ImportAssignmentRuleCriterion { Field = ImportAssignmentRuleFieldCatalog.All[0].Key };
        _newAction = new ImportAssignmentRuleAction { ActionType = ImportAssignmentRuleActionType.AssignLocation };

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _rule = await _db.Set<ImportAssignmentRule>()
            .Include(r => r.Criteria)
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == RuleId);
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _rule is null) return;

        _isSaving = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _rule is null) return;

        _db.Set<ImportAssignmentRule>().Remove(_rule);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/admin/import-rules");
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _rule is null) return;
        if (!ImportAssignmentRuleLabels.IsValuelessOperator(_newCriterion.Operator) && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

        _newCriterion.ImportAssignmentRuleId = _rule.Id;
        _rule.Criteria.Add(_newCriterion);
        await _db.SaveChangesAsync();

        _newCriterion = new ImportAssignmentRuleCriterion { Field = ImportAssignmentRuleFieldCatalog.All[0].Key };
    }

    private async Task RemoveCriterionAsync(ImportAssignmentRuleCriterion criterion)
    {
        if (_db is null || _rule is null) return;

        _rule.Criteria.Remove(criterion);
        _db.Set<ImportAssignmentRuleCriterion>().Remove(criterion);
        await _db.SaveChangesAsync();
    }

    private async Task AddActionAsync()
    {
        if (_db is null || _rule is null) return;
        if (ImportAssignmentRuleLabels.ActionHasValue(_newAction.ActionType) && string.IsNullOrWhiteSpace(_newAction.Value)) return;

        _newAction.ImportAssignmentRuleId = _rule.Id;
        _rule.Actions.Add(_newAction);
        await _db.SaveChangesAsync();

        _newAction = new ImportAssignmentRuleAction { ActionType = ImportAssignmentRuleActionType.AssignLocation };
    }

    private async Task RemoveActionAsync(ImportAssignmentRuleAction action)
    {
        if (_db is null || _rule is null) return;

        _rule.Actions.Remove(action);
        _db.Set<ImportAssignmentRuleAction>().Remove(action);
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
