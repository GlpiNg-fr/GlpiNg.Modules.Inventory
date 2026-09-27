using GlpiNg.Modules.Abstractions.Localization;
﻿using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory.Components.Pages.Printers;

public partial class Detail : ComponentBase, IAsyncDisposable
{
    private sealed record Snapshot(
        string Name, int? StatusId, int? LocationId, string? Type, string? Manufacturer, string? Model,
        string? SerialNumber, string? InventoryNumber, string? Uuid, string? TechnicianInCharge,
        string? AssignedUser, int? InitialPageCount, int? CurrentPageCount, string? Comment,
        string? IpAddress, PrinterSnmpVersion SnmpVersion, int SnmpPort, string? SnmpCommunity,
        string? SnmpUsername, PrinterSnmpAuthProtocol SnmpAuthProtocol, PrinterSnmpPrivProtocol SnmpPrivProtocol,
        bool HasAuthPassphrase, bool HasPrivPassphrase);

    [Parameter]
    public int ItemId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private DbContext? _db;
    private Printer? _item;
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

    /// <summary>Exemplaires de cartouche passés par cette imprimante, en service comme retirés.</summary>
    private List<Cartridge> _cartridges = [];

    /// <summary>Références ayant au moins une unité en stock : les seules installables.</summary>
    private List<CartridgeItem> _installableItems = [];

    private int _itemToInstall;
    private string? _cartridgeError;

    private IEnumerable<Cartridge> InstalledCartridges => _cartridges.Where(c => c.DateOut is null);
    private IEnumerable<Cartridge> PastCartridges => _cartridges.Where(c => c.DateOut is not null);

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

        _item = await _db.Set<Printer>()
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

        await LoadCartridgesAsync();

