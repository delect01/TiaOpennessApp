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

## Notas técnicas relevantes

- **AllowList**: el hash de cada exe debe registrarse en `HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList`
- **DLLs de Siemens**: resueltas vía `app.config` con `codeBase` (no copiar localmente, `Private=false`)
- **Idioma de desarrollo**: C# (.NET Framework 4.8)
- **TIA Portal**: V21, ruta Openness `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`