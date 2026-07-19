<#
.SYNOPSIS
  Provision, inspect, or destroy a Vast.ai GPU instance for GrokVideoStudio.
#>
param(
    [ValidateSet("4090", "A100", "H100", "Auto")]
    [string]$Tier = "Auto",
    [string]$VastApiKey = "",
    [switch]$Teardown,
    [switch]$ListInstances,
    [switch]$Status
)

$ErrorActionPreference = "Stop"
$script:VastCli = $null
$InstanceFile = Join-Path $PSScriptRoot ".vast_instance_id"
$DiskGb = 50
$RawServerBaseUrl = "https://raw.githubusercontent.com/Common-joeAI/Grok-Video-Studio_WPF/main/local_server"

function Write-Header([string]$Message) { Write-Host "`n==> $Message`n" -ForegroundColor Cyan }
function Write-Step([string]$Message) { Write-Host "  $Message" }
function Write-OK([string]$Message) { Write-Host "  $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "  $Message" -ForegroundColor Yellow }
function Write-Err([string]$Message) { Write-Host "  $Message" -ForegroundColor Red }

function Resolve-VastCli([string]$PythonExe = "") {
    foreach ($name in @("vastai", "vast")) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }

    if ($PythonExe) {
        $directories = @()
        try {
            $scriptsPath = & $PythonExe -c "import sysconfig; print(sysconfig.get_path('scripts'))" 2>$null | Select-Object -Last 1
            if ($scriptsPath) { $directories += $scriptsPath.Trim() }
        } catch { }
        try {
            $userBase = & $PythonExe -m site --user-base 2>$null | Select-Object -Last 1
            if ($userBase) { $directories += (Join-Path $userBase.Trim() "Scripts") }
        } catch { }

        foreach ($directory in ($directories | Where-Object { $_ } | Select-Object -Unique)) {
            foreach ($name in @("vastai.exe", "vastai", "vast.exe", "vast")) {
                $candidate = Join-Path $directory $name
                if (Test-Path $candidate) { return $candidate }
            }
        }
    }

    return $null
}

function Find-Python {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        foreach ($version in @("3.13", "3.12", "3.11", "3.10", "3.14")) {
            try {
                $path = & py -$version -c "import sys; print(sys.executable)" 2>$null | Select-Object -Last 1
                if ($path) {
                    $path = $path.Trim()
                    if (Test-Path $path) { return $path }
                }
            } catch { }
        }
    }

    $root = Join-Path $env:LOCALAPPDATA "Programs\Python"
    if (Test-Path $root) {
        foreach ($directory in (Get-ChildItem $root -Directory | Sort-Object Name -Descending)) {
            $path = Join-Path $directory.FullName "python.exe"
            if (Test-Path $path) { return $path }
        }
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) { return $python.Source }
    return $null
}

function Ensure-VastCli {
    Write-Step "Checking for Vast.ai CLI..."
    $script:VastCli = Resolve-VastCli
    if ($script:VastCli) {
        Write-OK "Vast CLI found: $script:VastCli"
        return $true
    }

    Write-Step "Installing the official Vast.ai CLI package (vastai)..."
    $python = Find-Python
    if (-not $python) {
        Write-Err "Python 3.10 or newer was not found."
        return $false
    }

    Write-Step "Using Python: $python"
    $output = & $python -m pip install --upgrade vastai 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Normal install failed; retrying with --user..."
        $output = & $python -m pip install --user --upgrade vastai 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Could not install Vast.ai CLI."
        $output | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
        return $false
    }

    $script:VastCli = Resolve-VastCli -PythonExe $python
    if (-not $script:VastCli) {
        Write-Err "vastai installed, but vastai.exe could not be located."
        return $false
    }

    Write-OK "Vast CLI installed: $script:VastCli"
    return $true
}

