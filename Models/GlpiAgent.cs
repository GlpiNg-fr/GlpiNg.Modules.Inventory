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

    /// <summary>Identifiant unique envoyé par l'agent (deviceid), ex: HOSTNAME-2024-01-01-12-00-00</summary>
    public required string DeviceId { get; set; }

    public string? Hostname { get; set; }
    public string? AgentVersion { get; set; }
    public string[] Tags { get; set; } = [];

    public DateTime FirstContactAt { get; set; } = DateTime.UtcNow;
    public DateTime LastContactAt { get; set; } = DateTime.UtcNow;

    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }
}
