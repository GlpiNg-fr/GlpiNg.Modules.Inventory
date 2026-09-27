using GlpiNg.Modules.Abstractions.Localization;
﻿using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Dictionaries;

public partial class DictionaryRuleDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public string TypeSlug { get; set; } = string.Empty;

    [Parameter]
    public int RuleId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DictionaryRuleType _type;
    private bool _typeResolved;
    private DictionaryRule? _rule;
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private DictionaryRuleCriterion _newCriterion = new();

    protected override async Task OnParametersSetAsync()
    {
        _typeResolved = DictionaryRuleTypeCatalog.TryParseSlug(TypeSlug, out _type);
        if (!_typeResolved)
        {
            return;
        }

        if (_rule is not null && _loadedId == RuleId)
        {
            return;
        }

        _loadedId = RuleId;
        _activeTabKey = "main";
        _newCriterion = new DictionaryRuleCriterion();

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _rule = await _db.Set<DictionaryRule>()
            .Include(r => r.Criteria)
            .FirstOrDefaultAsync(r => r.Id == RuleId && r.Type == _type);
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

        _db.Set<DictionaryRule>().Remove(_rule);
        await _db.SaveChangesAsync();
        Nav.NavigateTo($"/admin/dictionaries/{TypeSlug}");
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _rule is null) return;
        if (!IsValuelessOperator(_newCriterion.Operator) && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

        // Un champ que ce dictionnaire n'expose pas ne doit pas être enregistré : le moteur ne
        // saurait pas l'évaluer, et la règle paraîtrait simplement ne jamais s'appliquer.
        if (!DictionaryRuleTypeCatalog.CriterionFields(_type).Contains(_newCriterion.Field))
        {
            _newCriterion.Field = DictionaryCriterionField.Name;
        }

        _newCriterion.DictionaryRuleId = _rule.Id;
        _rule.Criteria.Add(_newCriterion);
        await _db.SaveChangesAsync();

        _newCriterion = new DictionaryRuleCriterion();
    }

    private async Task RemoveCriterionAsync(DictionaryRuleCriterion criterion)
    {
        if (_db is null || _rule is null) return;

        _rule.Criteria.Remove(criterion);
        _db.Set<DictionaryRuleCriterion>().Remove(criterion);
        await _db.SaveChangesAsync();
    }

    private static bool IsValuelessOperator(DictionaryCriterionOperator op) =>
        op is DictionaryCriterionOperator.Exists or DictionaryCriterionOperator.DoesNotExist;

    private static string IgnoreActionLabel(DictionaryRuleType type) => type == DictionaryRuleType.Software
        ? Tr.T("Ignorer l'import du logiciel")
        : Tr.T("Conserver la valeur existante");

    private static string CriterionFieldLabel(DictionaryRuleType type, DictionaryCriterionField field)
    {
        if (field == DictionaryCriterionField.Publisher) return Tr.T("Fabricant du logiciel");

        return type switch
        {
            DictionaryRuleType.Manufacturer => Tr.T("Fabricant"),
            DictionaryRuleType.ComputerModel => Tr.T("Modèle"),
            DictionaryRuleType.OperatingSystem => Tr.T("Système d'exploitation"),
            DictionaryRuleType.OperatingSystemVersion => Tr.T("Version de l'OS"),
            DictionaryRuleType.Software => Tr.T("Nom du logiciel"),
            _ => Tr.T("Valeur")
        };
    }

    private static string OperatorLabel(DictionaryCriterionOperator op) => op switch
    {
        DictionaryCriterionOperator.Is => "est",
        DictionaryCriterionOperator.IsNot => Tr.T("n'est pas"),
        DictionaryCriterionOperator.Contains => "contient",
        DictionaryCriterionOperator.NotContains => Tr.T("ne contient pas"),
        DictionaryCriterionOperator.StartsWith => Tr.T("commence par"),
        DictionaryCriterionOperator.EndsWith => Tr.T("finit par"),
        DictionaryCriterionOperator.MatchesRegex => "expression régulière vérifie",
        DictionaryCriterionOperator.NotMatchesRegex => "expression régulière ne vérifie pas",
        DictionaryCriterionOperator.Exists => "existe",
        DictionaryCriterionOperator.DoesNotExist => Tr.T("n'existe pas"),
        _ => op.ToString()
    };

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
