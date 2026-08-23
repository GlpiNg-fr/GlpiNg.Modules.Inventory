using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.ConsumableItems;

public partial class Index : ComponentBase
{
    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<ConsumableItem> _items = [];
    private List<ConsumableItem> _filtered = [];
    private List<ConsumableItem> _paged = [];
    private readonly HashSet<int> _selectedIds = [];
    private string _search = string.Empty;
    private string _sortField = "name";
    private bool _sortDescending;
    private int _pageSize = 25;
    private int _currentPage = 1;
    private ConsumableItem _newItem = NewBlank();
    private List<DropdownItem> _locationOptions = [];

    private bool AllSelected => _paged.Count > 0 && _selectedIds.IsSupersetOf(_paged.Select(item => item.Id));
    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<ConsumableItem>()
            .AsNoTracking()
            .Include(item => item.LocationItem)
            .Include(item => item.Consumables)
            .OrderBy(item => item.Name)
            .ToListAsync();

        _locationOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Location).OrderBy(i => i.Name).ToListAsync();

        _selectedIds.Clear();
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private static int AvailableStock(ConsumableItem item) => item.Consumables.Count(c => c.DateOut is null);

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
        IEnumerable<ConsumableItem> matched = _items;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string term = _search.Trim();
            matched = matched.Where(item =>
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.Type?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.Manufacturer?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.Reference?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Func<ConsumableItem, IComparable> keySelector = _sortField switch
        {
            "type" => item => item.Type ?? string.Empty,
            "manufacturer" => item => item.Manufacturer ?? string.Empty,
            "location" => item => item.LocationItem?.Name ?? string.Empty,
            "stock" => item => AvailableStock(item),
            _ => item => item.Name
        };

        _filtered = (_sortDescending ? matched.OrderByDescending(keySelector) : matched.OrderBy(keySelector)).ToList();
        _selectedIds.IntersectWith(_filtered.Select(item => item.Id));
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

    private void ToggleSelectAll(bool selectAll)
    {
        foreach (ConsumableItem item in _paged)
        {
            if (selectAll) _selectedIds.Add(item.Id);
            else _selectedIds.Remove(item.Id);
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
        List<ConsumableItem> toDelete = await db.Set<ConsumableItem>().Where(item => _selectedIds.Contains(item.Id)).ToListAsync();
        db.Set<ConsumableItem>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<ConsumableItem>().Add(_newItem);
        await db.SaveChangesAsync();

        _newItem = NewBlank();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newItemModal");
        await LoadAsync();
    }

    private static ConsumableItem NewBlank() => new() { Name = string.Empty };
}
