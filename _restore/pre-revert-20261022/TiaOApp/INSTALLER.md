 TiaOpennessApp installer

## Recommended technology

The installer uses WiX Toolset v4:

- **WiX + Burn**: creates an MSI and an EXE bootstrapper, supports registry prerequisite checks, and is suitable for managed Windows distribution.
- **Inno Setup**: simpler EXE-based packaging, but less suitable when MSI integration and enterprise deployment are required.
- **Visual Studio Installer Project**: easy for basic MSI packages, but has limited prerequisite and registry detection support.
- **ClickOnce**: useful for application updates, but not appropriate here because the application depends on TIA Portal and Openness being installed locally.

## Requirements

- Windows with TIA Portal V21 and the Openness option installed.
- .NET Framework 4.8 or later.
- WiX Toolset v4 with `wix.exe` available on `PATH`.
- .NET Framework 4.8 Developer Pack and the TIA Portal V21 PublicAPI assemblies available on the build machine.

The installer does not redistribute Siemens assemblies. The installed application resolves them from the TIA Portal installation directory.

## Build

For the current development packaging, the script uses the `Debug` configuration. Run it from the repository root, in an elevated PowerShell if required by the local build environment:

```powershell
.\Installer\pack-installer.ps1
```

The output is:

```text
dist\TiaOpennessApp-Setup.exe
```

The MSI is generated in `Installer\obj\pack` as an intermediate artifact.

Release packaging is intentionally deferred for now. When it is needed, pass `-Configuration Release` to the script.

## Installer checks

The Burn bootstrapper checks:

1. .NET Framework 4.8 using the .NET Framework `Release` registry value. If it is missing, installation stops with a download link.
2. Siemens TIA Portal V21 using:
   `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Siemens Installer Assistant - TIAP21`

If TIA Portal V21 is missing, installation stops. The detection key was verified on the development machine and should be revalidated for other TIA Portal editions or versions.

## Architecture

The application is built as AnyCPU. The MSI and bootstrapper are packaged as x86 so they can install on both 32-bit and 64-bit Windows. The application still uses the installed .NET Framework and Siemens API architecture.

## Testing

Test on machines representing these cases:

- .NET Framework 4.8 missing.
- TIA Portal V21 missing.
- Both prerequisites installed.
- A standard user account that can approve the UAC prompt.

For production distribution, sign `TiaOpennessApp-Setup.exe` with the organization's code-signing certificate.
