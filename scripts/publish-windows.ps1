[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts\jm-server-win-x64",
    [string]$ArchivePath = "artifacts\jm-server-win-x64.zip"
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

dotnet publish (Join-Path $repositoryRoot "src\JmServer.Server\JmServer.Server.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "Windows server publish failed with exit code $LASTEXITCODE."
}

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "deploy\windows\install-jm-server.ps1") `
    -Destination $outputPath
Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "deploy\windows\upgrade-jm-server.ps1") `
    -Destination $outputPath
Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "deploy\windows\README.md") `
    -Destination $outputPath

Get-ChildItem -LiteralPath $outputPath -Filter "*.pdb" -File |
    Remove-Item -Force

Compress-Archive `
    -Path (Join-Path $outputPath "*") `
    -DestinationPath $archiveFullPath `
    -CompressionLevel Optimal `
    -Force

Write-Host "Windows package created at $outputPath"
Write-Host "Windows server archive created at $archiveFullPath"
