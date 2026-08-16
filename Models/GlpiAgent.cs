namespace GlpiNg.Modules.Inventory.Models;

/// <summary>
/// Représente un agent GLPI-Agent enregistré (identifié par sa deviceid). C'est
/// l'entité par laquelle un poste remonte son inventaire ; elle appartient donc au
/// module Inventory. Le module Deploy (voir GlpiNg.Web) référence un agent par son
/// seul <see cref="Id"/> (via <c>DeploymentJob.AgentId</c>) plutôt que par une
/// navigation object-relationnelle, afin de ne pas créer de dépendance de ce module
/// vers le module Deploy.
/// </summary>
public class GlpiAgent
{
    public int Id { get; set; }

    /// <summary>
    /// Identité réelle de l'agent au sens du protocole (header HTTP "GLPI-Agent-ID", UUID).
    /// C'est la clé de corrélation stable — voir COMMON#glpi-agent-id du protocole JSON GLPI.
    /// </summary>
    public required string AgentUuid { get; set; }

    /// <summary>Nom "ami" envoyé dans le corps de la requête contact ("deviceid"), pas garanti unique.</summary>
    public string? DeviceId { get; set; }

    public string? Hostname { get; set; }
    public string? AgentName { get; set; }
    public string? AgentVersion { get; set; }
    public string? Tag { get; set; }
    public string[] InstalledTasks { get; set; } = [];
    public string[] EnabledTasks { get; set; } = [];

    public DateTime FirstContactAt { get; set; } = DateTime.UtcNow;
    public DateTime LastContactAt { get; set; } = DateTime.UtcNow;

    /// <summary>Header HTTP "User-Agent" du dernier contact reçu (ex: "GLPI-Agent_v1.16").</summary>
    public string? LastUserAgent { get; set; }

    /// <summary>Adresse IP constatée (RemoteIpAddress) du dernier contact reçu.</summary>
    public string? LastContactIp { get; set; }

    /// <summary>
    /// Navigation vers le poste rattaché. La FK réelle vit sur Computer.AgentId (relation
    /// configurée dans GlpiNgDbContext) — il n'y a volontairement pas de "ComputerId" ici
    /// pour éviter une colonne fantôme non synchronisée par EF Core.
    /// </summary>
    public Computer? Computer { get; set; }
}
