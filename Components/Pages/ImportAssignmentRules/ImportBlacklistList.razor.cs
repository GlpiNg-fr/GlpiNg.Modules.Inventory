using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.ImportAssignmentRules;

public partial class ImportBlacklistList : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<ImportBlacklistEntry> _entries = [];
    private readonly HashSet<int> _selectedIds = [];
    private ImportBlacklistEntry _newEntry = new() { Name = string.Empty, Value = string.Empty };

    private bool AllSelected => _entries.Count > 0 && _selectedIds.Count == _entries.Count;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _entries = await db.Set<ImportBlacklistEntry>().AsNoTracking().OrderBy(e => e.Type).ThenBy(e => e.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (ImportBlacklistEntry entry in _entries)
            {
                _selectedIds.Add(entry.Id);
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
        List<ImportBlacklistEntry> toDelete = await db.Set<ImportBlacklistEntry>().Where(e => _selectedIds.Contains(e.Id)).ToListAsync();
        db.Set<ImportBlacklistEntry>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newEntry.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<ImportBlacklistEntry>().Add(_newEntry);
        await db.SaveChangesAsync();

        // Nouvelle instance plutôt que réutiliser _newEntry (voir DropdownList.razor.cs.CreateAsync
        // pour la même raison : éviter un futur INSERT avec un Id déjà pris).
        _newEntry = new ImportBlacklistEntry { Name = string.Empty, Value = string.Empty };

        await JS.InvokeVoidAsync("glping.hideModal", "newImportBlacklistEntryModal");
        await LoadAsync();
    }
}
