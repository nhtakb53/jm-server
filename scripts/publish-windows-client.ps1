[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts\jm-launcher-win-x64",
    [string]$ArchivePath = "artifacts\jm-launcher-win-x64.zip"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$archiveFullPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ArchivePath))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts")).TrimEnd("\") + "\"

if (-not $outputPath.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish output directory must be inside '$artifactsRoot'."
}
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

dotnet publish (Join-Path $repositoryRoot "src\JmServer.Launcher.Wpf\JmServer.Launcher.Wpf.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "Windows client publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $outputPath -Filter "*.pdb" -File |
    Remove-Item -Force

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "third_party\D2SSharp-LICENSE.txt") `
    -Destination $outputPath

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "docs\client-quickstart.ko.md") `
    -Destination (Join-Path $outputPath "README.ko.md")

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "docs\unidentified-item-supply.ko.md") `
    -Destination (Join-Path $outputPath "unidentified-item-supply.ko.md")

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "docs\qol-features.ko.md") `
    -Destination (Join-Path $outputPath "qol-features.ko.md")

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "deploy\windows\configure-jm-game-port.ps1") `
    -Destination $outputPath

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "src\JmServer.GameIntegration\ModData\jm-loot-filter.json") `
    -Destination (Join-Path $outputPath "jm-loot-filter.json")

if (-not (Test-Path -LiteralPath (Join-Path $outputPath "guide\index.html"))) {
    throw "The published Windows client is missing guide\index.html."
}

Compress-Archive `
    -Path (Join-Path $outputPath "*") `
    -DestinationPath $archiveFullPath `
    -CompressionLevel Optimal `
    -Force

Write-Host "Windows client directory created at $outputPath"
Write-Host "Windows client archive created at $archiveFullPath"
