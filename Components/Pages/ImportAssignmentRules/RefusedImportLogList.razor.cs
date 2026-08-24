using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.ImportAssignmentRules;

public partial class RefusedImportLogList : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<RefusedImportLog> _entries = [];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _entries = await db.Set<RefusedImportLog>()
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync();
    }

    private async Task ClearAllAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        await db.Set<RefusedImportLog>().ExecuteDeleteAsync();
        await LoadAsync();
    }
}
