[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$GamePort = 15571,
    [string[]]$AllowedRemoteAddress,
    [switch]$AllowAnyRemoteAddress
)

$ErrorActionPreference = "Stop"
$productName = -join [char[]](0xC815, 0xB9CC, 0xC11C, 0xBC84)
$gameLabel = -join [char[]](0xAC8C, 0xC784)
$rulePrefix = "$productName $gameLabel TCP"
$ruleName = "$rulePrefix $GamePort"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

if ($AllowedRemoteAddress.Count -eq 0 -and -not $AllowAnyRemoteAddress) {
    throw "Specify the friend's public IP with -AllowedRemoteAddress, or use -AllowAnyRemoteAddress for testing."
}

$remoteAddress = if ($AllowAnyRemoteAddress) { "Any" } else { $AllowedRemoteAddress }
Get-NetFirewallRule -DisplayName "$rulePrefix *" -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule
New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $GamePort `
    -RemoteAddress $remoteAddress `
    -Profile Any | Out-Null

Write-Host "$ruleName inbound firewall rule applied."
