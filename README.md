# TIA Portal Openness - Engineering Tool (V21)

C# console application for reading and exporting TIA Portal V21 project data via the Openness API.

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
    "ProjectPath": "C:\\Users\\DPTO_ELECT\\Documents\\Automation\\AE82\\AE82.ap21",
    "Mode": "WithUserInterface",
    "OutputPath": "C:\\Users\\DPTO_ELECT\\Documents\\TIA Portal Openness\\Exports"
  }
}
```

`OutputPath` is used for every file result (JSON / XML / TXT). It can be:

- a **directory** (no file extension, or ends with `\`), in which case each export
  writes a default filename into it:
  `hardware_export.json`, `blocks_export.json`, `blocks_export\` (XML files),
  `devices_and_networks.json`, `io_channels.xml`.
- a **full file path**, used as-is for single-file exports (JSON / XML / TXT).

If `OutputPath` is empty, the app prompts for the destination at each menu.

## Features

| Menu | Description |
|------|-------------|
| 1. Project Info | Name, version, path, creation date, device count |
| 2. List Devices & HW Tree | Console tree of all devices, module items, addresses, positions |
| 3. Export Hardware → JSON | Full device + DeviceItem tree with order numbers, GSD data, I/O addresses to JSON |
| 4. List PLC Blocks | Console list of all OBs, FBs, FCs, DBs per CPU |
| 5. Export Blocks → JSON | Block metadata (name, number, language, memory size) to JSON |
| 6. Export Blocks → XML Files | One XML source file per block, organized by device/CPU folder |
| 7. Devices & Networks View | Device groups tree, ungrouped devices, subnets with nodes/interfaces |
| 8. Export Devices → JSON | Device groups and ungrouped devices (no networks/subnets) to JSON |
| 9. Export I/O Channels → XML | Station/Rack/Module/IOChannel with addresses and tags (AE82_IO_Channels.xml schema) |

## Project Structure

```
TiaOpennessApp/
├── Program.cs                  # CLI menu and orchestration
├── Services/
│   ├── TiaConnection.cs             # Openness assembly loading, portal connect, project open, cleanup
│   ├── DevicesAndNetworksService.cs # Device groups, subnets, nodes ("Devices & Networks" view)
│   ├── IoChannelService.cs          # I/O channel extraction per station/rack/module + XML export
│   ├── HardwareService.cs           # Device tree, HW attribute extraction, JSON export models
│   ├── BlockService.cs              # PLC block enumeration, XML source export
│   └── ProjectService.cs            # Project metadata queries
├── Models/
│   ├── DevicesAndNetworksInfo.cs    # DevicesAndNetworksReport, DeviceGroupInfo, SubnetInfo, NodeInfo
│   ├── IoChannelInfo.cs             # IoChannelExportReport, IoStationInfo, IoRackInfo, IoModuleInfo, IoChannelInfo
│   ├── HardwareInfo.cs              # HardwareInfo, DeviceSummary, HardwareExportReport, ProjectMetadata
│   └── BlockInfo.cs                 # BlockInfo, BlockExportReport
├── appsettings.json
├── app.config
└── TiaOpennessApp.csproj
```

## Key API Patterns Used

```csharp
// Connect headless (no UI)
var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);

// Access PLC software from a device item
SoftwareContainer sc = ((IEngineeringServiceProvider)deviceItem).GetService<SoftwareContainer>();
PlcSoftware sw = sc.Software as PlcSoftware;

// Iterate blocks
foreach (PlcBlock block in sw.BlockGroup.Blocks) { ... }

// GSD attributes
GsdDeviceItem gsd = ((IEngineeringServiceProvider)deviceItem).GetService<GsdDeviceItem>();
string gsdName = gsd.GsdName;

// Device groups ("Devices & Networks")
foreach (DeviceGroup group in project.DeviceGroups) { string n = group.Name; }
if (group is DeviceUserGroup userGroup)
    foreach (DeviceUserGroup child in userGroup.Groups) { ... }
foreach (Device device in project.UngroupedDevicesGroup.Devices) { ... }

// Subnets and node interfaces
foreach (Subnet subnet in project.Subnets)
    foreach (Node node in subnet.Nodes) { string host = node.Parent.ToString(); }

// I/O channels per module
foreach (DeviceItem item in allDeviceItems)
    foreach (Channel channel in item.Channels) { int n = channel.Number; }

// Export block source XML
block.Export(new FileInfo(path), ExportOptions.WithDefaults);

// Read any attribute via interface
IEngineeringObject obj = (IEngineeringObject)deviceItem;
string typeName = (string)obj.GetAttribute("TypeName");
```

## Next Steps

- [x] Devices & Networks view (groups, devices, subnets, nodes)
- [x] I/O channel export (stations, racks, modules, addresses, tags)
- [ ] Block interface member export (PlcBlockInterface)
- [ ] Tag table export
- [ ] PLC type (UDT / PLC data type) export
- [ ] Project comparison / diff
- [ ] Device group organization queries
- [ ] Subnet and network view export
