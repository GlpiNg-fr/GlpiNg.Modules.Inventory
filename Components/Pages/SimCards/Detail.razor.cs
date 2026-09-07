using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.SimCards;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record Snapshot(
        string Name, int? StatusId, int? LocationId, string? Type, string? Operator,
        string? SerialNumber, string? InventoryNumber, string? PhoneNumber, string? Msin,
        string? Pin, string? Pin2, string? Puk, string? Puk2, string? Voltage, string? Country,
        bool AllowVoip, string? TechnicianInCharge, string? AssignedUser, string? Comment);

    [Parameter]
    public int ItemId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private SimCard? _item;
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

        _item = await _db.Set<SimCard>()
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

        _total = await _db.Set<SimCard>().AsNoTracking().CountAsync();
        _position = await _db.Set<SimCard>().AsNoTracking().CountAsync(item => item.Id <= ItemId);
        _previousId = await _db.Set<SimCard>().AsNoTracking().Where(item => item.Id < ItemId).OrderByDescending(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
        _nextId = await _db.Set<SimCard>().AsNoTracking().Where(item => item.Id > ItemId).OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private static Snapshot ToSnapshot(SimCard i) => new(
        i.Name, i.StatusId, i.LocationId, i.Type, i.Operator, i.SerialNumber, i.InventoryNumber,
        i.PhoneNumber, i.Msin, i.Pin, i.Pin2, i.Puk, i.Puk2, i.Voltage, i.Country, i.AllowVoip,
        i.TechnicianInCharge, i.AssignedUser, i.Comment);

    private string StatusLabel(int? statusId) => statusId is { } id ? _statusOptions.FirstOrDefault(s => s.Id == id)?.Name ?? "—" : "—";
    private string LocationLabel(int? locationId) => locationId is { } id ? _locationOptions.FirstOrDefault(l => l.Id == id)?.Name ?? "—" : "—";

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private static string YesNo(bool value) => value ? "Oui" : "Non";

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(Snapshot before, Snapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.StatusId != after.StatusId) yield return ("Statut", StatusLabel(before.StatusId), StatusLabel(after.StatusId));
        if (before.LocationId != after.LocationId) yield return ("Lieu", LocationLabel(before.LocationId), LocationLabel(after.LocationId));
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Operator != after.Operator) yield return ("Opérateur", before.Operator, after.Operator);
        if (before.SerialNumber != after.SerialNumber) yield return ("ICCID", before.SerialNumber, after.SerialNumber);
        if (before.InventoryNumber != after.InventoryNumber) yield return ("Numéro d'inventaire", before.InventoryNumber, after.InventoryNumber);
        if (before.PhoneNumber != after.PhoneNumber) yield return ("Numéro de ligne", before.PhoneNumber, after.PhoneNumber);
        if (before.Msin != after.Msin) yield return ("MSIN", before.Msin, after.Msin);
        // Codes PIN/PUK : on trace le fait qu'ils ont changé, jamais les valeurs — elles ouvrent
        // la ligne, et l'historique est lisible par tout utilisateur ayant accès à la fiche.
        if (before.Pin != after.Pin) yield return ("PIN", "•••", "•••");
        if (before.Pin2 != after.Pin2) yield return ("PIN 2", "•••", "•••");
        if (before.Puk != after.Puk) yield return ("PUK", "•••", "•••");
        if (before.Puk2 != after.Puk2) yield return ("PUK 2", "•••", "•••");
        if (before.Voltage != after.Voltage) yield return ("Tension", before.Voltage, after.Voltage);
        if (before.Country != after.Country) yield return ("Pays", before.Country, after.Country);
        if (before.AllowVoip != after.AllowVoip) yield return ("Voix sur IP autorisée", YesNo(before.AllowVoip), YesNo(after.AllowVoip));
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
            List<SimCardHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new SimCardHistoryEntry
                {
                    SimCardId = _item.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _item.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<SimCardHistoryEntry>().AddRange(entries);
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

        _db.Set<SimCard>().Remove(_item);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/simcards");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
