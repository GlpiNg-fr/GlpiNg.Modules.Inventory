using GlpiNg.Modules.Abstractions.Cron;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Relève par SNMP ce que les imprimantes exposent d'elles-mêmes : leur compteur de pages, et le
/// niveau restant des cartouches qui y sont installées.
///
/// Une imprimante réseau n'héberge pas d'agent : personne ne remonte son état, alors qu'elle-même
/// le publie. Sans ce relevé, le compteur de pages d'une fiche est celui de la dernière saisie
/// manuelle, et le niveau d'une cartouche n'existe pas — on ne sait qu'elle est vide que le jour
/// où quelqu'un s'en plaint.
///
/// Seules les imprimantes dont la fiche porte une version SNMP et une adresse sont interrogées :
/// c'est ce réglage qui vaut consentement, une sonde sur tout le parc n'en serait pas un.
/// </summary>
public sealed class PrinterSnmpPollCronTask(
    DbContext db,
    PrinterSnmpReader reader,
    ILogger<PrinterSnmpPollCronTask> logger) : ICronTask
{
    /// <summary>
    /// Compteur de pages « vie entière » de la norme Printer-MIB (<c>prtMarkerLifeCount</c>).
    /// Standardisé, donc lisible sans configuration par imprimante — contrairement au niveau des
    /// consommables, dont le rang varie et se configure sur la référence de cartouche.
    /// </summary>
    private const string PageCountOid = "1.3.6.1.2.1.43.10.2.1.4.1.1";

    public string Key => "printer_snmp_poll";

    public string Name => "Relevé SNMP des imprimantes";

    public string Description =>
        "Interroge en SNMP les imprimantes dont la fiche porte une adresse et une version, pour " +
        "mettre à jour leur compteur de pages et le niveau des cartouches installées (OID " +
        "configuré sur la référence de cartouche). Les imprimantes injoignables sont ignorées et " +
        "réessayées au cycle suivant.";

    public int DefaultFrequencyMinutes => 720;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<Printer> printers = await db.Set<Printer>()
            .Where(printer => printer.SnmpVersion != PrinterSnmpVersion.None && printer.IpAddress != null)
            .ToListAsync(cancellationToken);

        if (printers.Count == 0)
        {
            return;
        }

        // Les cartouches en service, avec leur référence : c'est elle qui porte l'OID du niveau.
        List<int> printerIds = [.. printers.Select(printer => printer.Id)];
        List<Cartridge> installed = await db.Set<Cartridge>()
            .Include(cartridge => cartridge.CartridgeItem)
            .Where(cartridge => cartridge.PrinterId != null
                                && printerIds.Contains(cartridge.PrinterId.Value)
                                && cartridge.DateOut == null)
            .ToListAsync(cancellationToken);

        int polled = 0;
        int unreachable = 0;

        foreach (Printer printer in printers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool anySuccess = await PollPageCountAsync(printer, cancellationToken);

            foreach (Cartridge cartridge in installed.Where(c => c.PrinterId == printer.Id))
            {
                anySuccess |= await PollCartridgeLevelAsync(printer, cartridge, cancellationToken);
            }

            if (anySuccess)
            {
                polled++;
            }
            else
            {
                unreachable++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Relevé SNMP des imprimantes : {Polled} interrogée(s) avec succès, {Unreachable} sans réponse.",
            polled, unreachable);
    }

    private async Task<bool> PollPageCountAsync(Printer printer, CancellationToken cancellationToken)
    {
        SnmpReadResult result = await reader.ReadAsync(printer, PageCountOid, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogDebug("Relevé SNMP : compteur de pages de « {Printer} » non lu ({Failure}).",
                printer.Name, result.Failure);
            return false;
        }

        if (result.AsNumber is not { } pages || pages < 0)
        {
            return false;
        }

        int pageCount = (int)Math.Min(pages, int.MaxValue);

        // Le compteur ne peut que croître : une valeur inférieure signale une imprimante remplacée
        // sous la même adresse, pas un retour en arrière. On l'enregistre quand même — c'est bien
        // l'état courant — mais on le trace, parce que la consommation calculée par différence
        // deviendrait sinon aberrante sans explication.
        if (printer.CurrentPageCount is { } previous && pageCount < previous)
        {
            AddHistory(printer, "Compteur de pages",
                $"Compteur relevé en baisse ({previous} → {pageCount}) : imprimante probablement remplacée ou remise à zéro.");
        }
        else if (printer.CurrentPageCount == pageCount)
        {
            return true;
        }

        printer.CurrentPageCount = pageCount;
        printer.InitialPageCount ??= pageCount;

        return true;
    }

    private async Task<bool> PollCartridgeLevelAsync(Printer printer, Cartridge cartridge, CancellationToken cancellationToken)
    {
        if (cartridge.CartridgeItem?.SnmpLevelOid is not { Length: > 0 } levelOid)
        {
            return false;
        }

        SnmpReadResult level = await reader.ReadAsync(printer, levelOid, cancellationToken);

        if (!level.Succeeded || level.AsNumber is not { } raw)
        {
            logger.LogDebug("Relevé SNMP : niveau de « {Cartridge} » sur « {Printer} » non lu ({Failure}).",
                cartridge.CartridgeItem?.Name, printer.Name, level.Failure ?? "valeur non numérique");
            return false;
        }

        // La norme réserve les valeurs négatives : -1 « inconnu », -2 « sans limite », -3 « il en
        // reste ». Aucune n'est un niveau, et les enregistrer comme tel afficherait -1 % sur la
        // fiche.
        if (raw < 0)
        {
            logger.LogDebug("Relevé SNMP : « {Printer} » déclare le niveau de « {Cartridge} » comme non mesurable ({Raw}).",
                printer.Name, cartridge.CartridgeItem?.Name, raw);
            return true;
        }

        int? percent = await ResolvePercentAsync(printer, levelOid, raw, cancellationToken);

        if (percent is not { } value)
        {
            return true;
        }

        if (cartridge.LevelPercent != value)
        {
            AddHistory(printer, "Niveau de cartouche",
                $"« {cartridge.CartridgeItem?.Name} » : {cartridge.LevelPercent?.ToString() ?? "inconnu"} % → {value} %");
        }

        cartridge.LevelPercent = value;
        cartridge.LevelReadAt = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Convertit un niveau brut en pourcentage.
    ///
    /// La norme expose la capacité maximale d'un consommable à côté de son niveau : même sous-arbre
    /// (<c>prtMarkerSupplies</c>), colonne 8 au lieu de 9, même rang. On la lit donc en dérivant
    /// l'OID configuré, ce qui évite d'imposer un second OID à saisir par référence de cartouche.
    ///
    /// Quand cette lecture échoue, le niveau brut est retenu tel quel s'il tient dans une
    /// proportion — beaucoup d'imprimantes publient directement un pourcentage — et abandonné
    /// sinon : un nombre de millilitres affiché comme un pourcentage serait pire que pas de valeur.
    /// </summary>
    private async Task<int?> ResolvePercentAsync(Printer printer, string levelOid, long raw, CancellationToken cancellationToken)
    {
        if (DeriveMaxCapacityOid(levelOid) is { } maxOid)
        {
            SnmpReadResult max = await reader.ReadAsync(printer, maxOid, cancellationToken);

            if (max.Succeeded && max.AsNumber is { } capacity && capacity > 0)
            {
                return (int)Math.Clamp(raw * 100 / capacity, 0, 100);
            }
        }

        return raw <= 100 ? (int)raw : null;
    }

    /// <summary>OID de capacité maximale correspondant à un OID de niveau, quand il suit la norme.</summary>
    private static string? DeriveMaxCapacityOid(string levelOid)
    {
        const string levelPrefix = "1.3.6.1.2.1.43.11.1.1.9.";
        const string maxPrefix = "1.3.6.1.2.1.43.11.1.1.8.";

        string trimmed = levelOid.Trim().TrimStart('.');

        return trimmed.StartsWith(levelPrefix, StringComparison.Ordinal)
            ? maxPrefix + trimmed[levelPrefix.Length..]
            : null;
    }

    private void AddHistory(Printer printer, string field, string description) =>
        db.Set<PrinterHistoryEntry>().Add(new PrinterHistoryEntry
        {
            PrinterId = printer.Id,
            User = "Relevé SNMP",
            Field = field,
            Description = description,
        });
}
