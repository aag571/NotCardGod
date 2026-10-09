[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot
)

$sourceDirectory = Join-Path $ProjectRoot 'HybridCLRData\HotUpdateDlls\WebGL'
$targetDirectory = Join-Path $ProjectRoot 'Assets\GameAssets\Hotfix\Dll'
$sourceDll = Join-Path $sourceDirectory 'HotUpdate.dll'
$sourceBytes = Join-Path $sourceDirectory 'HotUpdate.dll.bytes'
$targetBytes = Join-Path $targetDirectory 'HotUpdate.dll.bytes'

if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "HybridCLR output directory does not exist: $sourceDirectory"
}

if (Test-Path -LiteralPath $sourceDll -PathType Leaf) {
    if (Test-Path -LiteralPath $sourceBytes -PathType Leaf) {
        Remove-Item -LiteralPath $sourceBytes -Force
    }

    Move-Item -LiteralPath $sourceDll -Destination $sourceBytes -Force
}

if (-not (Test-Path -LiteralPath $sourceBytes -PathType Leaf)) {
    throw "HotUpdate.dll or HotUpdate.dll.bytes was not found in: $sourceDirectory"
}

if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
}

Copy-Item -LiteralPath $sourceBytes -Destination $targetBytes -Force

$sourceInfo = Get-Item -LiteralPath $sourceBytes
$targetInfo = Get-Item -LiteralPath $targetBytes

Write-Output "Source: $($sourceInfo.FullName)"
Write-Output "Target: $($targetInfo.FullName)"
Write-Output "Size: $($targetInfo.Length) bytes"
