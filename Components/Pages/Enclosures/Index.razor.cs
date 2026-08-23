using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Enclosures;

public partial class Index : ComponentBase
{
    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<Enclosure> _items = [];
    private List<Enclosure> _filtered = [];
    private List<Enclosure> _paged = [];
    private readonly HashSet<int> _selectedIds = [];
    private string _search = string.Empty;
    private string _sortField = "name";
    private bool _sortDescending;
    private int _pageSize = 25;
    private int _currentPage = 1;
    private Enclosure _newItem = NewBlank();
    private List<DropdownItem> _statusOptions = [];
    private List<DropdownItem> _locationOptions = [];

    private bool AllSelected => _paged.Count > 0 && _selectedIds.IsSupersetOf(_paged.Select(item => item.Id));
    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<Enclosure>()
            .AsNoTracking()
            .Include(item => item.StatusItem)
            .Include(item => item.LocationItem)
            .OrderBy(item => item.Name)
            .ToListAsync();

        _statusOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Status).OrderBy(i => i.Name).ToListAsync();
        _locationOptions = await db.Set<DropdownItem>().AsNoTracking().Where(i => i.Type == DropdownType.Location).OrderBy(i => i.Name).ToListAsync();

        _selectedIds.Clear();
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
        if (_sortField == field)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortField = field;
            _sortDescending = false;
        }

        ApplyFilterAndSort();
    }

    private MarkupString SortIndicator(string field)
    {
        if (_sortField != field) return new MarkupString(string.Empty);
        return new MarkupString($"<i class=\"ti {(_sortDescending ? "ti-caret-up-filled" : "ti-caret-down-filled")}\"></i>");
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<Enclosure> matched = _items;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string term = _search.Trim();
            matched = matched.Where(item =>
                (item.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (item.Type?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.Manufacturer?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.Model?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.SerialNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Func<Enclosure, IComparable> keySelector = _sortField switch
        {
            "status" => item => item.StatusItem?.Name ?? string.Empty,
            "manufacturer" => item => item.Manufacturer ?? string.Empty,
            "location" => item => item.LocationItem?.Name ?? string.Empty,
            "type" => item => item.Type ?? string.Empty,
            "model" => item => item.Model ?? string.Empty,
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
        foreach (Enclosure item in _paged)
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
        List<Enclosure> toDelete = await db.Set<Enclosure>().Where(item => _selectedIds.Contains(item.Id)).ToListAsync();
        db.Set<Enclosure>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<Enclosure>().Add(_newItem);
        await db.SaveChangesAsync();

        _newItem = NewBlank();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newItemModal");
        await LoadAsync();
    }

    private static Enclosure NewBlank() => new() { Name = string.Empty };
}
