# GlpiNg.Modules.Inventory

*[Version française](README.md)*

GlpiNg's Inventory module: the IT asset park fed by GLPI-Agent.

> **Disclaimer** — GlpiNg is an independent project. It is not affiliated with, endorsed,
> supported or sponsored by Teclib' or the GLPI project. "GLPI" and "GLPI-Agent" are trademarks
> of their respective owners; they are mentioned here only to describe GlpiNg's compatibility
> with the GLPI-Agent protocol and import from a GLPI database.

## Contents

- Computers and components, monitors, printers, phones, network equipment, racks, PDUs, cables, consumables...
- GLPI agents
- Import, assignment and dictionary rules
- Saved searches
- Import from a GLPI MySQL database
- Automatic actions: agent cleanup, rules, printer SNMP polling
- Asset reports

## Usage

This repository is a submodule of [GlpiNg](https://github.com/GlpiNg-fr/GlpiNg), under
`src/GlpiNg.Modules.Inventory`. It does not build on its own: it references `GlpiNg.Modules.Abstractions` by relative path.

```bash
git clone --recurse-submodules https://github.com/GlpiNg-fr/GlpiNg.git
```

The host registers it with `services.AddInventoryModule(configuration)` (see `Program.cs`).

## License

[GNU Affero General Public License v3.0](LICENSE).
