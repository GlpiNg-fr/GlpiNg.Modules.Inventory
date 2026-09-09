namespace GlpiNg.Modules.Inventory.Models;

public enum ComponentType
{
    Cpu,
    Ram,
    Disk,
    NetworkCard,
    Gpu,
    Motherboard,

    /// <summary>Contrôleur (stockage, USB, chipset) — section "controllers" de l'inventaire.</summary>
    Controller,

    /// <summary>Carte son — section "sounds".</summary>
    SoundCard,

    /// <summary>Modem — section "modems".</summary>
    Modem,

    /// <summary>
    /// Firmware de la machine (BIOS/UEFI) — section "bios". Équivalent du composant Firmware de
    /// GLPI. Nouvelle valeur ajoutée en fin d'énumération, jamais insérée : Type est persisté
    /// comme entier.
    /// </summary>
    Firmware
}

public class ComputerComponent
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public ComponentType Type { get; set; }
    public required string Designation { get; set; }
    public string? Capacity { get; set; }
    public string? Serial { get; set; }
}
