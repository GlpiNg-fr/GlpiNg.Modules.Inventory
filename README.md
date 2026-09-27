# GlpiNg.Modules.Inventory

*[English version](README.en.md)*

Module Inventaire de GlpiNg : le parc informatique alimenté par les GLPI-Agent.

> **Avertissement** — GlpiNg est un projet indépendant. Il n'est ni affilié à, ni approuvé,
> soutenu ou sponsorisé par Teclib' ou le projet GLPI. « GLPI » et « GLPI-Agent » sont des
> marques de leurs propriétaires respectifs ; elles ne sont citées ici que pour décrire la
> compatibilité de GlpiNg avec le protocole GLPI-Agent et l'import depuis une base GLPI.

## Contenu

- Ordinateurs et composants, moniteurs, imprimantes, téléphones, équipements réseau, baies, PDU, câbles, consommables...
- Agents GLPI
- Règles d'import, d'affectation et dictionnaires
- Recherches enregistrées
- Import depuis une base GLPI MySQL
- Actions automatiques : nettoyage des agents, règles, relevé SNMP des imprimantes
- Rapports de parc

## Utilisation

Ce dépôt est un sous-module de [GlpiNg](https://github.com/GlpiNg-fr/GlpiNg), sous
`src/GlpiNg.Modules.Inventory`. Il ne se compile pas seul : il référence `GlpiNg.Modules.Abstractions` par chemin relatif.

```bash
git clone --recurse-submodules https://github.com/GlpiNg-fr/GlpiNg.git
```

L'hôte l'enregistre par `services.AddInventoryModule(configuration)` (voir `Program.cs`).

## Licence

[GNU Affero General Public License v3.0](LICENSE).
