# TIA Portal Openness - Engineering Tool (V21)

C# console application (.NET Framework 4.8) that connects to TIA Portal V21 via
the Openness API to inspect and export project data.

> **Scope of this document**: current state of the application (read / export /
> generation from documents). The phased execution plan lives in
> [`ROADMAP.md`](./ROADMAP.md); the CLI reference is in [`CLI.md`](./CLI.md).

## Prerequisites

- TIA Portal V21 with Openness option installed
- .NET Framework 4.8 SDK / Developer Pack
- Current user added to the local **`Siemens TIA Openness`** group
- The executable registered in the **Openness AllowList** (run `fix_allowlist.bat` as Administrator)

## Build

```powershell
cd "C:\Users\DPTO_ELECT\Documents\TIA Portal Openness"
dotnet build TiaOpennessApp.csproj -c Debug
```

Or use `build.bat`, which builds and then refreshes the Openness AllowList.

## Allowlist Registration (after every rebuild)

```cmd
fix_allowlist.bat
```

Changes the executable's path, modification date or hash, so every rebuild must be
re-registered. The script self-elevates (UAC) and writes the TIA Portal Openness V21
entry (`HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaOpennessApp.exe\Entry`)
using the official format: SHA-256 (base64) and the UTC last-write time. It is a
no-op when the current binary is already registered.

Status can be checked without elevation:

```cmd
bin\Debug\net48\TiaOpennessApp.exe --check-allowlist
```

Note: `Siemens.Automation.Portal.exe` must **not** have the "Run this program as
administrator" compatibility flag, otherwise non-elevated Openness applications
cannot start TIA Portal (`ERROR_ELEVATION_REQUIRED`).

## Configuration

Project path and output location are read from `appsettings.json` (or provided at runtime).
The example below reflects the current values; values you type interactively in the menu
are saved back to `appsettings.json` automatically.

```json
{
  "TiaPortal": {
    "Mode": "Auto",
    "Version": "21",
    "ProjectPath": "C:\\Users\\DPTO_ELECT\\Documents\\Automation",
    "ProjectName": "MyProject",
    "ExportPath": "C:\\Users\\DPTO_ELECT\\Documents\\TIA Portal Openness\\exports",
    "ImportPath": "C:\\Users\\DPTO_ELECT\\Documents\\TIA Portal Openness\\imports",
    "NewProjectPath": "C:\\Users\\DPTO_ELECT\\Documents\\Automation",
    "NewProjectName": "MyNewProject"
  }
}
```

| Key | Description |
|-----|-------------|
| `Mode` | `Auto` / `WithoutUserInterface` / `WithUserInterface` |
| `Version` | TIA Portal version (21) |
| `ProjectPath` + `ProjectName` | Root folder and name used to resolve the project to open |
| `ImportPath` | Folder reserved for import documents (used by the roadmap) |
| `NewProjectPath` + `NewProjectName` | Destination and name for project creation |

## Features

### Main menu

| Option | Description |
|--------|-------------|
| 1. Open an existing project | Opens the project (`TiaPortal:ProjectPath`/`ProjectName`) and shows the project submenu below |
| 2. Create a new project | `ProjectComposition.Create`, validates an empty destination folder |
| 3. Generate project from documents | Full pipeline: parse -> validate -> devices/modules/subnets/interfaces -> `Save()`; no auto-comparison; use `--diff-topology` to compare two JSON exports |
| 4. Import CSVs to SQLite database | Builds `tia_topology.db` (7 tables, FKs, `vw_device_topology`/`vw_io_mapping`) from `imports/` + `catalog/Catalog.csv`; overwrites the existing DB after confirmation |
| 5. Export SQLite database to CSVs | Reverse of option 4: regenerates the 6 CSVs in `exports/` plus `catalog/Catalog.csv` from the DB (byte-identical round-trip) |
| 0. Sortir | Exit the application |

### Project submenu (option 1)

| Option | Description |
|--------|-------------|
| 1. Project Info | Name, version, path, creation date, device count |
| 2. Network Devices Tree | Console tree of all devices and their network interfaces |
| 3. Export Docs → 6 CSV files | Generates `Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv`, `IoTags.csv` and `ModuleAddresses.csv` from the open project (inverse of the generation pipeline; the I/O tags are extracted from the PLC tag tables, and both tags and module addresses are associated to the hardware modules by address range) |
| 4. Generate Catalog → Catalog.csv | Seeds/merges the catalog from the open project |
| 0. Exit | Back to the main menu |

### Generation status

- **Status: complete for Siemens devices; GSD third-party devices require manual insertion.**
  - Siemens (S7-CPU, ET200SP IM+modules, subnets, interfaces): fully automated, verified (CPU21 + ETH03/04/05, 65 modules plugged, 2 subnets).
  - GSD devices (SEW, DFE/MOVIFIT, FANUC): `CreateWithItem` fails with `Necessary license 'gsdml-...' is missing` (Openness enforces GSD licensing on the API path; GUI insertion works). Add them manually from the catalog. See ROADMAP "GSD" change log entry.
