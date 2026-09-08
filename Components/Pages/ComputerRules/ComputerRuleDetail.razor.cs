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
    private List<string> _statusOptions = [];
    private ComputerRule? _rule;
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private ComputerRuleCriterion _newCriterion = new() { Field = ComputerRuleFieldCatalog.All[0].Key };
    private ComputerRuleAction _newAction = new() { Field = ComputerRuleFieldCatalog.All[0].Key };

    private bool _testPanelOpen;
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

        // Statuts existants : une action ou un critère sur le statut se choisit dans une liste, pas
        // en saisie libre. Le moteur rapproche la valeur d'un intitulé par son nom exact, et la
        // tâche périodique n'en crée pas à la volée — une faute de frappe rendrait donc la règle
        // silencieusement sans effet.
        _statusOptions = await _db.Set<DropdownItem>().AsNoTracking()
            .Where(item => item.Type == DropdownType.Status)
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync();
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

    /// <summary>Indication de saisie adaptée à l'opérateur : heures pour une ancienneté, motif pour une expression régulière.</summary>
    private string CriterionValuePlaceholder()
    {
        if (ComputerRuleLabels.IsDurationOperator(_newCriterion.Operator))
        {
            return "nombre d'heures (ex. 24)";
        }

        return _newCriterion.Operator == ComputerRuleCriterionOperator.MatchesRegex ? "expression régulière" : string.Empty;
    }

    /// <summary>Vrai si le champ désigné est le statut, dont la valeur se choisit dans une liste.</summary>
    private static bool IsStatusField(string fieldKey)
        => ComputerRuleFieldCatalog.ByKey.TryGetValue(fieldKey, out ComputerRuleFieldDefinition? field)
           && field.Kind == ComputerRuleFieldKind.Status;

    /// <summary>
    /// Types d'action proposés pour un champ. Sur le statut, seule l'affectation a un sens :
    /// concaténer un statut ou y injecter le résultat d'une expression régulière ne donnerait
    /// jamais un intitulé existant.
    /// </summary>
    private static IEnumerable<ComputerRuleActionType> ActionTypesFor(string fieldKey)
        => IsStatusField(fieldKey)
            ? [ComputerRuleActionType.Assign]
            : Enum.GetValues<ComputerRuleActionType>();

    /// <summary>
    /// Champs affectables. Exclut ceux marqués en lecture seule — la date du dernier inventaire est
    /// un constat, pas une valeur qu'une règle réécrit ; la proposer en action n'aurait donné
    /// qu'une action sans effet.
    /// </summary>
    private static IEnumerable<ComputerRuleFieldDefinition> AssignableFields
        => ComputerRuleFieldCatalog.All.Where(candidate => !candidate.IsReadOnly);

    /// <summary>Aligne le type d'action sur le champ choisi : passer au statut doit retomber sur « Affecter ».</summary>
    private void OnActionFieldChanged(string fieldKey)
    {
        _newAction.Field = fieldKey;

        if (!ActionTypesFor(fieldKey).Contains(_newAction.ActionType))
        {
            _newAction.ActionType = ComputerRuleActionType.Assign;
        }

        _newAction.Value = null;
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _rule is null) return;
        if (!ComputerRuleLabels.IsValuelessOperator(_newCriterion.Operator) && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

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

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
