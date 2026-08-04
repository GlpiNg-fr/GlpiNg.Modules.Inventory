using System.Security.Claims;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.SavedSearches;

public partial class Index : ComponentBase
{
    private enum SortField
    {
        Name,
        ItemType,
        Owner,
        ResultCount,
        CreatedAt
    }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200];

    private List<SavedSearch> _savedSearches = [];
    private List<SavedSearch> _filteredSavedSearches = [];
    private List<SavedSearch> _pagedSavedSearches = [];
    private string _searchTerm = string.Empty;
    private string _itemTypeFilter = string.Empty;
    private SortField _sortField = SortField.Name;
    private bool _sortDescending;
    private int _pageSize = 25;
    private int _currentPage = 1;
    private int? _currentUserId;

    private int TotalPages => _filteredSavedSearches.Count == 0 ? 1 : (int)Math.Ceiling(_filteredSavedSearches.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync()
    {
        if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            string? userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _currentUserId = int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _savedSearches = await db.Set<SavedSearch>()
            .AsNoTracking()
            .Where(saved => saved.OwnerUserId == _currentUserId || saved.IsPublic)
            .ToListAsync();

        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private async Task OnRefreshAsync() => await LoadAsync();

    private void OnSearchChanged(KeyboardEventArgs args) => ApplySearch();

    private void ApplySearch()
    {
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void SetItemTypeFilter(string itemType)
    {
        _itemTypeFilter = itemType;
        ApplySearch();
    }

    private void ApplyFilterAndSort()
    {
        string term = _searchTerm.Trim();

        IEnumerable<SavedSearch> query = _savedSearches;

        if (term.Length > 0)
        {
            query = query.Where(saved => saved.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (_itemTypeFilter.Length > 0)
        {
            query = query.Where(saved => saved.ItemType == _itemTypeFilter);
        }

        Func<SavedSearch, IComparable> sortKey = _sortField switch
        {
            SortField.ItemType => saved => SavedSearchItemTypes.LabelFor(saved.ItemType),
            SortField.Owner => saved => saved.OwnerName ?? string.Empty,
            SortField.ResultCount => saved => saved.ResultCount,
            SortField.CreatedAt => saved => saved.CreatedAt,
            _ => saved => saved.Name
        };

        query = _sortDescending ? query.OrderByDescending(sortKey) : query.OrderBy(sortKey);

        _filteredSavedSearches = query.ToList();
        ApplyPaging();
    }

    private void SetSort(SortField field)
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

    private void SetPageSize(int pageSize)
    {
        if (_pageSize == pageSize) return;

        _pageSize = pageSize;
        _currentPage = 1;
        ApplyPaging();
    }

    private void GoToPage(int page)
    {
        int targetPage = Math.Clamp(page, 1, TotalPages);
        if (targetPage == _currentPage) return;

        _currentPage = targetPage;
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _pagedSavedSearches = _filteredSavedSearches
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    private async Task SetDefaultAsync(SavedSearch saved)
    {
        if (saved.OwnerUserId != _currentUserId) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<SavedSearch> ownSearchesForItemType = await db.Set<SavedSearch>()
            .Where(s => s.OwnerUserId == _currentUserId && s.ItemType == saved.ItemType)
            .ToListAsync();

        foreach (SavedSearch ownSearch in ownSearchesForItemType)
        {
            ownSearch.IsDefault = ownSearch.Id == saved.Id;
        }

        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
