using GlpiNg.Modules.Abstractions.Cron;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Équivalent réduit de la tâche cron GLPI "cleanupagents" : purge les agents GLPI-Agent
/// (contact seul, sans inventaire jamais rattaché) inactifs depuis plus de <see
/// cref="AgentCleanupOptions.AgentCleanupDays"/> jours.
///
/// Ne supprime volontairement que les agents sans <see cref="Computer"/> rattaché : la FK
/// Computer.AgentId est en <c>ON DELETE CASCADE</c> (voir GlpiNgDbContext), donc purger un
/// agent encore lié à un poste supprimerait ce poste et tout son historique — un agent qui
/// a déjà remonté un inventaire garde sa fiche même s'il redevient injoignable.
/// </summary>
public sealed class AgentCleanupCronTask(DbContext db, IOptionsMonitor<AgentCleanupOptions> options) : ICronTask
{
    public string Key => "agent_cleanup";

    public string Name => "Purge des agents orphelins";

    public string Description =>
        "Purge les agents GLPI-Agent jamais rattachés à un ordinateur (contact seul) inactifs " +
        "depuis plus longtemps que le délai réglé dans la configuration du module Inventaire.";

    public int DefaultFrequencyMinutes => 1440;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        int days = options.CurrentValue.AgentCleanupDays;
        if (days <= 0)
        {
            return;
        }

        DateTime threshold = DateTime.UtcNow.AddDays(-days);

        // RemoveRange + SaveChanges plutôt que ExecuteDeleteAsync : ce dernier vit dans
        // Microsoft.EntityFrameworkCore.Relational, une dépendance que ce module évite
        // volontairement (voir GlpiNg.Modules.Inventory.csproj) puisqu'il ne connaît pas
        // le fournisseur de base de données de l'hôte. Le volume d'agents orphelins est
        // faible, donc le coût du chargement en mémoire est négligeable.
        List<GlpiAgent> staleAgents = await db.Set<GlpiAgent>()
            .Where(a => a.LastContactAt < threshold && !db.Set<Computer>().Any(c => c.AgentId == a.Id))
            .ToListAsync(cancellationToken);

        if (staleAgents.Count == 0)
        {
            return;
        }

        db.Set<GlpiAgent>().RemoveRange(staleAgents);
        await db.SaveChangesAsync(cancellationToken);
    }
}