function Get-VastErrorMessage([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try {
        $parsed = $Text | ConvertFrom-Json
        if ($parsed.error -eq $true) {
            if ($parsed.msg) { return [string]$parsed.msg }
            return $Text
        }
    } catch { }
    return $null
}

function Invoke-Vast([string[]]$Arguments) {
    $output = & $script:VastCli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output -join "`n").Trim()
    $apiError = Get-VastErrorMessage $text

    if ($exitCode -ne 0 -or $apiError) {
        $message = if ($apiError) { $apiError } elseif ($text) { $text } else { "Unknown Vast.ai CLI error." }
        throw "vastai $($Arguments -join ' ') failed: $message"
    }

    return $output
}

function Normalize-VastApiKey([string]$Key) {
    if ([string]::IsNullOrWhiteSpace($Key)) { return $null }

    $candidate = $Key.Trim()
    if ($candidate -match '^(ghp_|github_pat_)') {
        Write-Err "The value entered is a GitHub token, not a Vast.ai API key."
        Write-Err "Create a Vast.ai key from the Vast.ai console Keys page."
        return $null
    }

    if ($candidate -notmatch '^[A-Fa-f0-9]{64}$') {
        Write-Err "Invalid Vast.ai API key format."
        Write-Err "A Vast.ai API key must contain exactly 64 hexadecimal characters."
        Write-Err "Do not paste a GitHub, OpenAI, xAI, or other provider token here."
        return $null
    }

    return $candidate.ToLowerInvariant()
}

function Get-VastUser {
    $json = (Invoke-Vast @("show", "user", "--raw")) -join "`n"
    $result = $json | ConvertFrom-Json
    if ($result.PSObject.Properties["user"]) { return $result.user }
    return $result
}

function Test-VastAuthentication {
    try {
        $null = Get-VastUser
        return $true
    } catch {
        Write-Err $_.Exception.Message
        return $false
    }
}

function Set-AndVerifyVastKey([string]$Key) {
    $normalized = Normalize-VastApiKey $Key
    if (-not $normalized) { return $false }

    $env:VAST_API_KEY = $normalized
    try {
        Invoke-Vast @("set", "api-key", $normalized) | Out-Null
    } catch {
        Write-Err $_.Exception.Message
        return $false
    }

    if (-not (Test-VastAuthentication)) {
        Write-Err "Vast.ai rejected the API key. Create or reset a key in the Vast.ai console."
        return $false
    }

    Write-OK "Vast.ai API key verified."
    return $true
}

function Ensure-ApiKey {
    if (-not [string]::IsNullOrWhiteSpace($VastApiKey)) {
        return Set-AndVerifyVastKey $VastApiKey
    }

    if (-not [string]::IsNullOrWhiteSpace($env:VAST_API_KEY)) {
        return Set-AndVerifyVastKey $env:VAST_API_KEY
    }

    if (Test-VastAuthentication) {
        Write-OK "Existing Vast.ai authentication is valid."
        return $true
    }

    Write-Warn "No valid Vast.ai API key is configured."
    Write-Host "  Create one in the Vast.ai console under Keys -> API Keys -> New."
    $key = Read-Host "  Vast.ai API key"
    if (-not $key) { return $false }
    return Set-AndVerifyVastKey $key
}

function Ensure-VastCredit {
    try {
        $user = Get-VastUser
        if (-not $user.PSObject.Properties["balance"]) {
            Write-Err "Vast.ai did not return an account balance."
            return $false
        }

        $balance = [double]$user.balance
        $displayBalance = $balance.ToString("0.00", [Globalization.CultureInfo]::InvariantCulture)
        if ($balance -le 0) {
            Write-Err "Vast.ai account credit: `$$displayBalance"
            Write-Err "Add prepaid credit on the Vast.ai Billing page before renting a GPU."
            return $false
        }

        Write-OK "Vast.ai account credit: `$$displayBalance"
        return $true
    } catch {
        Write-Err "Could not read Vast.ai account credit: $($_.Exception.Message)"
        return $false
    }
}

