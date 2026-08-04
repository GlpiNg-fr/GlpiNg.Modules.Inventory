using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Software;

public partial class Index : ComponentBase
{
    private sealed record SoftwareInstall(int ComputerId, string ComputerName, string? Version, string? InstallDate);

    private sealed record SoftwareGroup(string Name, string? Publisher, List<SoftwareInstall> Installs)
    {
        public int InstallCount => Installs.Count;
        public int VersionCount => Installs.Select(install => install.Version ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<SoftwareGroup> _groups = [];
    private List<SoftwareGroup> _filteredGroups = [];
    private readonly HashSet<string> _expandedKeys = [];
    private string _searchTerm = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<Computer> computers = await db.Set<Computer>()
            .AsNoTracking()
            .Include(computer => computer.Softwares)
            .ToListAsync();

        _groups = computers
            .SelectMany(computer => computer.Softwares.Select(software => (Computer: computer, Software: software)))
            .GroupBy(entry => (entry.Software.Name, entry.Software.Publisher))
            .Select(group => new SoftwareGroup(
                group.Key.Name,
                group.Key.Publisher,
                group.Select(entry => new SoftwareInstall(entry.Computer.Id, entry.Computer.Name, entry.Software.Version, entry.Software.InstallDate))
                    .OrderBy(install => install.ComputerName, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _filteredGroups = _groups;
    }

    private void OnSearchChanged(KeyboardEventArgs args)
    {
        string term = _searchTerm.Trim();

        _filteredGroups = term.Length == 0
            ? _groups
            : _groups.Where(group => MatchesSearch(group, term)).ToList();
    }

    private static bool MatchesSearch(SoftwareGroup group, string term)
    {
        return group.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (group.Publisher?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || group.Installs.Any(install =>
                install.ComputerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (install.Version?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static string GroupKey(SoftwareGroup group) => $"{group.Name}|{group.Publisher}";

    private void ToggleGroup(string key)
    {
        if (!_expandedKeys.Add(key))
        {
            _expandedKeys.Remove(key);
        }
    }
}
