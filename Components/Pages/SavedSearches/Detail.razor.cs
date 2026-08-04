using System.Security.Claims;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.SavedSearches;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record FicheTab(string Key, string Icon, string Label);

    [Parameter]
    public int SavedSearchId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private static readonly FicheTab[] Tabs =
    [
        new("savedsearch", "ti-bookmark", "Recherche sauvegardée"),
        new("alerts", "ti-bell", "Alertes de recherches sauvegardées"),
        new("all", "ti-list", "Tous"),
    ];

    private DbContext? _db;
    private SavedSearch? _savedSearch;
    private string _activeTabKey = "savedsearch";
    private bool _isSaving;
    private int? _currentUserId;
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;

    private bool IsOwner => _savedSearch is not null && _savedSearch.OwnerUserId == _currentUserId;

    protected override async Task OnInitializedAsync()
    {
        if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            string? userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _currentUserId = int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        _db = await DbFactory.CreateDbContextAsync();

        _savedSearch = await _db.Set<SavedSearch>().FirstOrDefaultAsync(saved => saved.Id == SavedSearchId);
        if (_savedSearch is null)
        {
            return;
        }

        if (_savedSearch.OwnerUserId != _currentUserId && !_savedSearch.IsPublic)
        {
            _savedSearch = null;
            return;
        }

        IQueryable<SavedSearch> visible = _db.Set<SavedSearch>()
            .AsNoTracking()
            .Where(saved => saved.OwnerUserId == _currentUserId || saved.IsPublic);

        _total = await visible.CountAsync();
        _position = await visible.CountAsync(saved => saved.Id <= SavedSearchId);
        _previousId = await visible
            .Where(saved => saved.Id < SavedSearchId)
            .OrderByDescending(saved => saved.Id)
            .Select(saved => (int?)saved.Id)
            .FirstOrDefaultAsync();
        _nextId = await visible
            .Where(saved => saved.Id > SavedSearchId)
            .OrderBy(saved => saved.Id)
            .Select(saved => (int?)saved.Id)
            .FirstOrDefaultAsync();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _savedSearch is null || !IsOwner) return;

        _isSaving = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _savedSearch is null || !IsOwner) return;

        _db.Set<SavedSearch>().Remove(_savedSearch);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/tools/saved-searches");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
