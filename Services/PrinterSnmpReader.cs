using GlpiNg.Modules.Abstractions.Localization;
﻿using System.Net;
using GlpiNg.Modules.Inventory.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Ce qu'une interrogation SNMP a rapporté pour un OID, ou pourquoi elle n'a rien rapporté.</summary>
/// <param name="Value">Valeur brute rendue par l'agent SNMP, telle quelle.</param>
/// <param name="Failure">Cause de l'échec, formulée pour un administrateur. Null en cas de succès.</param>
public sealed record SnmpReadResult(string? Value, string? Failure)
{
    public bool Succeeded => Failure is null;

    /// <summary>Valeur interprétée comme un entier, quand elle en est un.</summary>
    public long? AsNumber => long.TryParse(Value, out long parsed) ? parsed : null;
}

/// <summary>
/// Interroge une imprimante en SNMP, à partir des paramètres portés par sa fiche.
///
/// Les trois versions sont gérées, parce que le parc d'imprimantes d'une organisation les mélange
/// presque toujours : du matériel ancien en v1, l'essentiel en v2c, et les modèles récents
/// configurés en v3 quand la politique de sécurité l'impose.
///
/// Aucune exception ne sort d'ici : une imprimante éteinte, déplacée ou mal configurée est un
/// événement ordinaire dans un parc, pas une panne du serveur. Chaque échec est rendu sous forme
/// de texte, pour que l'action périodique puisse le journaliser et passer à la suivante.
/// </summary>
public sealed class PrinterSnmpReader
{
    /// <summary>Délai d'attente par requête. Court : une imprimante injoignable ne doit pas retarder
    /// tout le parc, et le relevé repassera au cycle suivant.</summary>
    private const int TimeoutMilliseconds = 3000;

    public async Task<SnmpReadResult> ReadAsync(Printer printer, string oid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);

        if (printer.SnmpVersion == PrinterSnmpVersion.None)
        {
            return new SnmpReadResult(null, "interrogation SNMP désactivée sur cette imprimante");
        }

        if (!IPAddress.TryParse(printer.IpAddress?.Trim(), out IPAddress? address))
        {
            return new SnmpReadResult(null, $"adresse IP absente ou invalide ({printer.IpAddress ?? "vide"})");
        }

        if (!TryParseOid(oid, out ObjectIdentifier? identifier))
        {
            return new SnmpReadResult(null, Tr.T("OID invalide ({0})", oid));
        }

        IPEndPoint endpoint = new(address, printer.SnmpPort > 0 ? printer.SnmpPort : 161);

