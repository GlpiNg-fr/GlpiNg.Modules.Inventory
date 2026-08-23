using GlpiNg.Modules.Inventory.Import;
using Microsoft.AspNetCore.Components;

namespace GlpiNg.Modules.Inventory.Components.Pages.Import;

public partial class GlpiMySqlImportPage : ComponentBase, IDisposable
{
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
}
