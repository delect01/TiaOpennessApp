# TIA Portal Openness - Engineering Tool (V21)

C# console application (.NET Framework 4.8) that connects to TIA Portal V21 via
the Openness API to inspect and export project data.

> **Scope of this document**: current state of the application (read / export).
> The evolution plan (device generation from documents) lives in
> [`ROADMAP.md`](./ROADMAP.md).

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

Project path and output location are read from `appsettings.json` (or provided at runtime):

```json
{
  "TiaPortal": {
    "Mode": "Auto",
    "Version": "21",
    "ProjectPath": "C:\\Users\\DPTO_ELECT\\Documents\\Automation",
    "ProjectName": "AE82",
    "ExportPath": "C:\\Users\\DPTO_ELECT\\Documents\\TIA Portal Openness\\exports",
    "ImportPath": "C:\\Users\\DPTO_ELECT\\Documents\\TIA Portal Openness\\imports",
    "NewProjectPath": "C:\\Users\\DPTO_ELECT\\Documents\\Automation",
    "NewProjectName": "AE82_New"
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

| Menu | Description |
|------|-------------|
| 1. Project Info | Name, version, path, creation date, device count |
| 2. Network Devices Tree | Console tree of all devices and their network interfaces |
| 3. Export Docs → 4 CSV files | Generates `Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv` from the open project (inverse of the generation pipeline) |

Plus three top-level flows:

- **Open an existing project** (reads `TiaPortal:ProjectPath`/`ProjectName`)
- **Create a new project** (`ProjectComposition.Create`, validates an empty destination folder)
- **Generate project from documents** (full pipeline: parse -> validate -> devices/modules/subnets/interfaces -> `Save()`; no auto-comparison; use `--diff-topology` to compare two JSON exports)
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
│   └── DevicesAndNetworksService.cs # Device/interfaces/modules extraction, console tree, JSON export
├── Models/
│   └── NetworkDeviceInfo.cs         # NetworkDeviceInfo, NetworkInterfaceInfo, NetworkModuleInfo
├── ROADMAP.md                   # Phased execution plan (device generation from documents)
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
  + dry-run, and eventually device/module/subnet/interface **generation** in TIA.
- F10+: PLC blocks, DBs, tag tables and HMI (after P9 is approved).