function Get-Query {
    switch ($Tier) {
        "4090" { return "gpu_name=RTX_4090 num_gpus=1 reliability>=0.95 disk_space>=$DiskGb rented=false" }
        "A100" { return "gpu_name=A100 num_gpus=1 gpu_ram>=70 reliability>=0.95 disk_space>=$DiskGb rented=false" }
        "H100" { return "gpu_name=H100 num_gpus=1 gpu_ram>=70 reliability>=0.95 disk_space>=$DiskGb rented=false" }
        default { return "gpu_ram>=16 num_gpus=1 reliability>=0.95 dph<=0.50 disk_space>=$DiskGb rented=false" }
    }
}

function Search-Offers {
    Write-Step "Searching for available GPU offers..."
    try {
        $json = (Invoke-Vast @("search", "offers", (Get-Query), "--order", "dph_total", "--limit", "5", "--raw")) -join "`n"
        $result = $json | ConvertFrom-Json
        $offers = if ($result.PSObject.Properties["offers"]) { @($result.offers) } else { @($result) }
        $offers = @($offers | Where-Object { $_.id })
        if ($offers.Count -eq 0) { throw "No matching offers were returned." }

        Write-Host ""
        for ($i = 0; $i -lt $offers.Count; $i++) {
            $offer = $offers[$i]
            $price = if ($null -ne $offer.dph_total) { [double]$offer.dph_total } else { [double]$offer.dph }
            Write-Host ('  [{0}] ID {1} | {2} | ${3:0.000}/hr' -f ($i + 1), $offer.id, $offer.gpu_name, $price)
        }

        if ($Tier -eq "Auto") { return [string]$offers[0].id }
        $choice = Read-Host "  Pick an offer [1-$($offers.Count), Enter for 1]"
        if (-not $choice) { $choice = 1 }
        $index = [Math]::Max(0, [Math]::Min($offers.Count - 1, ([int]$choice - 1)))
        return [string]$offers[$index].id
    } catch {
        Write-Err $_.Exception.Message
        return $null
    }
}

function New-OnstartFile {
    $bootstrapUrl = "$RawServerBaseUrl/vast_bootstrap.sh"
    $script = @"
#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl
curl --fail --location --retry 5 --retry-delay 3 '$bootstrapUrl' -o /tmp/gvs-bootstrap.sh
test -s /tmp/gvs-bootstrap.sh
chmod +x /tmp/gvs-bootstrap.sh
exec /tmp/gvs-bootstrap.sh
"@

    $byteCount = [Text.Encoding]::UTF8.GetByteCount($script)
    if ($byteCount -gt 12000) {
        throw "Generated startup script is unexpectedly large ($byteCount bytes)."
    }

    $path = Join-Path $env:TEMP ("gvs-vast-{0}.sh" -f [Guid]::NewGuid().ToString("N"))
    $script | Out-File $path -Encoding ascii -NoNewline
    Write-Step "Startup bootstrap request size: $byteCount bytes"
    return $path
}

