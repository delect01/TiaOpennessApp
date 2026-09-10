# TIA Portal Openness - Base Application

## Prerequisites

- TIA Portal V21 installed with Openness option
- .NET Framework 4.8 or later
- Visual Studio 2019 or later (recommended)

## Setup

1. Set the environment variable `TIA_OPENNESS_PATH` to the TIA Portal Openness installation directory:

```powershell
# Example for TIA Portal V21:
$env:TIA_OPENNESS_PATH = "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21"
```

2. Update `appsettings.json` with your TIA Portal project path:

```json
{
  "TiaPortal": {
    "ProjectPath": "C:\\Path\\To\\Your\\TIA\\Project\\MyProject.ap21"
  }
}
```

## Build and Run

```powershell
# From the project directory:
dotnet build
dotnet run
```

## Features

- Opens TIA Portal project using Openness API
- Lists all devices and device items in the project
- Proper error handling and cleanup

## Next Steps

- Add hardware configuration modifications
- Implement PLC program import/export
- Add HMI screen management
- Create batch processing capabilities
