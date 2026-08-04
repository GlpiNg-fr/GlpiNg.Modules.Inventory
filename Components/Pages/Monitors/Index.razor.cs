using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Monitors;

public partial class Index : ComponentBase
{
    private sealed record MonitorRow(int ComputerId, string ComputerName, string Designation, string? Manufacturer, string? Serial);

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<MonitorRow> _monitors = [];
    private List<MonitorRow> _filteredMonitors = [];
    private string _searchTerm = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _monitors = await db.Set<ComputerPeripheral>()
            .AsNoTracking()
            .Where(peripheral => peripheral.Kind == PeripheralKind.Monitor)
            .Join(db.Set<Computer>().AsNoTracking(),
                peripheral => peripheral.ComputerId,
                computer => computer.Id,
                (peripheral, computer) => new { peripheral, computer })
            .OrderBy(joined => joined.computer.Name)
            .ThenBy(joined => joined.peripheral.Designation)
            .Select(joined => new MonitorRow(joined.computer.Id, joined.computer.Name, joined.peripheral.Designation, joined.peripheral.Manufacturer, joined.peripheral.Serial))
            .ToListAsync();

        _filteredMonitors = _monitors;
    }

    private void OnSearchChanged(KeyboardEventArgs args)
    {
        string term = _searchTerm.Trim();

        _filteredMonitors = term.Length == 0
            ? _monitors
            : _monitors.Where(monitor => MatchesSearch(monitor, term)).ToList();
    }

    private static bool MatchesSearch(MonitorRow monitor, string term)
    {
        return monitor.Designation.Contains(term, StringComparison.OrdinalIgnoreCase)
            || monitor.ComputerName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (monitor.Manufacturer?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (monitor.Serial?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