        _total = await _db.Set<Printer>().AsNoTracking().CountAsync();
        _position = await _db.Set<Printer>().AsNoTracking().CountAsync(item => item.Id <= ItemId);
        _previousId = await _db.Set<Printer>().AsNoTracking().Where(item => item.Id < ItemId).OrderByDescending(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
        _nextId = await _db.Set<Printer>().AsNoTracking().Where(item => item.Id > ItemId).OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefaultAsync();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task LoadCartridgesAsync()
    {
        if (_db is null)
        {
            return;
        }

        _cartridges = await _db.Set<Cartridge>()
            .Include(cartridge => cartridge.CartridgeItem)
            .Where(cartridge => cartridge.PrinterId == ItemId)
            .OrderByDescending(cartridge => cartridge.DateUse)
            .ToListAsync();

        // Une référence n'est proposée que si elle a une unité disponible : proposer d'installer
        // ce qui n'est pas en stock reviendrait à créer l'unité au passage, ce que la gestion des
        // consommables ne doit pas faire dans le dos du magasinier.
        _installableItems = await _db.Set<CartridgeItem>()
            .Where(item => item.Cartridges.Any(unit => unit.DateUse == null && unit.DateOut == null))
            .OrderBy(item => item.Name)
            .ToListAsync();
    }

    private int AvailableStock(CartridgeItem item) =>
        item.Cartridges.Count(unit => unit.DateUse is null && unit.DateOut is null);

    /// <summary>
    /// Installe une unité en stock de la référence choisie dans cette imprimante.
    ///
    /// L'unité la plus anciennement reçue part la première : un consommable se périme, et laisser
    /// vieillir le fond de stock pendant qu'on entame les arrivages est précisément ce qu'un suivi
    /// d'exemplaires doit éviter.
    ///
    /// La trace est écrite des deux côtés — historique de l'imprimante et de la référence — parce
    /// que les deux questions se posent : « qu'a-t-on mis dans cette imprimante » et « où sont
    /// parties les unités de cette référence ».
    /// </summary>
    private async Task InstallCartridgeAsync()
    {
        _cartridgeError = null;

        if (_db is null || _item is null || _itemToInstall == 0)
        {
            return;
        }

        Cartridge? available = await _db.Set<Cartridge>()
            .Include(cartridge => cartridge.CartridgeItem)
            .Where(cartridge => cartridge.CartridgeItemId == _itemToInstall
                                && cartridge.DateUse == null
                                && cartridge.DateOut == null)
            .OrderBy(cartridge => cartridge.DateIn)
            .FirstOrDefaultAsync();

        if (available is null)
        {
            _cartridgeError = Tr.T("Plus aucune unité de cette référence n'est en stock.");
            await LoadCartridgesAsync();
            return;
        }

        available.PrinterId = _item.Id;
        available.DateUse = DateTime.UtcNow;

        string reference = available.CartridgeItem?.Name ?? $"#{available.CartridgeItemId}";

        _db.Set<PrinterHistoryEntry>().Add(new PrinterHistoryEntry
        {
            PrinterId = _item.Id,
            User = _currentUserName,
            Field = "Cartouche",
            Description = $"Cartouche « {reference} » (unité #{available.Id}) installée",
        });

        _db.Set<CartridgeItemHistoryEntry>().Add(new CartridgeItemHistoryEntry
        {
            CartridgeItemId = available.CartridgeItemId,
            User = _currentUserName,
            Field = "Stock",
            Description = $"Unité #{available.Id} mise en service sur {_item.Name}",
        });

        await _db.SaveChangesAsync();

        _itemToInstall = 0;
        await LoadCartridgesAsync();
        await _db.Entry(_item).Collection(printer => printer.HistoryEntries).LoadAsync();
    }

    private async Task RemoveCartridgeAsync(Cartridge cartridge)
    {
        if (_db is null || _item is null)
        {
            return;
        }

        cartridge.DateOut = DateTime.UtcNow;

        string reference = cartridge.CartridgeItem?.Name ?? $"#{cartridge.CartridgeItemId}";

        _db.Set<PrinterHistoryEntry>().Add(new PrinterHistoryEntry
        {
            PrinterId = _item.Id,
            User = _currentUserName,
            Field = "Cartouche",
            Description = $"Cartouche « {reference} » (unité #{cartridge.Id}) retirée",
        });

        _db.Set<CartridgeItemHistoryEntry>().Add(new CartridgeItemHistoryEntry
        {
            CartridgeItemId = cartridge.CartridgeItemId,
            User = _currentUserName,
            Field = "Stock",
            Description = $"Unité #{cartridge.Id} retirée de {_item.Name}",
        });

        await _db.SaveChangesAsync();

        await LoadCartridgesAsync();
        await _db.Entry(_item).Collection(printer => printer.HistoryEntries).LoadAsync();
    }

    /// <summary>
    /// Les phrases secrètes n'entrent dans l'instantané que sous forme de « renseignée ou non » :
    /// l'historique dirait sinon en clair, et pour toujours, un secret que la fiche elle-même
    /// masque à l'écran.
    /// </summary>
    private static Snapshot ToSnapshot(Printer i) => new(
        i.Name, i.StatusId, i.LocationId, i.Type, i.Manufacturer, i.Model,
        i.SerialNumber, i.InventoryNumber, i.Uuid, i.TechnicianInCharge,
        i.AssignedUser, i.InitialPageCount, i.CurrentPageCount, i.Comment,
        i.IpAddress, i.SnmpVersion, i.SnmpPort, i.SnmpCommunity,
        i.SnmpUsername, i.SnmpAuthProtocol, i.SnmpPrivProtocol,
        !string.IsNullOrEmpty(i.SnmpAuthPassphrase), !string.IsNullOrEmpty(i.SnmpPrivPassphrase));

    private string StatusLabel(int? statusId) => statusId is { } id ? _statusOptions.FirstOrDefault(s => s.Id == id)?.Name ?? "—" : "—";
    private string LocationLabel(int? locationId) => locationId is { } id ? _locationOptions.FirstOrDefault(l => l.Id == id)?.Name ?? "—" : "—";

    private static string VersionLabel(PrinterSnmpVersion version) => version switch
    {
        PrinterSnmpVersion.None => "aucune",
        PrinterSnmpVersion.V2c => "v2c",
        _ => version.ToString().ToLowerInvariant(),
    };

    /// <summary>Une communauté est un secret partagé : sa valeur n'a pas sa place dans un journal
    /// que tout lecteur de la fiche peut consulter.</summary>
    private static string Secret(string? value) => string.IsNullOrEmpty(value) ? "vide" : Tr.T("renseignée");

    private static string Presence(bool present) => present ? Tr.T("renseignée") : "vide";

    private static string FormatChange(string? oldValue, string? newValue) =>
        $"{(string.IsNullOrEmpty(oldValue) ? "vide" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "vide" : newValue)}";

    private IEnumerable<(string Field, string? Old, string? New)> DiffFields(Snapshot before, Snapshot after)
    {
        if (before.Name != after.Name) yield return ("Nom", before.Name, after.Name);
        if (before.StatusId != after.StatusId) yield return ("Statut", StatusLabel(before.StatusId), StatusLabel(after.StatusId));
        if (before.LocationId != after.LocationId) yield return ("Lieu", LocationLabel(before.LocationId), LocationLabel(after.LocationId));
        if (before.Type != after.Type) yield return ("Type", before.Type, after.Type);
        if (before.Manufacturer != after.Manufacturer) yield return ("Fabricant", before.Manufacturer, after.Manufacturer);
        if (before.Model != after.Model) yield return ("Modèle", before.Model, after.Model);
        if (before.SerialNumber != after.SerialNumber) yield return ("Numéro de série", before.SerialNumber, after.SerialNumber);
        if (before.InventoryNumber != after.InventoryNumber) yield return ("Numéro d'inventaire", before.InventoryNumber, after.InventoryNumber);
        if (before.Uuid != after.Uuid) yield return ("UUID", before.Uuid, after.Uuid);
        if (before.TechnicianInCharge != after.TechnicianInCharge) yield return ("Technicien responsable", before.TechnicianInCharge, after.TechnicianInCharge);
        if (before.AssignedUser != after.AssignedUser) yield return ("Utilisateur", before.AssignedUser, after.AssignedUser);
        if (before.InitialPageCount != after.InitialPageCount) yield return ("Compteur de page initial", before.InitialPageCount?.ToString(), after.InitialPageCount?.ToString());
        if (before.CurrentPageCount != after.CurrentPageCount) yield return ("Compteur de page actuel", before.CurrentPageCount?.ToString(), after.CurrentPageCount?.ToString());
        if (before.Comment != after.Comment) yield return ("Commentaires", before.Comment, after.Comment);
        if (before.IpAddress != after.IpAddress) yield return ("Adresse IP", before.IpAddress, after.IpAddress);
        if (before.SnmpVersion != after.SnmpVersion) yield return ("Version SNMP", VersionLabel(before.SnmpVersion), VersionLabel(after.SnmpVersion));
        if (before.SnmpPort != after.SnmpPort) yield return ("Port SNMP", before.SnmpPort.ToString(), after.SnmpPort.ToString());
        if (before.SnmpCommunity != after.SnmpCommunity) yield return ("Communauté SNMP", Secret(before.SnmpCommunity), Secret(after.SnmpCommunity));
        if (before.SnmpUsername != after.SnmpUsername) yield return ("Nom de sécurité SNMP", before.SnmpUsername, after.SnmpUsername);
        if (before.SnmpAuthProtocol != after.SnmpAuthProtocol) yield return ("Authentification SNMP", before.SnmpAuthProtocol.ToString(), after.SnmpAuthProtocol.ToString());
        if (before.SnmpPrivProtocol != after.SnmpPrivProtocol) yield return ("Chiffrement SNMP", before.SnmpPrivProtocol.ToString(), after.SnmpPrivProtocol.ToString());
        if (before.HasAuthPassphrase != after.HasAuthPassphrase) yield return ("Phrase d'authentification SNMP", Presence(before.HasAuthPassphrase), Presence(after.HasAuthPassphrase));
        if (before.HasPrivPassphrase != after.HasPrivPassphrase) yield return ("Phrase de chiffrement SNMP", Presence(before.HasPrivPassphrase), Presence(after.HasPrivPassphrase));
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
            List<PrinterHistoryEntry> entries = DiffFields(_beforeEdit, after)
                .Select(diff => new PrinterHistoryEntry
                {
                    PrinterId = _item.Id,
                    User = _currentUserName,
                    Field = diff.Field,
                    Description = FormatChange(diff.Old, diff.New)
                })
                .ToList();

            _item.UpdatedAt = DateTime.UtcNow;
            if (entries.Count > 0)
            {
                _db.Set<PrinterHistoryEntry>().AddRange(entries);
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

        _db.Set<Printer>().Remove(_item);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/parc/printers");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
