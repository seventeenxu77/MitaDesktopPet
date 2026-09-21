[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$source = Join-Path $project "Assets\DesktopPet"
$sourceMeta = Join-Path $project "Assets\DesktopPet.meta"
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "DesktopPet source folder was not found: $source"
}
if (-not (Test-Path -LiteralPath $sourceMeta -PathType Leaf)) {
    throw "DesktopPet.meta was not found: $sourceMeta"
}

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$destination = Join-Path $repository "unity-overlay\Assets\DesktopPet"
$destinationMeta = Join-Path $repository "unity-overlay\Assets\DesktopPet.meta"

if ($PSCmdlet.ShouldProcess($destination, "Update the tracked Unity overlay from the development project")) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath $sourceMeta -Destination $destinationMeta -Force
}

$count = (Get-ChildItem -LiteralPath $destination -Recurse -File).Count
Write-Host "Synchronized $count desktop-pet files into $destination"
Write-Warning "This safe sync does not delete repository files that were deleted in Unity. Review git status and remove obsolete files deliberately."
git -C $repository status --short
