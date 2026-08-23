using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.ComputerRules;

public partial class ComputerRuleDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int RuleId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private ComputerRule? _rule;
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private ComputerRuleCriterion _newCriterion = new() { Field = ComputerRuleFieldCatalog.All[0].Key };
    private ComputerRuleAction _newAction = new() { Field = ComputerRuleFieldCatalog.All[0].Key };

    private List<Computer> _testComputers = [];
    private int _testComputerId;
    private bool _testIsUpdate;
    private bool _testRan;
    private bool _testMatched;
    private List<(string Label, string? Before, string? After)> _testChanges = [];

    protected override async Task OnParametersSetAsync()
    {
        if (_rule is not null && _loadedId == RuleId)
        {
            return;
        }

        _loadedId = RuleId;
        _activeTabKey = "main";
        _newCriterion = new ComputerRuleCriterion { Field = ComputerRuleFieldCatalog.All[0].Key };
        _newAction = new ComputerRuleAction { Field = ComputerRuleFieldCatalog.All[0].Key };
        _testRan = false;

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _rule = await _db.Set<ComputerRule>()
            .Include(r => r.Criteria)
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == RuleId);

        _testComputers = await _db.Set<Computer>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
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

        _db.Set<ComputerRule>().Remove(_rule);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/admin/rules");
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _rule is null) return;
        if (!IsValuelessOperator(_newCriterion.Operator) && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

        _newCriterion.ComputerRuleId = _rule.Id;
        _rule.Criteria.Add(_newCriterion);
        await _db.SaveChangesAsync();

        _newCriterion = new ComputerRuleCriterion { Field = ComputerRuleFieldCatalog.All[0].Key };
    }

    private async Task RemoveCriterionAsync(ComputerRuleCriterion criterion)
    {
        if (_db is null || _rule is null) return;

        _rule.Criteria.Remove(criterion);
        _db.Set<ComputerRuleCriterion>().Remove(criterion);
        await _db.SaveChangesAsync();
    }

    private async Task AddActionAsync()
    {
        if (_db is null || _rule is null) return;

        _newAction.ComputerRuleId = _rule.Id;
        _rule.Actions.Add(_newAction);
        await _db.SaveChangesAsync();

        _newAction = new ComputerRuleAction { Field = ComputerRuleFieldCatalog.All[0].Key };
    }

    private async Task RemoveActionAsync(ComputerRuleAction action)
    {
        if (_db is null || _rule is null) return;

        _rule.Actions.Remove(action);
        _db.Set<ComputerRuleAction>().Remove(action);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Prévisualise l'effet de la règle telle qu'enregistrée sur un ordinateur existant, sans
    /// toucher à la base : clone les champs pertinents, applique ComputerRuleEngine avec
    /// uniquement cette règle, puis compare avant/après.
    /// </summary>
    private async Task RunTestAsync()
    {
        _testRan = true;
        _testMatched = false;
        _testChanges = [];

        if (_db is null || _rule is null) return;

        Computer? source = await _db.Set<Computer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == _testComputerId);
        if (source is null) return;

        Computer before = ClonePreviewFields(source);
        Computer after = ClonePreviewFields(source);

        ComputerRuleEngine.Apply(after, isNew: !_testIsUpdate, [_rule]);

        _testChanges = ComputerRuleFieldCatalog.All
            .Select(f => (f.Label, Before: f.GetValue(before), After: f.GetValue(after)))
            .Where(c => c.Before != c.After)
            .ToList();

        _testMatched = _testChanges.Count > 0;
    }

    private static Computer ClonePreviewFields(Computer source)
    {
        Computer clone = new() { Name = source.Name };
        foreach (ComputerRuleFieldDefinition field in ComputerRuleFieldCatalog.All)
        {
            field.SetValue(clone, field.GetValue(source));
        }

        return clone;
    }

    private static bool IsValuelessOperator(ComputerRuleCriterionOperator op) =>
        op is ComputerRuleCriterionOperator.Exists or ComputerRuleCriterionOperator.DoesNotExist;

    private static string OperatorLabel(ComputerRuleCriterionOperator op) => op switch
    {
        ComputerRuleCriterionOperator.Is => "est",
        ComputerRuleCriterionOperator.IsNot => "n'est pas",
        ComputerRuleCriterionOperator.Contains => "contient",
        ComputerRuleCriterionOperator.NotContains => "ne contient pas",
        ComputerRuleCriterionOperator.StartsWith => "commence par",
        ComputerRuleCriterionOperator.EndsWith => "finit par",
        ComputerRuleCriterionOperator.MatchesRegex => "expression régulière vérifie",
        ComputerRuleCriterionOperator.Exists => "existe",
        ComputerRuleCriterionOperator.DoesNotExist => "n'existe pas",
        _ => op.ToString()
    };

    private static string ActionTypeLabel(ComputerRuleActionType type) => type switch
    {
        ComputerRuleActionType.Assign => "Affecter",
        ComputerRuleActionType.Append => "Ajouter à la fin",
        ComputerRuleActionType.RegexResult => "Résultat d'une expression régulière",
        _ => type.ToString()
    };

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
