using GlpiNg.Modules.Abstractions.Cron;
using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Réévalue périodiquement les règles pour les actifs marquées « à l'exécution périodique »
/// (<see cref="ComputerRuleAppliesTo.OnSchedule"/>).
///
/// Sans elle, une règle portant sur l'ancienneté du dernier contact ne se déclencherait jamais :
/// le moteur n'était évalué qu'à l'arrivée d'un inventaire, moment où le contact vient
/// précisément d'avoir lieu et où la condition est donc toujours fausse. C'est ce passage
/// périodique qui rend utilisables des règles du type « si le dernier contact remonte à plus de
/// 24 h, passer le poste en Hors service ».
///
/// Ne traite que les règles portant explicitement ce moment : les règles d'inventaire ne doivent
/// pas se réappliquer dans le dos de l'administrateur, sur des postes qui n'ont rien remonté.
/// </summary>
public sealed class ComputerRuleCronTask(DbContext db) : ICronTask
{
    public string Key => "computer_rules";

    public string Name => "Règles pour les actifs (exécution périodique)";

    public string Description =>
        "Réévalue les règles pour les actifs marquées « à l'exécution périodique », notamment " +
        "celles qui portent sur l'ancienneté du dernier inventaire — une condition qu'un " +
        "inventaire entrant ne peut jamais vérifier.";

    public int DefaultFrequencyMinutes => 60;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<ComputerRule> rules = await db.Set<ComputerRule>()
            .Include(rule => rule.Criteria)
            .Include(rule => rule.Actions)
            .Where(rule => rule.IsActive && (rule.AppliesTo & ComputerRuleAppliesTo.OnSchedule) != 0)
            .OrderBy(rule => rule.SortOrder)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return;
        }

        // Statuts pré-résolus : le moteur est synchrone et n'accède pas à la base, il reçoit donc
        // une correspondance nom -> intitulé déjà construite. Seuls les noms réellement cités par
        // les actions sont résolus.
        Dictionary<string, int> statusIdByName = await ResolveStatusIdsAsync(rules, cancellationToken);

        // Instant de référence unique : sur un parc important, le traitement peut durer, et deux
        // postes comparables ne doivent pas être jugés différemment selon leur rang dans la boucle.
        DateTime now = DateTime.UtcNow;

        List<Computer> computers = await db.Set<Computer>()
            .Include(computer => computer.StatusItem)
            .Where(computer => !computer.IsDeleted)
            .ToListAsync(cancellationToken);

        int changed = 0;

        foreach (Computer computer in computers)
        {
            ComputerSnapshot before = ComputerSnapshot.Capture(computer);

            ComputerRuleEngine.Apply(
                computer,
                ComputerRuleAppliesTo.OnSchedule,
                rules,
                statusName => statusIdByName.TryGetValue(statusName, out int id) ? id : null,
                now);

            if (!before.Equals(ComputerSnapshot.Capture(computer)))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Résout les statuts cités par les actions. Contrairement à l'import d'inventaire, un statut
    /// inconnu n'est pas créé à la volée : une règle mal saisie ne doit pas peupler le référentiel
    /// des intitulés. L'action est alors sans effet.
    /// </summary>
    private async Task<Dictionary<string, int>> ResolveStatusIdsAsync(List<ComputerRule> rules, CancellationToken cancellationToken)
    {
        List<string> names = [.. rules
            .SelectMany(rule => rule.Actions)
            .Where(action => action.Field == "Status" && !string.IsNullOrWhiteSpace(action.Value))
            .Select(action => action.Value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (names.Count == 0)
        {
            return [];
        }

        List<DropdownItem> items = await db.Set<DropdownItem>()
            .Where(item => item.Type == DropdownType.Status && names.Contains(item.Name))
            .ToListAsync(cancellationToken);

        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (DropdownItem item in items)
        {
            byName[item.Name] = item.Id;
        }

        return byName;
    }

    /// <summary>Ce qu'une règle périodique peut modifier, pour ne sauvegarder que s'il y a eu un changement.</summary>
    private sealed record ComputerSnapshot(int? StatusId, string? Name, string? Comment, string? AssignedUser, string? Site, string? Building, string? Room)
    {
        public static ComputerSnapshot Capture(Computer computer) =>
            new(computer.StatusId, computer.Name, null, computer.AssignedUser, computer.Site, computer.Building, computer.Room);
    }
}
