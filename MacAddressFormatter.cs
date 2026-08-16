using System.Text.RegularExpressions;

namespace GlpiNg.Modules.Inventory;

public static partial class MacAddressFormatter
{
    public static string Format(string? mac, string format)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return "—";

        string hex = SeparatorRegex().Replace(mac, "").ToUpperInvariant();

        if (hex.Length != 12)
            return mac;

        return format switch
        {
            "HP" => $"{hex[..6]}-{hex[6..]}",
            "Cisco" => $"{hex[..4]}.{hex[4..8]}.{hex[8..]}".ToLowerInvariant(),
            _ => $"{hex[..2]}-{hex[2..4]}-{hex[4..6]}-{hex[6..8]}-{hex[8..10]}-{hex[10..]}",
        };
    }

    [GeneratedRegex(@"[-:.\s]")]
    private static partial Regex SeparatorRegex();
}
