using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Parc;

public partial class Global : ComponentBase
{
    private sealed record AssetRow(int Id, string Name, string ItemType, string TypeLabel, string Icon, string Route, DropdownItem? Status, string? Location);

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<AssetRow> _items = [];
    private List<AssetRow> _filtered = [];
    private List<AssetRow> _paged = [];
    private string _search = string.Empty;
    private string _sortField = "name";
    private bool _sortDescending;
    private int _pageSize = 25;
    private int _currentPage = 1;

    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        List<AssetRow> rows = [];

        rows.AddRange((await db.Set<Computer>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Computer", "Ordinateur", "ti-device-desktop", $"/parc/computer/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Peripheral>().AsNoTracking().Include(i => i.StatusItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Peripheral", "Périphérique", "ti-mouse", $"/parc/peripherals/{i.Id}", i.StatusItem, null)));

        rows.AddRange((await db.Set<NetworkEquipment>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "NetworkEquipment", "Matériel réseau", "ti-router", $"/parc/network-equipments/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Printer>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Printer", "Imprimante", "ti-printer", $"/parc/printers/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Phone>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Phone", "Téléphone", "ti-phone", $"/parc/phones/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Rack>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Rack", "Baie", "ti-server-2", $"/parc/racks/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Enclosure>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Enclosure", "Châssis", "ti-layout-grid", $"/parc/enclosures/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<Pdu>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "Pdu", "PDU", "ti-plug", $"/parc/pdus/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        rows.AddRange((await db.Set<PassiveEquipment>().AsNoTracking().Include(i => i.StatusItem).Include(i => i.LocationItem).ToListAsync())
            .Select(i => new AssetRow(i.Id, i.Name, "PassiveEquipment", "Équipement passif", "ti-plug-connected", $"/parc/passive-equipments/{i.Id}", i.StatusItem, i.LocationItem?.Name)));

        _items = rows;
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void OnSearchInput(string? value)
    {
        _search = value ?? string.Empty;
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void SetSort(string field)
    {
        if (_sortField == field) _sortDescending = !_sortDescending;
        else { _sortField = field; _sortDescending = false; }
        ApplyFilterAndSort();
    }

    private MarkupString SortIndicator(string field)
    {
        if (_sortField != field) return new MarkupString(string.Empty);
        return new MarkupString($"<i class=\"ti {(_sortDescending ? "ti-caret-up-filled" : "ti-caret-down-filled")}\"></i>");
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<AssetRow> matched = _items;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string term = _search.Trim();
            matched = matched.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.TypeLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Status?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Location?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Func<AssetRow, IComparable> keySelector = _sortField switch
        {
            "type" => r => r.TypeLabel,
            "status" => r => r.Status?.Name ?? string.Empty,
            "location" => r => r.Location ?? string.Empty,
            _ => r => r.Name
        };

        _filtered = (_sortDescending ? matched.OrderByDescending(keySelector) : matched.OrderBy(keySelector)).ToList();
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _paged = _filtered.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
    }

    private void SetPageSize(int pageSize)
    {
        if (_pageSize == pageSize) return;
        _pageSize = pageSize;
        _currentPage = 1;
        ApplyPaging();
    }

    private void GoToPage(int page)
    {
        int target = Math.Clamp(page, 1, TotalPages);
        if (target == _currentPage) return;
        _currentPage = target;
        ApplyPaging();
    }
}
