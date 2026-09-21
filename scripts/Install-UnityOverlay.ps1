[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$assets = Join-Path $project "Assets"
if (-not (Test-Path -LiteralPath $assets -PathType Container)) {
    throw "The selected directory is not a Unity project: $project"
}

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$source = Join-Path $repository "unity-overlay\Assets\DesktopPet"
$sourceMeta = Join-Path $repository "unity-overlay\Assets\DesktopPet.meta"
$destination = Join-Path $assets "DesktopPet"
$destinationMeta = Join-Path $assets "DesktopPet.meta"

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Unity overlay is missing: $source"
}

if ($PSCmdlet.ShouldProcess($destination, "Install Mita desktop-pet Unity overlay")) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath $sourceMeta -Destination $destinationMeta -Force
}

$count = (Get-ChildItem -LiteralPath $destination -Recurse -File).Count
Write-Host "Installed $count desktop-pet files into $destination"
Write-Host "Open Unity and run the Desktop Pet update menu command."
