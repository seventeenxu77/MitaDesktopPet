[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$GPTSoVITSPath
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$manifestPath = Join-Path $repository "external-assets.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$roots = @{
    unity = (Resolve-Path -LiteralPath $ProjectPath).Path
    gptSoVits = (Resolve-Path -LiteralPath $GPTSoVITSPath).Path
}

$failed = 0
foreach ($groupName in @("unity", "gptSoVits")) {
    foreach ($item in $manifest.$groupName) {
        $relativePath = $item.path.Replace("/", [IO.Path]::DirectorySeparatorChar)
        $path = Join-Path $roots[$groupName] $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Write-Host "MISSING [$groupName] $($item.path)" -ForegroundColor Red
            $failed++
            continue
        }

        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual -ne $item.sha256) {
            Write-Host "HASH MISMATCH [$groupName] $($item.path)" -ForegroundColor Red
            Write-Host "  expected $($item.sha256)"
            Write-Host "  actual   $actual"
            $failed++
            continue
        }

        Write-Host "OK [$groupName] $($item.path)" -ForegroundColor Green
    }
}

if ($failed -gt 0) {
    throw "$failed external asset checks failed."
}

Write-Host "All external assets match the recorded development snapshot." -ForegroundColor Green
