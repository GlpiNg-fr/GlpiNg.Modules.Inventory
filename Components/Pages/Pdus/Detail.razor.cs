using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Pdus;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record Snapshot(
        string Name, int? StatusId, int? LocationId, string? Type, string? Manufacturer, string? Model,
        string? SerialNumber, string? InventoryNumber, string? TechnicianInCharge,
        string? AssignedUser, string? Comment);

    [Parameter]
    public int ItemId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private Pdu? _item;
    private List<DropdownItem> _statusOptions = [];
    private List<DropdownItem> _locationOptions = [];
    private string _activeTabKey = "main";
    private bool _isSaving;
    private string _currentUserName = "Système";
    private Snapshot _beforeEdit = null!;
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;
    private int _loadedItemId;

    protected override async Task OnParametersSetAsync()
    {
        if (_item is not null && _loadedItemId == ItemId)
        {
            return;
        }

        if (AuthStateTask is not null)
        {
            var authState = await AuthStateTask;
            string? name = authState.User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _currentUserName = name;
            }
        }

        _loadedItemId = ItemId;
        _activeTabKey = "main";

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();

        _item = await _db.Set<Pdu>()
            .Include(item => item.HistoryEntries)
            .Include(item => item.StatusItem)
            .Include(item => item.LocationItem)
            .FirstOrDefaultAsync(item => item.Id == ItemId);

        if (_item is null)
        {
            return;
        }

        _statusOptions = await _db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Status).OrderBy(i => i.Name).ToListAsync();
        _locationOptions = await _db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Location).OrderBy(i => i.Name).ToListAsync();
        _beforeEdit = ToSnapshot(_item);

        _total = await _db.Set<Pdu>().AsNoTracking().CountAsync();
        _position = await _db.Set<Pdu>().AsNoTracking().CountAsync(item => item.Id <= ItemId);
        _previousId = await _db.Set<Pdu>().AsNoTracking().Where(item => item.Id < ItemId).OrderByDescending(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
        _nextId = await _db.Set<Pdu>().AsNoTracking().Where(item => item.Id > ItemId).OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private static Snapshot ToSnapshot(Pdu i) => new(
        i.Name, i.StatusId, i.LocationId, i.Type, i.Manufacturer, i.Model,
        i.SerialNumber, i.InventoryNumber, i.TechnicianInCharge, i.AssignedUser, i.Comment);

    private string StatusLabel(int? statusId) => statusId is { } id ? _statusOptions.FirstOrDefault(s => s.Id == id)?.Name ?? "—" : "—";
    private string LocationLabel(int? locationId) => locationId is { } id ? _locationOptions.FirstOrDefault(l => l.Id == id)?.Name ?? "—" : "—";

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(Snapshot before, Snapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.StatusId != after.StatusId) yield return ("Statut", StatusLabel(before.StatusId), StatusLabel(after.StatusId));
        if (before.LocationId != after.LocationId) yield return ("Lieu", LocationLabel(before.LocationId), LocationLabel(after.LocationId));
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Manufacturer != after.Manufacturer) yield return ("Fabricant", before.Manufacturer, after.Manufacturer);
        if (before.Model != after.Model) yield return ("Modèle", before.Model, after.Model);
        if (before.SerialNumber != after.SerialNumber) yield return ("Numéro de série", before.SerialNumber, after.SerialNumber);
        if (before.InventoryNumber != after.InventoryNumber) yield return ("Numéro d'inventaire", before.InventoryNumber, after.InventoryNumber);
        if (before.TechnicianInCharge != after.TechnicianInCharge) yield return ("Technicien responsable", before.TechnicianInCharge, after.TechnicianInCharge);
        if (before.AssignedUser != after.AssignedUser) yield return ("Utilisateur", before.AssignedUser, after.AssignedUser);
        if (before.Comment != after.Comment) yield return ("Commentaires", before.Comment, after.Comment);
    }

    private async Task SaveAsync()
    {
        if (_db is null || _item is null)
        {
            return;
        }

        _isSaving = true;
        try
        {
            Snapshot after = ToSnapshot(_item);
            List<PduHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new PduHistoryEntry
                {
                    PduId = _item.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _item.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<PduHistoryEntry>().AddRange(entries);
            }

            await _db.SaveChangesAsync();
            _beforeEdit = after;
            await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
            await _db.Entry(_item).Reference(i => i.StatusItem).LoadAsync();
            await _db.Entry(_item).Reference(i => i.LocationItem).LoadAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _item is null)
        {
            return;
        }

        _db.Set<Pdu>().Remove(_item);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/pdus");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
