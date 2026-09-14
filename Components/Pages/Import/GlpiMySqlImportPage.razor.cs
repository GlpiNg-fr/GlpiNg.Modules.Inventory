using GlpiNg.Modules.Abstractions.Import;
using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Components;

namespace GlpiNg.Modules.Inventory.Components.Pages.Import;

public partial class GlpiMySqlImportPage : ComponentBase, IDisposable
{
    /// <summary>
    /// Onglets de la sélection. Ils recoupent exactement les quatre services d'import (parc,
    /// administration, base de connaissances, plugin d'inventaire), qui ont chacun leur analyse,
    /// leur sélection et leur résultat.
    /// </summary>
    private enum ImportTab
    {
        Parc,
        Administration,
        KnowledgeBase,
        Plugin,
    }

    private ImportTab _tab = ImportTab.Parc;

    [Inject]
    private GlpiImportStateService State { get; set; } = null!;

    protected override void OnInitialized()
    {
        State.Changed += OnStateChanged;
    }

    private Task AnalyzeAsync() => State.AnalyzeAsync();

    private Task RunImportAsync() => State.RunAsync();

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => State.Changed -= OnStateChanged;

    // ---- Interrupteurs de titre d'onglet -----------------------------------
    //
    // Une catégorie que la base source n'a pas reste hors du décompte comme hors de la bascule :
    // son interrupteur individuel est désactivé, et l'interrupteur d'onglet doit dire la même
    // chose — sinon il afficherait « rien de coché » sur un onglet où il n'y a rien à cocher.

    private (int Selected, int Available) ParcTally(GlpiImportAnalysis analysis)
    {
        (bool Selected, int Count)[] items =
        [
            (State.Selection.ImportComputers, analysis.ComputersCount),
            (State.Selection.ImportMonitors, analysis.MonitorsCount),
            (State.Selection.ImportAgents, analysis.AgentsCount),
            (State.Selection.ImportComponents, analysis.ComponentsCount),
            (State.Selection.ImportSoftwares, analysis.SoftwaresCount),
            (State.Selection.ImportPrinters, analysis.PrintersCount),
            (State.Selection.ImportPeripherals, analysis.PeripheralsCount),
            (State.Selection.ImportVolumes, analysis.VolumesCount),
            (State.Selection.ImportBatteries, analysis.BatteriesCount),
        ];

        return (items.Count(item => item is { Selected: true, Count: > 0 }), items.Count(item => item.Count > 0));
    }

    private void SetAllParc(GlpiImportAnalysis analysis, bool selected)
    {
        State.Selection.ImportComputers = selected && analysis.ComputersCount > 0;
        State.Selection.ImportMonitors = selected && analysis.MonitorsCount > 0;
        State.Selection.ImportAgents = selected && analysis.AgentsCount > 0;
        State.Selection.ImportComponents = selected && analysis.ComponentsCount > 0;
        State.Selection.ImportSoftwares = selected && analysis.SoftwaresCount > 0;
        State.Selection.ImportPrinters = selected && analysis.PrintersCount > 0;
        State.Selection.ImportPeripherals = selected && analysis.PeripheralsCount > 0;
        State.Selection.ImportVolumes = selected && analysis.VolumesCount > 0;
        State.Selection.ImportBatteries = selected && analysis.BatteriesCount > 0;
    }

    private (int Selected, int Available) AdminTally(GlpiAdminImportAnalysis analysis)
    {
        (bool Selected, int Count)[] items =
        [
            (State.AdminSelection.ImportEntities, analysis.EntitiesCount),
            (State.AdminSelection.ImportGroups, analysis.GroupsCount),
            (State.AdminSelection.ImportProfiles, analysis.ProfilesCount),
            (State.AdminSelection.ImportUsers, analysis.UsersCount),
            (State.AdminSelection.ImportGeneralConfig, analysis.GeneralConfigAvailable ? 1 : 0),
        ];

        return (items.Count(item => item is { Selected: true, Count: > 0 }), items.Count(item => item.Count > 0));
    }

    private void SetAllAdmin(GlpiAdminImportAnalysis analysis, bool selected)
    {
        State.AdminSelection.ImportEntities = selected && analysis.EntitiesCount > 0;
        State.AdminSelection.ImportGroups = selected && analysis.GroupsCount > 0;
        State.AdminSelection.ImportProfiles = selected && analysis.ProfilesCount > 0;
        State.AdminSelection.ImportUsers = selected && analysis.UsersCount > 0;
        State.AdminSelection.ImportGeneralConfig = selected && analysis.GeneralConfigAvailable;
    }

    private (int Selected, int Available) KnowledgeBaseTally(GlpiKnowledgeBaseImportAnalysis analysis)
    {
        (bool Selected, int Count)[] items =
        [
            (State.KnowledgeBaseSelection.ImportCategories, analysis.CategoriesCount),
            (State.KnowledgeBaseSelection.ImportArticles, analysis.ArticlesCount),
            (State.KnowledgeBaseSelection.ImportTargets, analysis.TargetsCount),
            (State.KnowledgeBaseSelection.ImportRevisions, analysis.RevisionsCount),
        ];

        return (items.Count(item => item is { Selected: true, Count: > 0 }), items.Count(item => item.Count > 0));
    }

    private void SetAllKnowledgeBase(GlpiKnowledgeBaseImportAnalysis analysis, bool selected)
    {
        State.KnowledgeBaseSelection.ImportCategories = selected && analysis.CategoriesCount > 0;
        State.KnowledgeBaseSelection.ImportArticles = selected && analysis.ArticlesCount > 0;
        State.KnowledgeBaseSelection.ImportTargets = selected && analysis.TargetsCount > 0;
        State.KnowledgeBaseSelection.ImportRevisions = selected && analysis.RevisionsCount > 0;
    }

    private (int Selected, int Available) PluginTally(GlpiInventoryPluginInfo plugin)
    {
        (bool Selected, int Count)[] items =
        [
            (State.PluginSelection.ImportIpRanges, plugin.IpRangesCount),
            (State.PluginSelection.ImportSnmpCredentials, plugin.SnmpCredentialsCount),
            (State.PluginSelection.ImportDeployPackages, plugin.DeployPackagesCount),
            (State.PluginSelection.ImportUnmanagedDevices, plugin.UnmanagedDevicesCount),
        ];

        return (items.Count(item => item is { Selected: true, Count: > 0 }), items.Count(item => item.Count > 0));
    }

    private void SetAllPlugin(GlpiInventoryPluginInfo plugin, bool selected)
    {
        State.PluginSelection.ImportIpRanges = selected && plugin.IpRangesCount > 0;
        State.PluginSelection.ImportSnmpCredentials = selected && plugin.SnmpCredentialsCount > 0;
        State.PluginSelection.ImportDeployPackages = selected && plugin.DeployPackagesCount > 0;
        State.PluginSelection.ImportUnmanagedDevices = selected && plugin.UnmanagedDevicesCount > 0;
    }
}
