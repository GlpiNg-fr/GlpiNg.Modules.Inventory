using System.Net;
using System.Net.Sockets;

namespace GlpiNg.Modules.Inventory.Services;

/// <summary>Une interface réseau connue d'un poste, telle qu'on peut la viser au réveil.</summary>
/// <param name="Mac">Adresse MAC, dans n'importe quel format usuel (séparée par « : », « - » ou collée).</param>
/// <param name="IpAddress">Dernière IPv4 connue de cette interface, pour en déduire le broadcast dirigé.</param>
/// <param name="IpMask">Masque de sous-réseau associé.</param>
public sealed record WakeOnLanNic(string? Mac, string? IpAddress = null, string? IpMask = null);

/// <param name="Macs">MAC réellement visées, normalisées et dédoublonnées.</param>
/// <param name="Broadcasts">Adresses de diffusion utilisées, pour que l'échec soit diagnosticable.</param>
/// <param name="PacketsSent">Nombre de datagrammes émis (MAC × broadcast × port).</param>
/// <param name="Errors">Erreurs de socket rencontrées, une par cible.</param>
public sealed record WakeOnLanSendResult(
    IReadOnlyList<string> Macs,
    IReadOnlyList<string> Broadcasts,
    int PacketsSent,
    IReadOnlyList<string> Errors)
{
    public bool Sent => PacketsSent > 0;
}

/// <summary>
/// Émet des « magic packets » Wake-on-LAN depuis le serveur.
///
/// À ne pas confondre avec les tâches Wake-on-LAN du module Déploiement, qui font relayer le
/// réveil par un agent : celles-ci franchissent les sous-réseaux (l'agent relais diffuse depuis
/// l'intérieur du segment), au prix d'une planification et d'un agent allumé sur place. Ce
/// service-ci réveille immédiatement, sans relais — mais un magic packet est un datagramme de
/// broadcast, et un routeur ne le fait normalement pas traverser. Il n'atteint donc que les postes
/// du segment du serveur, ou ceux dont le routeur accepte le broadcast dirigé (rarement le cas par
/// défaut). Les deux mécanismes se complètent ; aucun ne remplace l'autre.
/// </summary>
public sealed class WakeOnLanSender
{
    /// <summary>
    /// Les deux ports d'usage. Le WOL ne définit aucun port : la charge utile est reconnue par la
    /// carte réseau quel que soit le port UDP. 9 (discard) est le plus répandu, 7 (echo) reste
    /// utilisé par du matériel ancien — les deux sont émis, le coût étant nul.
    /// </summary>
    private static readonly int[] Ports = [9, 7];

    public async Task<WakeOnLanSendResult> SendAsync(IEnumerable<WakeOnLanNic> nics, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nics);

        List<WakeOnLanNic> knownNics = [.. nics];

        // Une même MAC peut apparaître sur plusieurs lignes d'inventaire (alias, ré-inventaire) :
        // inutile d'émettre deux fois la même trame.
        List<string> macs = [.. knownNics
            .Select(nic => NormalizeMac(nic.Mac))
            .Where(mac => mac is not null)
            .Select(mac => mac!)
            .Distinct(StringComparer.Ordinal)];

        List<IPAddress> broadcasts = ResolveBroadcasts(knownNics);
        List<string> errors = [];
        int sent = 0;

        if (macs.Count > 0)
        {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true
            };

            foreach (string mac in macs)
            {
                byte[] packet = BuildMagicPacket(mac);

                foreach (IPAddress broadcast in broadcasts)
                {
                    foreach (int port in Ports)
                    {
                        try
                        {
                            await socket.SendToAsync(packet, SocketFlags.None, new IPEndPoint(broadcast, port), cancellationToken);
                            sent++;
                        }
                        catch (SocketException ex)
                        {
                            errors.Add($"{mac} vers {broadcast}:{port} — {ex.Message}");
                        }
                    }
                }
            }
        }

        return new WakeOnLanSendResult(macs, [.. broadcasts.Select(b => b.ToString())], sent, errors);
    }

    /// <summary>
    /// Broadcast limité (255.255.255.255) plus, quand l'inventaire connaît l'IP et le masque de
    /// l'interface, le broadcast dirigé de son sous-réseau. Le second ne coûte rien à émettre et
    /// est la seule chance d'aboutir quand le poste n'est pas sur le segment du serveur ; le
    /// premier est le seul qui fonctionne à coup sûr, mais uniquement sur ce segment.
    /// </summary>
    private static List<IPAddress> ResolveBroadcasts(IEnumerable<WakeOnLanNic> nics)
    {
        List<IPAddress> broadcasts = [IPAddress.Broadcast];

        foreach (WakeOnLanNic nic in nics)
        {
            if (DirectedBroadcast(nic.IpAddress, nic.IpMask) is not { } directed)
            {
                continue;
            }

            if (!broadcasts.Any(existing => existing.Equals(directed)))
            {
                broadcasts.Add(directed);
            }
        }

        return broadcasts;
    }

    private static IPAddress? DirectedBroadcast(string? ipAddress, string? mask)
    {
        if (!IPAddress.TryParse(ipAddress, out IPAddress? ip)
            || !IPAddress.TryParse(mask, out IPAddress? netmask)
            || ip.AddressFamily != AddressFamily.InterNetwork
            || netmask.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        byte[] ipBytes = ip.GetAddressBytes();
        byte[] maskBytes = netmask.GetAddressBytes();
        byte[] result = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            result[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        }

        IPAddress broadcast = new(result);

        // Un masque /32 (ou absent, remonté comme 255.255.255.255) redonne l'IP du poste : ce
        // n'est pas un broadcast, et l'envoyer en unicast à une machine éteinte ne réveille rien.
        return broadcast.Equals(ip) ? null : broadcast;
    }

    /// <summary>MAC sur 12 chiffres hexadécimaux majuscules, ou <c>null</c> si la valeur remontée
    /// n'en est pas une (champ libre côté agent : on n'émet pas une trame construite au hasard).</summary>
    internal static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        Span<char> digits = stackalloc char[12];
        int count = 0;

        foreach (char c in mac)
        {
            if (c is ':' or '-' or '.' or ' ')
            {
                continue;
            }

            if (!Uri.IsHexDigit(c) || count == 12)
            {
                return null;
            }

            digits[count++] = char.ToUpperInvariant(c);
        }

        return count == 12 ? new string(digits) : null;
    }

    /// <summary>6 octets 0xFF suivis de 16 répétitions de la MAC, soit 102 octets.</summary>
    private static byte[] BuildMagicPacket(string normalizedMac)
    {
        byte[] mac = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            mac[i] = Convert.ToByte(normalizedMac.Substring(i * 2, 2), 16);
        }

        byte[] packet = new byte[6 + (16 * 6)];
        packet.AsSpan(0, 6).Fill(0xFF);

        for (int repeat = 0; repeat < 16; repeat++)
        {
            mac.CopyTo(packet, 6 + (repeat * 6));
        }

        return packet;
    }
}