function Create-Instance([string]$OfferId) {
    $onstart = $null
    try {
        $onstart = New-OnstartFile
        $json = (Invoke-Vast @(
            "create", "instance", $OfferId,
            "--image", "nvidia/cuda:12.4.0-cudnn-runtime-ubuntu22.04",
            "--disk", "$DiskGb",
            "--ssh",
            "--env", "-p 7860:7860/tcp",
            "--onstart", $onstart,
            "--label", "GrokVideoStudio",
            "--cancel-unavail",
            "--raw"
        )) -join "`n"

        $result = $json | ConvertFrom-Json
        $id = if ($result.new_contract) { $result.new_contract } elseif ($result.id) { $result.id } else { $null }
        if (-not $id) { throw "Instance was created but no instance ID was returned: $json" }

        [string]$id | Out-File $InstanceFile -Encoding ascii -NoNewline
        Write-OK "Instance created: $id"
        return [string]$id
    } catch {
        $message = $_.Exception.Message
        Write-Err $message
        if ($message -match "lacks credit") {
            Write-Warn "Add credit on the Vast.ai Billing page, then run provisioning again."
        }
        return $null
    } finally {
        if ($onstart -and (Test-Path $onstart)) {
            Remove-Item $onstart -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-Instance([string]$Id) {
    $json = (Invoke-Vast @("show", "instance", $Id, "--raw")) -join "`n"
    $result = $json | ConvertFrom-Json
    if ($result.PSObject.Properties["instances"]) {
        $instances = @($result.instances)
        if ($instances.Count -gt 0) { return $instances[0] }
    }
    return $result
}

function Wait-ForUrl([string]$Id) {
    Write-Step "Waiting for the instance and public port..."
    $deadline = (Get-Date).AddMinutes(10)

    while ((Get-Date) -lt $deadline) {
        try {
            $instance = Get-Instance $Id
            $ip = [string]$instance.public_ipaddr
            $port = $null

            if ($instance.ports) {
                $mapping = $instance.ports.PSObject.Properties["7860/tcp"]
                if ($mapping -and $mapping.Value) {
                    $entries = @($mapping.Value)
                    if ($entries.Count -gt 0) { $port = [string]$entries[0].HostPort }
                }
            }

            if ($ip -and $port) { return "http://${ip}:${port}" }
        } catch { }

        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep 5
    }

    Write-Host ""
    return $null
}

function Wait-ForHealth([string]$Url) {
    Write-Step "Waiting for the LTX-Video server to finish installing..."
    $deadline = (Get-Date).AddMinutes(20)

    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod "$($Url.TrimEnd('/'))/health" -TimeoutSec 10
            if ($health.status -eq "healthy") {
                Write-OK "Server is healthy. GPU: $($health.gpu_available); VRAM: $($health.total_vram_gb) GB"
                return $true
            }
        } catch { }

        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep 10
    }

    Write-Host ""
    Write-Warn "Server is not healthy yet. The instance is still active and billing."
    Write-Warn "The bootstrap log is /var/log/grok-video-studio-bootstrap.log on the instance."
    return $false
}

function Destroy-SavedInstance {
    if (-not (Test-Path $InstanceFile)) {
        Write-Warn "No saved instance ID found."
        return
    }

    $id = (Get-Content $InstanceFile -Raw).Trim()
    Invoke-Vast @("destroy", "instance", $id, "--raw") | Out-Null
    Remove-Item $InstanceFile -Force
    Write-OK "Instance $id destroyed. Billing stopped."
}

Write-Header "GrokVideoStudio -> Vast.ai Cloud GPU"
if (-not (Ensure-VastCli)) { exit 1 }
if (-not (Ensure-ApiKey)) { exit 1 }

if ($ListInstances) {
    Invoke-Vast @("show", "instances") | ForEach-Object { Write-Host $_ }
    exit 0
}

if ($Status) {
    if (Test-Path $InstanceFile) {
        Invoke-Vast @("show", "instance", (Get-Content $InstanceFile -Raw).Trim()) | ForEach-Object { Write-Host $_ }
    } else {
        Write-Warn "No saved instance ID found."
    }
    exit 0
}

if ($Teardown) {
    Destroy-SavedInstance
    exit 0
}

if (-not (Ensure-VastCredit)) { exit 1 }

if ($Tier -eq "Auto") {
    Write-Host "  [1] RTX 4090`n  [2] A100`n  [3] H100`n  [4] Auto"
    switch (Read-Host "  Pick a tier [1-4, Enter for 1]") {
        "2" { $Tier = "A100" }
        "3" { $Tier = "H100" }
        "4" { $Tier = "Auto" }
        default { $Tier = "4090" }
    }
}

Write-Host "  Selected tier: $Tier" -ForegroundColor Cyan
$offerId = Search-Offers
if (-not $offerId) { exit 1 }

$id = Create-Instance $offerId
if (-not $id) { exit 1 }

$url = Wait-ForUrl $id
if (-not $url) {
    Write-Err "Could not resolve the public URL. Instance $id is active and billing."
    Write-Warn "Run this script with -Teardown to stop it."
    exit 1
}

Write-Host "`n  SERVER URL: $url`n" -ForegroundColor Green
Write-Host "  Paste this into Settings -> Local Server URL."
Write-Host "  Stop billing with: .\vast_provision.ps1 -Teardown`n"
Wait-ForHealth $url | Out-Null
Write-Host "Done." -ForegroundColor Green
