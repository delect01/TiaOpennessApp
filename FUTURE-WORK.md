# Trabajos Futuros - TIA Portal Openness

> Lista de mejoras y extensiones propuestas para el proyecto `TiaOpennessApp`.
> Estado: Pendiente de planificación e implementación.

---

## Propuesta 1: Add-In de TIA Portal (VSIX)

Integrar la aplicación como un **Add-In de TIA Portal** empaquetado como VSIX.

### Beneficios
- Aparece como cinta/menú personalizado dentro de la UI de TIA Portal
- Comandos personalizados sobre el proyecto abierto
- Integración nativa con el entorno de ingeniería

### Recursos disponibles (ya instalados)
- `Siemens.Engineering.AddIn.Publisher.exe`
- `Siemens.Engineering.AddIn.*.dll` (Base, Step7, WinCC, Safety, Utilities)
- `Siemens.Engineering.AddIn.Publisher.xsd`
- NuGet: Microsoft.VisualStudio.SDK (para empaquetado VSIX)

### Funcionalidades candidatas
- Mostrar dispositivos del proyecto
- Exportar hardware a JSON
- Compilar bloques PLC
- Importar/exportar programas
- Edición HMI

### Referencias
- API XML: `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\Siemens.Engineering.AddIn.*.dll`
- Ejemplos: documentación Openness instalada con TIA Portal V21

---

## Propuesta 2: Extensión VSIX para Visual Studio

Paquete VSIX que agrega **menús/comandos a Visual Studio** que ejecutan operaciones Openness sobre TIA Portal.

### Beneficios
- Comandos de TIA Portal disponibles dentro de Visual Studio
- Unificar el flujo de trabajo de desarrollo de automatización con el editor
- Reutilizar el código existente de `TiaOpennessApp`

### Funcionalidades candidatas
- Build/compilación de bloques PLC desde Visual Studio
- Exportar/importar programas entre VS y TIA
- Comparación de versiones de proyecto
- Diagnóstico de conexión Openness

### Requisitos sugeridos
- Visual Studio Community 2022 (instalado)
- VSIX SDK
- Elevación de permisos para la AllowList de Openness

---

## Propuesta 3: Generar documentos de entrada (CSV/XLSX) desde un proyecto real

Inversa de l'exportació de dispositius de xarxa: a partir del proyecto TIA
abierto, generar
`imports/docs/{Devices,Modules,Subnets,Interfaces}.csv` (y opcionalmente el XLSX)
listos para el pipeline de generación (`--generate-project`).

### Detalles
- Fuente: `DevicesAndNetworksService.ExportNetworkDevices()`.
- Columnas objetivo en `GenerationColumns.cs`:
  - `Devices.csv`: `DeviceName,Group,TypeKey,Firmware,Comment`
  - `Modules.csv`: `Device,ModuleName,Slot,TypeKey,Comment`
  - `Subnets.csv`: `Name,SubnetType`
  - `Interfaces.csv`: `Device,InterfaceItem,Subnet,IPAddress,SubnetMask,PNDeviceName`
- Resolver `TypeKey` automáticamente contra `Catalog.csv` (TypeKey = clave del catálogo para ese MLFB/OrderNumber).

### Estado: IMPLEMENTADO
- Comando `--export-docs <projectPath|exportJson> [outDocsDir] [catalogCsv]`
  (CLI.md sección 13; `DocsExportService`, `Program.cs`).
- `NetworkInterfaceInfo` extendido con `SubnetMask` y `PnDeviceName`; el
  exportador los lee cuando el atributo existe.
- Test offline PASSED a partir de `exports/real_network_devices.json`
  (2 devices / 6 modules / 1 subnet / 3 interfaces, 0 TypeKeys faltantes).
- Round-trip real con TIA PASSED (2026-09-16): `--generate-project` desde
  `imports\docs` regeneró AE82_New (2 devices creados, 3 módulos plugueados,
  1 subnet, 3 interfaces) y `--diff-topology` real vs sandbox dio
  0 diferencias duras, 3 explicadas (naming de módulos sanitizado).
- Adoptado: `imports\docs` contiene los documentos generados desde el proyecto
  real AE82 (se sustituyó la muestra curada).

---

## Notas técnicas relevantes

- **AllowList**: el hash de cada exe debe registrarse en `HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList`
- **DLLs de Siemens**: resueltas vía `app.config` con `codeBase` (no copiar localmente, `Private=false`)
- **Idioma de desarrollo**: C# (.NET Framework 4.8)
- **TIA Portal**: V21, ruta Openness `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`