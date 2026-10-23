# Installer technology comparison

| Technology | Strengths | Limitations | Suitability |
|---|---|---|---|
| WiX Toolset v4 + Burn | MSI plus EXE bootstrapper, registry searches, prerequisite conditions, enterprise deployment and silent install support | More authoring and build setup | Recommended for TiaOpennessApp |
| Inno Setup | Fast to create, straightforward EXE, good scripting support | Not MSI-based; enterprise deployment and prerequisite modeling are less standardized | Good simple alternative |
| Visual Studio Installer Project | Familiar Visual Studio workflow and basic MSI output | Limited prerequisite detection and customization; extra extension required | Suitable for a basic MSI only |
| ClickOnce | Built-in application deployment and update flow | Not intended for machine-level dependencies such as TIA Portal Openness | Not recommended |

WiX was selected because this application requires .NET Framework 4.8 and a specific TIA Portal installation, and the installer must stop with a clear message when those prerequisites are absent.
