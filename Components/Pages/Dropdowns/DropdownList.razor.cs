using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Inventory.Components.Pages.Dropdowns;

public partial class DropdownList : ComponentBase
{
    [Parameter]
    public string? TypeSlug { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private DropdownType _type;
    private bool _typeResolved;
    private List<DropdownItem> _items = [];
    private readonly HashSet<int> _selectedIds = [];
    private DropdownItem _newItem = new() { Name = string.Empty };

    private int? _editingId;
    private string _editName = string.Empty;
    private string? _editComment;

    private bool AllSelected => _items.Count > 0 && _selectedIds.Count == _items.Count;

    protected override async Task OnParametersSetAsync()
    {
        // Slug absent (route "/config/dropdowns" sans segment) : bascule sur le premier type par défaut.
        _typeResolved = string.IsNullOrEmpty(TypeSlug)
            ? SetDefaultType()
            : DropdownTypeCatalog.TryParseSlug(TypeSlug, out _type);

        if (!_typeResolved)
        {
            return;
        }

        _newItem = new DropdownItem { Name = string.Empty, Type = _type };
        _editingId = null;
        await LoadAsync();
    }

    private bool SetDefaultType()
    {
        _type = DropdownTypeCatalog.All[0];
        return true;
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<DropdownItem>()
            .AsNoTracking()
            .Where(i => i.Type == _type)
            .OrderBy(i => i.Name)
            .ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DropdownItem item in _items)
            {
                _selectedIds.Add(item.Id);
            }
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
        List<DropdownItem> toDelete = await db.Set<DropdownItem>().Where(i => _selectedIds.Contains(i.Id)).ToListAsync();
        db.Set<DropdownItem>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task DeleteAsync(int id)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DropdownItem? tracked = await db.Set<DropdownItem>().FirstOrDefaultAsync(i => i.Id == id);
        if (tracked is null) return;

        db.Set<DropdownItem>().Remove(tracked);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private void StartEdit(DropdownItem item)
    {
        _editingId = item.Id;
        _editName = item.Name;
        _editComment = item.Comment;
    }

    private void CancelEdit()
    {
        _editingId = null;
    }

    private async Task SaveEditAsync()
    {
        if (_editingId is not int id || string.IsNullOrWhiteSpace(_editName)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DropdownItem? tracked = await db.Set<DropdownItem>().FirstOrDefaultAsync(i => i.Id == id);
        if (tracked is null) return;

        tracked.Name = _editName;
        tracked.Comment = _editComment;
        await db.SaveChangesAsync();

        _editingId = null;
        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItem.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DropdownItem>().Add(_newItem);
        await db.SaveChangesAsync();

        // Nouvelle instance plutôt que de réinitialiser les champs de _newItem en place : une fois
        // enregistré, cet objet porte l'Id généré par la base — le réutiliser tel quel dans un
        // futur Add() sur un DbContext frais ferait tenter un INSERT avec un Id explicite déjà pris
        // (colonne Identity), rejeté par SQL Server (IDENTITY_INSERT OFF).
        _newItem = new DropdownItem { Name = string.Empty, Type = _type };

        await JS.InvokeVoidAsync("glpiNg.hideModal", "newDropdownItemModal");
        await LoadAsync();
    }
}
