# Installer project

`Product.wxs` defines the MSI package. `Bundle.wxs` defines the Burn bootstrapper and prerequisite checks. `pack-installer.ps1` builds the application from the Debug configuration by default and creates `dist\TiaOpennessApp-Setup.exe`.

Run the script from the repository root after installing WiX Toolset v4 and adding `wix.exe` to `PATH`.
