using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Cables;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record Snapshot(
        string Name, int? StatusId, string? Type, string? Color, string? Comment,
        CableEndpointType EndpointAType, int EndpointAId, CableEndpointType? EndpointBType, int? EndpointBId);

    [Parameter]
    public int ItemId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private Cable? _item;
    private List<DropdownItem> _statusOptions = [];
    private Dictionary<(CableEndpointType Type, int Id), string> _endpointNames = [];
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

        _item = await _db.Set<Cable>()
            .Include(item => item.HistoryEntries)
            .Include(item => item.StatusItem)
            .FirstOrDefaultAsync(item => item.Id == ItemId);

        if (_item is null)
        {
            return;
        }

        _statusOptions = await _db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Status).OrderBy(i => i.Name).ToListAsync();

        List<(CableEndpointType Type, int Id)> keys = [(_item.EndpointAType, _item.EndpointAId)];
        if (_item.EndpointBType is { } typeB && _item.EndpointBId is { } idB)
        {
            keys.Add((typeB, idB));
        }
        _endpointNames = await CableEndpointCatalog.ResolveNamesAsync(_db, keys);

        _beforeEdit = ToSnapshot(_item);

        _total = await _db.Set<Cable>().AsNoTracking().CountAsync();
        _position = await _db.Set<Cable>().AsNoTracking().CountAsync(item => item.Id <= ItemId);
        _previousId = await _db.Set<Cable>().AsNoTracking().Where(item => item.Id < ItemId).OrderByDescending(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
        _nextId = await _db.Set<Cable>().AsNoTracking().Where(item => item.Id > ItemId).OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private string EndpointLabel(CableEndpointType type, int id) => _endpointNames.TryGetValue((type, id), out string? name) ? name : $"#{id}";

    private static Snapshot ToSnapshot(Cable i) => new(
        i.Name, i.StatusId, i.Type, i.Color, i.Comment, i.EndpointAType, i.EndpointAId, i.EndpointBType, i.EndpointBId);

    private string StatusLabel(int? statusId) => statusId is { } id ? _statusOptions.FirstOrDefault(s => s.Id == id)?.Name ?? "—" : "—";

    private string EndpointFieldLabel(CableEndpointType? type, int? id) =>
        type is { } t && id is { } i ? $"{CableEndpointCatalog.DisplayName(t)} — {EndpointLabel(t, i)}" : "vide";

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(Snapshot before, Snapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.StatusId != after.StatusId) yield return ("Statut", StatusLabel(before.StatusId), StatusLabel(after.StatusId));
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Color != after.Color) yield return ("Couleur", before.Color, after.Color);
        if (before.EndpointAType != after.EndpointAType || before.EndpointAId != after.EndpointAId)
            yield return ("Extrémité A", EndpointFieldLabel(before.EndpointAType, before.EndpointAId), EndpointFieldLabel(after.EndpointAType, after.EndpointAId));
        if (before.EndpointBType != after.EndpointBType || before.EndpointBId != after.EndpointBId)
            yield return ("Extrémité B", EndpointFieldLabel(before.EndpointBType, before.EndpointBId), EndpointFieldLabel(after.EndpointBType, after.EndpointBId));
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
            List<CableHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new CableHistoryEntry
                {
                    CableId = _item.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _item.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<CableHistoryEntry>().AddRange(entries);
            }

            await _db.SaveChangesAsync();
            _beforeEdit = after;
            await _db.Entry(_item).Collection(i => i.HistoryEntries).LoadAsync();
            await _db.Entry(_item).Reference(i => i.StatusItem).LoadAsync();

            List<(CableEndpointType Type, int Id)> keys = [(_item.EndpointAType, _item.EndpointAId)];
            if (_item.EndpointBType is { } typeB && _item.EndpointBId is { } idB)
            {
                keys.Add((typeB, idB));
            }
            _endpointNames = await CableEndpointCatalog.ResolveNamesAsync(_db, keys);
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

        _db.Set<Cable>().Remove(_item);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/cables");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
