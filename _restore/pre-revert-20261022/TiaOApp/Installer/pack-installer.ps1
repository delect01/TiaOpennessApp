[CmdletBinding()]
param(
	[string]$Configuration = 'Debug',
	[string]$Version = '1.0.0.0'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $root 'TiaOpennessApp.csproj'
$appSource = Join-Path $root "bin\$Configuration\net48"
$work = Join-Path $PSScriptRoot 'obj\pack'
$dist = Join-Path $root 'dist'
$msi = Join-Path $work 'TiaOpennessApp.msi'
$bundle = Join-Path $dist 'TiaOpennessApp-Setup.exe'
$balExtension = Join-Path $env:USERPROFILE '.nuget\packages\wixtoolset.bal.wixext\6.0.2\wixext6\WixToolset.BootstrapperApplications.wixext.dll'
$utilExtension = Join-Path $env:USERPROFILE '.nuget\packages\wixtoolset.util.wixext\6.0.2\wixext6\WixToolset.Util.wixext.dll'

function Invoke-Wix {
	param([string[]]$Arguments)

	& wix @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "WiX failed with exit code $LASTEXITCODE."
	}
}

$wixCommand = Get-Command wix.exe -ErrorAction SilentlyContinue
if (-not $wixCommand) {
	throw 'WiX v4 was not found. Install the WiX Toolset SDK or add wix.exe to PATH.'
}
if (-not (Test-Path $balExtension) -or -not (Test-Path $utilExtension)) {
	throw 'WiX Burn/Util extensions were not found. Build Installer\TiaOpennessApp.Installer.wixproj once to restore them.'
}

if (-not (Test-Path 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll')) {
	throw 'TIA Portal V21 Openness was not found in the expected installation path.'
}

Write-Host 'Building TiaOpennessApp...'
dotnet build $appProject --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
	throw "dotnet build failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path (Join-Path $appSource 'TiaOpennessApp.exe'))) {
	throw "Application output was not found: $appSource"
}

New-Item -ItemType Directory -Force -Path $work, $dist | Out-Null
Remove-Item $msi, $bundle -Force -ErrorAction SilentlyContinue

Write-Host 'Building MSI...'
Invoke-Wix @(
	'build', '-arch', 'x86',
	'-d', "AppSourceDir=$appSource",
	'-d', "Version=$Version",
	(Join-Path $PSScriptRoot 'Product.wxs'),
	(Join-Path $PSScriptRoot 'AppFiles.wxs'),
	'-o', $msi
)

Write-Host 'Building Burn bootstrapper...'
Invoke-Wix @(
	'build', '-arch', 'x86',
	'-ext', $balExtension,
	'-ext', $utilExtension,
	'-d', "MsiPath=$msi",
	'-d', "Version=$Version",
	(Join-Path $PSScriptRoot 'Bundle.wxs'),
	'-o', $bundle
)

if (-not (Test-Path $bundle)) {
	throw "Bootstrapper output was not found: $bundle"
}
Write-Host "Installer created: $bundle"
