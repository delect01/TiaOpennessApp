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
cd "C:\Users\DPTO_ELECT\Documents\TIA Portal Openness\TiaOpennessApp"
dotnet build
```

## Allowlist Registration (after every rebuild)

```cmd
fix_allowlist.bat
```
Run as Administrator. Updates the HKLM registry entry with the current exe hash.

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
| 0. Sortir | Exit the application |

### Project submenu (option 1)

| Option | Description |
|--------|-------------|
| 1. Project Info | Name, version, path, creation date, device count |
| 2. Network Devices Tree | Console tree of all devices and their network interfaces |
| 3. Export Docs → 4 CSV files | Generates `Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv` from the open project (inverse of the generation pipeline) |
| 4. Generate Catalog → Catalog.csv | Seeds/merges the catalog from the open project |
| 0. Exit | Back to the main menu |

### Generation status

- **Status: complete for Siemens devices; GSD third-party devices require manual insertion.**
  - Siemens (S7-CPU, ET200SP IM+modules, subnets, interfaces): fully automated, verified (CPU21 + ETH03/04/05, 65 modules plugged, 2 subnets).
  - GSD devices (SEW, DFE/MOVIFIT, FANUC): `CreateWithItem` fails with `Necessary license 'gsdml-...' is missing` (Openness enforces GSD licensing on the API path; GUI insertion works). Add them manually from the catalog. See ROADMAP "GSD" change log entry.

## Project Structure

```
TiaOpennessApp/
├── Program.cs                  # CLI flows, menu and orchestration
├── Services/
│   ├── TiaConnection.cs             # Assembly resolve, portal connect (headless/UI/auto), open project, cleanup
│   ├── ProjectService.cs            # Project metadata queries
│   ├── ProjectCreationService.cs    # Empty project creation via ProjectComposition.Create
│   ├── DevicesAndNetworksService.cs # Device/interfaces/modules extraction, console tree, JSON export
│   ├── CatalogService.cs            # Catalog.csv load/save/merge + candidate generation (no TIA)
│   ├── CsvGenerationReader.cs       # GenerationDocument from the 4 input CSVs
│   ├── CsvGenerationWriter.cs       # CSV serialization of export documents
│   ├── ExcelGenerationReader.cs     # GenerationDocument from XLSX
│   ├── ExcelTemplateGenerator.cs    # Blank XLSX template with catalog dropdowns
│   ├── IGenerationReader.cs         # Common read interface
│   ├── GenerationColumns.cs         # Column definitions for Devices/Modules/Subnets/Interfaces
│   ├── GenerationRowParsing.cs      # Row parsers + type/cell parsing helpers
│   ├── ValidationService.cs         # Dry-run validation rules (names, IPs, slots, catalog, folder)
│   ├── DevicesGenerationService.cs  # Device create (idempotent, try/catch per item)
│   ├── ModulesGenerationService.cs  # Module PlugNew per slot (idempotent)
│   ├── SubnetsGenerationService.cs  # Subnet creation (System:Subnet.Ethernet/Profibus)
│   ├── InterfacesGenerationService.cs # Node IP/subnet assignment + ConnectToSubnet
│   ├── TopologyDiffService.cs       # Compares two exported topologies
│   └── DocsExportService.cs         # Exports the 4 docs CSVs from a project or JSON
├── Models/
│   ├── NetworkDeviceInfo.cs         # NetworkDeviceInfo, NetworkInterfaceInfo, NetworkModuleInfo
│   ├── TopologyDiff.cs              # TopologyDiffReport / TopologyDiffItem / KnownGap
│   └── Generation/                  # GenerationDocument, DeviceSpec, ModuleSpec, SubnetSpec,
│                                   # InterfaceSpec, CatalogEntry, ParseIssue, GenerationParseResult,
│                                   # ValidationReport, ValidationIssue
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
- F10+: PLC blocks, DBs, tag tables and HMI (next steps, same pipeline).