        try
        {
            Task<SnmpReadResult> read = printer.SnmpVersion == PrinterSnmpVersion.V3
                ? ReadV3Async(printer, endpoint, identifier!)
                : ReadV1V2Async(printer, endpoint, identifier!);

            // Les surcharges asynchrones de la bibliothèque ne prennent pas de délai d'attente :
            // on le pose ici. Une imprimante qui ne répond pas immobiliserait sinon le relevé de
            // tout le parc, alors qu'elle sera réinterrogée au cycle suivant.
            Task finished = await Task.WhenAny(read, Task.Delay(TimeoutMilliseconds, cancellationToken));

            return finished == read
                ? await read
                : new SnmpReadResult(null, Tr.T("pas de réponse en {0} ms", TimeoutMilliseconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Volontairement large : la bibliothèque remonte des types variés (timeout, socket,
            // erreur de protocole, authentification refusée) et aucun ne justifie d'interrompre le
            // relevé des autres imprimantes.
            return new SnmpReadResult(null, ex.Message);
        }
    }

    private static async Task<SnmpReadResult> ReadV1V2Async(
        Printer printer, IPEndPoint endpoint, ObjectIdentifier oid)
    {
        if (string.IsNullOrWhiteSpace(printer.SnmpCommunity))
        {
            return new SnmpReadResult(null, "communauté SNMP non renseignée");
        }

        VersionCode version = printer.SnmpVersion == PrinterSnmpVersion.V1 ? VersionCode.V1 : VersionCode.V2;

        IList<Variable> reply = await Messenger.GetAsync(
            version, endpoint, new OctetString(printer.SnmpCommunity), [new Variable(oid)]);

        return FromReply(reply);
    }

    /// <summary>
    /// Interrogation v3, précédée de la découverte du moteur distant : un agent SNMPv3 refuse
    /// toute requête qui ne cite pas son identifiant de moteur et ses compteurs, qu'on ne peut
    /// obtenir qu'en le lui demandant.
    /// </summary>
    private static async Task<SnmpReadResult> ReadV3Async(
        Printer printer, IPEndPoint endpoint, ObjectIdentifier oid)
    {
        if (string.IsNullOrWhiteSpace(printer.SnmpUsername))
        {
            return new SnmpReadResult(null, "nom de sécurité SNMPv3 non renseigné");
        }

        if (BuildPrivacy(printer) is not { } privacy)
        {
            return new SnmpReadResult(null, "protocole d'authentification ou de chiffrement SNMPv3 non pris en charge");
        }

        Discovery discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        ReportMessage report = await discovery.GetResponseAsync(endpoint);

        GetRequestMessage request = new(
            VersionCode.V3,
            Messenger.NextMessageId,
            Messenger.NextRequestId,
            new OctetString(printer.SnmpUsername),
            // Nom de contexte vide : celui par défaut de l'agent, le seul qu'une imprimante
            // expose. La surcharge sans ce paramètre est dépréciée.
            OctetString.Empty,
            [new Variable(oid)],
            privacy,
            Messenger.MaxMessageSize,
            report);

        ISnmpMessage response = await request.GetResponseAsync(endpoint);

        return response.Pdu().ErrorStatus.ToInt32() != 0
            ? new SnmpReadResult(null, Tr.T("erreur SNMP {0}", response.Pdu().ErrorStatus))
            : FromReply(response.Pdu().Variables);
    }

    private static SnmpReadResult FromReply(IList<Variable> reply)
    {
        if (reply.Count == 0)
        {
            return new SnmpReadResult(null, "réponse vide");
        }

        ISnmpData data = reply[0].Data;

        // Un agent qui ne connaît pas l'OID répond par un marqueur, pas par une erreur : sans ce
        // contrôle, on enregistrerait « noSuchObject » comme s'il s'agissait d'une valeur.
        if (data.TypeCode is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView)
        {
            return new SnmpReadResult(null, Tr.T("OID inconnu de l'imprimante ({0})", data.TypeCode));
        }

        if (data.TypeCode == SnmpType.Null)
        {
            return new SnmpReadResult(null, "valeur absente");
        }

        return new SnmpReadResult(data.ToString(), null);
    }

    /// <summary>
    /// Fournisseur de sécurité v3 correspondant aux protocoles choisis. Trois combinaisons
    /// existent : sans authentification, authentifié seul, authentifié et chiffré — le chiffrement
    /// sans authentification n'existe pas dans le protocole.
    /// </summary>
    private static IPrivacyProvider? BuildPrivacy(Printer printer)
    {
        if (printer.SnmpAuthProtocol == PrinterSnmpAuthProtocol.None)
        {
            return DefaultPrivacyProvider.DefaultPair;
        }

        OctetString authPhrase = new(printer.SnmpAuthPassphrase ?? string.Empty);

        // MD5, SHA-1, DES et 3DES sont dépréciés par la bibliothèque, à juste titre : ils sont
        // cassés. Ils restent pourtant ce que proposent quantité d'imprimantes encore en service,
        // dont le micrologiciel ne connaît rien d'autre. Les refuser rendrait la fonction inutile
        // sur le matériel qui en a le plus besoin ; le choix revient à l'administrateur, qui les
        // sélectionne explicitement sur la fiche.
#pragma warning disable CS0618 // Type or member is obsolete
        IAuthenticationProvider? authentication = printer.SnmpAuthProtocol switch
        {
            PrinterSnmpAuthProtocol.Md5 => new MD5AuthenticationProvider(authPhrase),
            PrinterSnmpAuthProtocol.Sha => new SHA1AuthenticationProvider(authPhrase),
            // SHA-224 est absent de la bibliothèque : plutôt que de le rabattre en silence sur un
            // autre condensat — ce qui produirait une authentification refusée sans explication —
            // le protocole est déclaré non pris en charge, et l'action le dit.
            PrinterSnmpAuthProtocol.Sha256 => new SHA256AuthenticationProvider(authPhrase),
            PrinterSnmpAuthProtocol.Sha384 => new SHA384AuthenticationProvider(authPhrase),
            PrinterSnmpAuthProtocol.Sha512 => new SHA512AuthenticationProvider(authPhrase),
            _ => null,
        };

        if (authentication is null)
        {
            return null;
        }

        if (printer.SnmpPrivProtocol == PrinterSnmpPrivProtocol.None)
        {
            return new DefaultPrivacyProvider(authentication);
        }

        OctetString privPhrase = new(printer.SnmpPrivPassphrase ?? string.Empty);

        return printer.SnmpPrivProtocol switch
        {
            PrinterSnmpPrivProtocol.Des => new DESPrivacyProvider(privPhrase, authentication),
            PrinterSnmpPrivProtocol.TripleDes => new TripleDESPrivacyProvider(privPhrase, authentication),
            PrinterSnmpPrivProtocol.Aes128 => new AESPrivacyProvider(privPhrase, authentication),
            PrinterSnmpPrivProtocol.CiscoAes192 or PrinterSnmpPrivProtocol.Aes192Ietf =>
                new AES192PrivacyProvider(privPhrase, authentication),
            PrinterSnmpPrivProtocol.CiscoAes256 or PrinterSnmpPrivProtocol.Aes256Ietf =>
                new AES256PrivacyProvider(privPhrase, authentication),
            _ => null,
        };
#pragma warning restore CS0618
    }

    /// <summary>Un OID se lit « 1.3.6.1.… », éventuellement précédé d'un point.</summary>
    private static bool TryParseOid(string? oid, out ObjectIdentifier? identifier)
    {
        identifier = null;

        if (string.IsNullOrWhiteSpace(oid))
        {
            return false;
        }

        try
        {
            identifier = new ObjectIdentifier(oid.Trim().TrimStart('.'));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
