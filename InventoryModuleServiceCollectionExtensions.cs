using GlpiNg.Modules.Abstractions.Cron;
using GlpiNg.Modules.Abstractions.Menu;
using GlpiNg.Modules.Inventory.Controllers;
using GlpiNg.Modules.Inventory.Import;
using GlpiNg.Modules.Inventory.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GlpiNg.Modules.Inventory;

/// <summary>
/// Point d'enregistrement du module Inventory dans le conteneur DI de l'application
/// hôte (GlpiNg.Web). Sert de modèle pour les futurs modules (Tickets, etc.) : chacun
/// expose une extension du même type (ex. <c>AddTicketsModule</c>), appelée depuis
/// Program.cs, plutôt que de faire enregistrer ses services par le projet hôte.
///
/// Le module ne connaît pas le DbContext concret de l'hôte (voir
/// GlpiMySqlImportService, qui dépend du <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// de base) : c'est à l'hôte de mapper les entités du module (Computer, ComputerComponent,
/// GlpiAgent) dans son propre DbContext.
/// </summary>
public static class InventoryModuleServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GlpiImportOptions>(configuration.GetSection(GlpiImportOptions.SectionName));
        services.AddScoped<GlpiMySqlImportService>();

        // État partagé entre tous les admins consultant /admin/import/glpi (voir
        // GlpiImportStateService) : singleton plutôt que scoped, pour qu'un import
        // déclenché par un admin soit visible par tous les autres.
        services.AddSingleton<GlpiImportStateService>();

        // Contribution du module au service cron de l'hôte (voir GlpiNg.Modules.Cron) :
        // purge périodique des agents orphelins, réglage "AgentCleanupDays" de l'onglet
        // "Parc" (Administration > Inventaire).
        services.Configure<AgentCleanupOptions>(configuration.GetSection(AgentCleanupOptions.SectionName));
        services.AddScoped<ICronTask, AgentCleanupCronTask>();

        // Reevaluation periodique des regles pour les actifs : voir ComputerRuleCronTask, sans
        // laquelle une regle portant sur l'anciennete du dernier contact ne se declencherait jamais.
        services.AddScoped<ICronTask, ComputerRuleCronTask>();

        // Relevé SNMP des imprimantes : compteur de pages et niveau des cartouches installées.
        // Une imprimante réseau n'héberge pas d'agent — sans cette action, personne ne remonte son
        // état alors qu'elle-même le publie. Voir PrinterSnmpPollCronTask.
        services.AddScoped<PrinterSnmpReader>();
        services.AddScoped<ICronTask, PrinterSnmpPollCronTask>();

        // Contribution du module au menu latéral de l'hôte (groupe "Parc" + entrées
        // Agents/Déploiements dans "Outils") — voir InventoryMenuProvider.
        services.AddSingleton<IMenuProvider, InventoryMenuProvider>();

        services.AddScoped<ComputerListStateService>();

        // Réveil immédiat depuis la fiche ordinateur (bouton « Actions »). Sans état, donc
        // singleton. À ne pas confondre avec les tâches Wake-on-LAN du module Déploiement, qui
        // font relayer le réveil par un agent — voir la doc de WakeOnLanSender.
        services.AddSingleton<WakeOnLanSender>();

        // Permet à ASP.NET Core de découvrir les contrôleurs de ce module (assembly
        // distincte de celle du projet hôte).
        services.AddControllers()
            .AddApplicationPart(typeof(GlpiImportController).Assembly);

        return services;
    }
}
