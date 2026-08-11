[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\JM Server",
    [ValidateRange(1024, 65535)]
    [int]$ListenPort = 15570
)

$ErrorActionPreference = "Stop"
$serviceName = "JmServer"
$productName = -join [char[]](0xC815, 0xB9CC, 0xC11C, 0xBC84)
$executableName = "$productName.exe"
$sourceExecutable = Join-Path $PSScriptRoot $executableName
$installedExecutable = Join-Path $InstallDirectory $executableName
$legacyInstalledExecutable = Join-Path $InstallDirectory "JmServer.Server.exe"
$settingsPath = Join-Path $InstallDirectory "appsettings.Local.json"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this upgrade from an elevated PowerShell window."
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "$executableName was not found next to this upgrade script."
}

$previousExecutable = if (Test-Path -LiteralPath $installedExecutable) {
    $installedExecutable
}
elseif (Test-Path -LiteralPath $legacyInstalledExecutable) {
    $legacyInstalledExecutable
}
else {
    throw "The installed $productName executable was not found in '$InstallDirectory'."
}

$backupDirectory = Join-Path $InstallDirectory ("backup\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$backupExecutable = Join-Path $backupDirectory (Split-Path -Leaf $previousExecutable)
Copy-Item -LiteralPath $previousExecutable -Destination $backupExecutable
$backupSettings = Join-Path $backupDirectory "appsettings.Local.json"
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "The installed local settings file was not found: $settingsPath"
}
Copy-Item -LiteralPath $settingsPath -Destination $backupSettings
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$previousListenPort = [int]$settings.JM_LISTEN_PORT
$firewallRuleName = "JmServer TCP $ListenPort"
$firewallRuleSnapshots = @(
    Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object {
            $_.DisplayName -like "JmServer TCP *" -or
            $_.DisplayName -like "$productName TCP *"
        } |
        Select-Object Name,DisplayName
)
$createdFirewallRule = $false

try {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $serviceName
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }

    $settings.JM_LISTEN_PORT = $ListenPort.ToString([Globalization.CultureInfo]::InvariantCulture)
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    Copy-Item -LiteralPath $sourceExecutable -Destination $installedExecutable -Force
    & $installedExecutable migrate
    if ($LASTEXITCODE -ne 0) {
        throw "Database migration failed with exit code $LASTEXITCODE."
    }

    & sc.exe config $serviceName binPath= "`"$installedExecutable`"" DisplayName= $productName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Windows service update failed with exit code $LASTEXITCODE."
    }

    foreach ($firewallRuleSnapshot in $firewallRuleSnapshots) {
        $firewallRule = Get-NetFirewallRule -Name $firewallRuleSnapshot.Name
        $firewallRule | Get-NetFirewallPortFilter |
            Set-NetFirewallPortFilter -LocalPort $ListenPort | Out-Null
        $firewallRule | Set-NetFirewallRule -NewDisplayName $firewallRuleName | Out-Null
    }
    if ($firewallRuleSnapshots.Count -eq 0) {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $ListenPort `
            -RemoteAddress Any `
            -Profile Any | Out-Null
        $createdFirewallRule = $true
    }

    Start-Service -Name $serviceName
    $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

    if ($previousExecutable -ne $installedExecutable -and
        (Test-Path -LiteralPath $previousExecutable)) {
        Remove-Item -LiteralPath $previousExecutable -Force
    }

    $hash = (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
    Write-Host "$productName upgrade complete."
    Write-Host "Listen port: $ListenPort"
    Write-Host "Executable SHA-256: $hash"
    Write-Host "Previous executable: $backupExecutable"
}
catch {
    if ((Get-Service -Name $serviceName).Status -ne "Stopped") {
        Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
    }

    if ($previousExecutable -ne $installedExecutable -and
        (Test-Path -LiteralPath $installedExecutable)) {
        Remove-Item -LiteralPath $installedExecutable -Force
    }

    Copy-Item -LiteralPath $backupExecutable -Destination $previousExecutable -Force
    Copy-Item -LiteralPath $backupSettings -Destination $settingsPath -Force
    if ($createdFirewallRule) {
        Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule
    }
    else {
        foreach ($firewallRuleSnapshot in $firewallRuleSnapshots) {
            $firewallRule = Get-NetFirewallRule `
                -Name $firewallRuleSnapshot.Name `
                -ErrorAction SilentlyContinue
            $firewallRule | Get-NetFirewallPortFilter |
                Set-NetFirewallPortFilter `
                    -LocalPort $previousListenPort `
                    -ErrorAction SilentlyContinue | Out-Null
            $firewallRule | Set-NetFirewallRule `
                -NewDisplayName $firewallRuleSnapshot.DisplayName `
                -ErrorAction SilentlyContinue | Out-Null
        }
    }
    & sc.exe config $serviceName binPath= "`"$previousExecutable`"" DisplayName= $productName | Out-Null
    Start-Service -Name $serviceName -ErrorAction SilentlyContinue
    throw
}