- **Reproduction of the manual I/Q hardware address map** (`ModuleAddresses.csv` + `ModuleAddressesGenerationService`): TIA auto-assigns dense addresses when creating hardware, while the reference projects use manual/sparse byte addresses. The pipeline re-assigns `Address.StartAddress` of every module/submodule to the source values. Verified against TEST_TIAO (74 addresses applied; ETH03/ETH04/ETH05 match the source exactly) and via the headless round-trip (`HwAddr` discrepancies reduced to the 60 GSD-derived rows only).
  - The I/Q address space is **shared** across I/O devices, so conflicts (`"This address is already being used"`) are resolved **globally** in three passes: (A) park every pending address in a high zone found by probing, (A2) park any other project address (unmapped modules, server modules, other devices) whose range overlaps a target, (B) assign the final sparse values.
- **I/O tag generation** (`IoTags.csv` + `TagsGenerationService`): recreates the PLC tag tables and their tags (name/type/address/comment), idempotently.

## Project Structure

```
TiaOpennessApp/
├── Program.cs                  # CLI flows, menu and orchestration
├── Services/
│   ├── TiaConnection.cs             # Assembly resolve, portal connect (headless/UI/auto), open project, cleanup
│   ├── ProjectService.cs            # Project metadata queries
│   ├── ProjectCreationService.cs    # Empty project creation via ProjectComposition.Create
│   ├── DevicesAndNetworksService.cs # Device/interfaces/modules extraction, console tree, JSON export
│   ├── TagsExportService.cs         # I/O tags extraction (PLC tag tables) + module addresses; association by address range
│   ├── CatalogService.cs            # Catalog.csv load/save/merge + candidate generation (no TIA)
│   ├── CsvGenerationReader.cs       # GenerationDocument from the input CSVs (incl. IoTags, ModuleAddresses)
│   ├── CsvGenerationWriter.cs       # CSV serialization of export documents (6 CSVs incl. IoTags, ModuleAddresses)
│   ├── ExcelGenerationReader.cs     # GenerationDocument from XLSX
│   ├── ExcelTemplateGenerator.cs    # Blank XLSX template with catalog dropdowns
│   ├── IGenerationReader.cs         # Common read interface
│   ├── GenerationColumns.cs         # Column definitions for Devices/Modules/Subnets/Interfaces
│   ├── GenerationRowParsing.cs      # Row parsers + type/cell parsing helpers
│   ├── ValidationService.cs         # Dry-run validation rules (names, IPs, slots, catalog, folder)
│   ├── DevicesGenerationService.cs  # Device create (idempotent, try/catch per item)
│   ├── ModulesGenerationService.cs  # Module PlugNew per slot (idempotent)
│   ├── ModuleAddressesGenerationService.cs # Re-assigns Address.StartAddress to the source map (global conflict resolution)
│   ├── TagsGenerationService.cs     # Recreates PLC tag tables + tags from IoTags (idempotent)
│   ├── SubnetsGenerationService.cs  # Subnet creation (System:Subnet.Ethernet/Profibus)
│   ├── InterfacesGenerationService.cs # Node IP/subnet assignment + ConnectToSubnet
│   ├── TopologyDiffService.cs       # Compares two exported topologies
│   └── DocsExportService.cs         # Exports the docs CSVs from a project or JSON
├── Models/
│   ├── NetworkDeviceInfo.cs         # NetworkDeviceInfo, NetworkInterfaceInfo, NetworkModuleInfo
│   ├── TopologyDiff.cs              # TopologyDiffReport / TopologyDiffItem / KnownGap
│   └── Generation/                  # GenerationDocument, DeviceSpec, ModuleSpec, SubnetSpec,
│                                   # InterfaceSpec, IoTagSpec, ModuleAddressSpec, CatalogEntry,
│                                   # ParseIssue, GenerationParseResult, ValidationReport, ValidationIssue
├── CLI.md                        # Full CLI reference (all commands)
├── FUTURE-WORK.md                # Proposed future improvements
├── ROADMAP.md                    # Phased execution plan (device generation from documents)
├── appsettings.json
├── app.config
└── TiaOpennessApp.csproj
```

## Key API Patterns Used

```csharp
// Connect headless (no UI)
var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);

// Read any attribute via interface
IEngineeringObject obj = (IEngineeringObject)deviceItem;
string typeName = (string)obj.GetAttribute("TypeName");

// Device groups ("Devices & Networks")
foreach (DeviceGroup group in project.DeviceGroups) { string n = group.Name; }
if (group is DeviceUserGroup userGroup)
    foreach (DeviceUserGroup child in userGroup.Groups) { ... }
foreach (Device device in project.UngroupedDevicesGroup.Devices) { ... }

// Subnets and node interfaces
foreach (Subnet subnet in project.Subnets)
    foreach (Node node in subnet.Nodes) { string host = node.Parent.ToString(); }
```

## Roadmap

See [`ROADMAP.md`](./ROADMAP.md) for the phased plan:

- P0–P9: build, import models, reference catalog, CSV/XLSX readers, validator
  + dry-run, and device/module/subnet/interface **generation** in TIA — **all complete and approved**.
- Beyond the plan (same pipeline): manual I/Q **hardware address map** reproduction and **I/O tag
  table** generation — complete and verified via the headless round-trip.
- F10+: PLC blocks, DBs and HMI (next steps, same pipeline).