[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\JM Server",
    [string]$ListenIp = "192.168.0.10",
    [ValidateRange(1024, 65535)]
    [int]$ListenPort = 15570,
    [string]$DatabaseHost = "192.168.0.148",
    [string]$DatabaseName = "jm_server",
    [string]$DatabaseUsername = "jm_server",
    [string[]]$AllowedRemoteAddress,
    [switch]$AllowAnyRemoteAddress
)

$ErrorActionPreference = "Stop"
$serviceName = "JmServer"
$productName = -join [char[]](0xC815, 0xB9CC, 0xC11C, 0xBC84)
$executableName = "$productName.exe"
$firewallName = "$productName TCP $ListenPort"
$sourceDirectory = $PSScriptRoot

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this installer from an elevated PowerShell window."
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Service '$serviceName' already exists. Refusing to overwrite an existing installation."
}

$serverExecutable = Join-Path $sourceDirectory $executableName
if (-not (Test-Path -LiteralPath $serverExecutable)) {
    throw "$executableName was not found next to this installer."
}

$databasePasswordSecure = Read-Host "Password for PostgreSQL role '$DatabaseUsername'" -AsSecureString
$databasePasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($databasePasswordSecure)
try {
    $databasePassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($databasePasswordPointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($databasePasswordPointer)
}

$randomBytes = [byte[]]::new(32)
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($randomBytes)
}
finally {
    $randomGenerator.Dispose()
}
$pfxPassword = [Convert]::ToBase64String($randomBytes).Replace("+", "-").Replace("/", "_").TrimEnd("=")
$pfxSecurePassword = ConvertTo-SecureString $pfxPassword -AsPlainText -Force

$certificate = New-SelfSignedCertificate `
    -Subject "CN=JM Server" `
    -DnsName "jm-server" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(2)

try {
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $InstallDirectory -Recurse -Force

    $pfxPath = Join-Path $InstallDirectory "jm-server.pfx"
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $pfxSecurePassword | Out-Null

    $connectionString = "Host=$DatabaseHost;Port=5432;Database=$DatabaseName;Username=$DatabaseUsername;" +
        "Password=$databasePassword;SSL Mode=Require;Timeout=5;Command Timeout=15;Application Name=JM Server"
    $settings = [ordered]@{
        JM_DATABASE        = $connectionString
        JM_LISTEN_IP       = $ListenIp
        JM_LISTEN_PORT     = $ListenPort.ToString([Globalization.CultureInfo]::InvariantCulture)
        JM_TLS_CERTIFICATE = $pfxPath
        JM_TLS_PASSWORD    = $pfxPassword
    }
    $settingsPath = Join-Path $InstallDirectory "appsettings.Local.json"
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    $system = [Security.Principal.SecurityIdentifier]::new("S-1-5-18").Translate([Security.Principal.NTAccount])
    $administrators = [Security.Principal.SecurityIdentifier]::new("S-1-5-32-544").Translate([Security.Principal.NTAccount])
    $localService = [Security.Principal.SecurityIdentifier]::new("S-1-5-19").Translate([Security.Principal.NTAccount])
    foreach ($protectedFile in @($settingsPath, $pfxPath)) {
        $acl = Get-Acl -LiteralPath $protectedFile
        $acl.SetAccessRuleProtection($true, $false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($system, "FullControl", "Allow"))
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($administrators, "FullControl", "Allow"))
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($localService, "Read", "Allow"))
        Set-Acl -LiteralPath $protectedFile -AclObject $acl
    }

    $installedExecutable = Join-Path $InstallDirectory $executableName
    & sc.exe create $serviceName binPath= "`"$installedExecutable`"" start= auto `
        obj= "NT AUTHORITY\LocalService" DisplayName= $productName
    if ($LASTEXITCODE -ne 0) {
        throw "Windows service creation failed with exit code $LASTEXITCODE."
    }
    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

    if ($AllowedRemoteAddress.Count -gt 0 -or $AllowAnyRemoteAddress) {
        $remoteAddress = if ($AllowAnyRemoteAddress) { "Any" } else { $AllowedRemoteAddress }
        New-NetFirewallRule `
            -DisplayName $firewallName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $ListenPort `
            -RemoteAddress $remoteAddress `
            -Profile Any | Out-Null
    }

    Start-Service -Name $serviceName
    $service = Get-Service -Name $serviceName
    $sha256Algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $sha256 = $sha256Algorithm.ComputeHash($certificate.RawData)
    }
    finally {
        $sha256Algorithm.Dispose()
    }
    $certificatePin = ([BitConverter]::ToString($sha256)).Replace("-", "")

    Write-Host "$productName service state: $($service.Status)"
    Write-Host "Listen endpoint: $ListenIp`:$ListenPort"
    Write-Host "Client certificate SHA-256: $certificatePin"
    if ($AllowedRemoteAddress.Count -eq 0 -and -not $AllowAnyRemoteAddress) {
        Write-Warning "No firewall rule was created. Re-run firewall configuration after choosing allowed client addresses."
    }
}
finally {
    Remove-Item -LiteralPath "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    if ($null -ne $databasePassword) {
        $databasePassword = $null
    }
    $pfxPassword = $null
}
