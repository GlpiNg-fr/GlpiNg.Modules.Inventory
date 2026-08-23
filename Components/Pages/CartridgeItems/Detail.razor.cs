using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.CartridgeItems;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record Snapshot(
        string Name, string? Type, string? Manufacturer, string? Reference, int? LocationId,
        string? TechnicianInCharge, int AlertThreshold, string? Comment);

    [Parameter]
    public int ItemId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private CartridgeItem? _item;
    private List<DropdownItem> _locationOptions = [];
    private List<Printer> _printers = [];
    private string _activeTabKey = "main";
    private bool _isSaving;
    private string _currentUserName = "Système";
    private Snapshot _beforeEdit = null!;
    private int _loadedItemId;
    private int _receiveQuantity = 1;
    private readonly Dictionary<int, int> _printerToUse = [];

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

        _item = await _db.Set<CartridgeItem>()
            .Include(item => item.LocationItem)
            .Include(item => item.Cartridges).ThenInclude(c => c.Printer)
            .Include(item => item.HistoryEntries)
            .FirstOrDefaultAsync(item => item.Id == ItemId);

        if (_item is null)
        {
            return;
        }

        _locationOptions = await _db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Location).OrderBy(i => i.Name).ToListAsync();
        _printers = await _db.Set<Printer>().AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        _beforeEdit = ToSnapshot(_item);
    }

    private void SetTab(string key) => _activeTabKey = key;

    private static Snapshot ToSnapshot(CartridgeItem i) => new(
        i.Name, i.Type, i.Manufacturer, i.Reference, i.LocationId, i.TechnicianInCharge, i.AlertThreshold, i.Comment);

    private string LocationLabel(int? locationId) => locationId is { } id ? _locationOptions.FirstOrDefault(l => l.Id == id)?.Name ?? "—" : "—";

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(Snapshot before, Snapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Manufacturer != after.Manufacturer) yield return ("Fabricant", before.Manufacturer, after.Manufacturer);
        if (before.Reference != after.Reference) yield return ("Référence", before.Reference, after.Reference);
        if (before.LocationId != after.LocationId) yield return ("Lieu", LocationLabel(before.LocationId), LocationLabel(after.LocationId));
        if (before.TechnicianInCharge != after.TechnicianInCharge) yield return ("Technicien responsable", before.TechnicianInCharge, after.TechnicianInCharge);
        if (before.AlertThreshold != after.AlertThreshold) yield return ("Seuil d'alerte", before.AlertThreshold.ToString(), after.AlertThreshold.ToString());
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
            List<CartridgeItemHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new CartridgeItemHistoryEntry
                {
                    CartridgeItemId = _item.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _item.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<CartridgeItemHistoryEntry>().AddRange(entries);
            }

            await _db.SaveChangesAsync();
            _beforeEdit = after;
            await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
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

        _db.Set<CartridgeItem>().Remove(_item);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/cartridge-items");
    }

    private async Task ReceiveStockAsync()
    {
        if (_db is null || _item is null || _receiveQuantity < 1)
        {
            return;
        }

        for (int i = 0; i < _receiveQuantity; i++)
        {
            _item.Cartridges.Add(new Cartridge { CartridgeItemId = _item.Id, DateIn = DateTime.UtcNow });
        }

        _db.Set<CartridgeItemHistoryEntry>().Add(new CartridgeItemHistoryEntry
        {
            CartridgeItemId = _item.Id,
            User = _currentUserName,
            Field = "Stock",
            Description = $"Réception de {_receiveQuantity} unité(s)"
        });

        await _db.SaveChangesAsync();
        await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
        _receiveQuantity = 1;
    }

    private async Task PutIntoUseAsync(Cartridge cartridge)
    {
        if (_db is null || _item is null) return;
        if (!_printerToUse.TryGetValue(cartridge.Id, out int printerId) || printerId == 0) return;

        cartridge.PrinterId = printerId;
        cartridge.DateUse = DateTime.UtcNow;

        _db.Set<CartridgeItemHistoryEntry>().Add(new CartridgeItemHistoryEntry
        {
            CartridgeItemId = _item.Id,
            User = _currentUserName,
            Field = "Stock",
            Description = $"Unité #{cartridge.Id} mise en service sur {_printers.FirstOrDefault(p => p.Id == printerId)?.Name ?? "?"}"
        });

        await _db.SaveChangesAsync();
        await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
        await _db.Entry(cartridge).Reference(c => c.Printer).LoadAsync();
    }

    private async Task RemoveFromServiceAsync(Cartridge cartridge)
    {
        if (_db is null || _item is null) return;

        cartridge.DateOut = DateTime.UtcNow;

        _db.Set<CartridgeItemHistoryEntry>().Add(new CartridgeItemHistoryEntry
        {
            CartridgeItemId = _item.Id,
            User = _currentUserName,
            Field = "Stock",
            Description = $"Unité #{cartridge.Id} retirée"
        });

        await _db.SaveChangesAsync();
        await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
