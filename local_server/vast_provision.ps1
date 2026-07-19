<#
.SYNOPSIS
  Provision or destroy a Vast.ai GPU instance for GrokVideoStudio.
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
        try { $directories += (& $PythonExe -c "import sysconfig; print(sysconfig.get_path('scripts'))" 2>$null) } catch { }
        try { $directories += (Join-Path ((& $PythonExe -m site --user-base 2>$null | Select-Object -Last 1).Trim()) "Scripts") } catch { }
        foreach ($directory in ($directories | Where-Object { $_ } | Select-Object -Unique)) {
            foreach ($name in @("vastai.exe", "vastai", "vast.exe", "vast")) {
                $candidate = Join-Path $directory.Trim() $name
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
                $path = (& py -$version -c "import sys; print(sys.executable)" 2>$null | Select-Object -Last 1).Trim()
                if ($path -and (Test-Path $path)) { return $path }
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

function Invoke-Vast([string[]]$Arguments) {
    $output = & $script:VastCli @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "vastai $($Arguments -join ' ') failed:`n$($output -join "`n")"
    }
    return $output
}

function Ensure-ApiKey {
    if ($VastApiKey) { $env:VAST_API_KEY = $VastApiKey }
    if ($env:VAST_API_KEY) {
        Invoke-Vast @("set", "api-key", $env:VAST_API_KEY) | Out-Null
        Write-OK "Vast.ai API key configured."
        return $true
    }

    & $script:VastCli show user --raw 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-OK "Existing Vast.ai authentication is valid."
        return $true
    }

    $key = Read-Host "  Vast.ai API key"
    if (-not $key) { return $false }
    Invoke-Vast @("set", "api-key", $key) | Out-Null
    Write-OK "Vast.ai API key configured."
    return $true
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
            $price = if ($null -ne $offer.dph_total) { $offer.dph_total } else { $offer.dph }
            Write-Host "  [$($i + 1)] ID $($offer.id) | $($offer.gpu_name) | `$$price/hr"
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
    $server = Join-Path $PSScriptRoot "video_server.py"
    $requirements = Join-Path $PSScriptRoot "requirements.txt"
    if (-not (Test-Path $server)) { throw "Missing $server" }
    if (-not (Test-Path $requirements)) { throw "Missing $requirements" }

    $server64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($server))
    $requirements64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($requirements))
    $script = @"
#!/bin/bash
set -euo pipefail
mkdir -p /app
printf '%s' '$server64' | base64 -d > /app/video_server.py
printf '%s' '$requirements64' | base64 -d > /app/requirements.txt
apt-get update
apt-get install -y --no-install-recommends python3 python3-pip python3-venv libgl1 libglib2.0-0 ffmpeg ca-certificates
rm -rf /var/lib/apt/lists/*
python3 -m venv /opt/venv
/opt/venv/bin/python -m pip install --upgrade pip setuptools wheel
/opt/venv/bin/python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
/opt/venv/bin/python -m pip install -r /app/requirements.txt
cd /app
exec /opt/venv/bin/python -m uvicorn video_server:app --host 0.0.0.0 --port 7860
"@
    $path = Join-Path $env:TEMP ("gvs-vast-{0}.sh" -f [Guid]::NewGuid().ToString("N"))
    $script | Out-File $path -Encoding ascii -NoNewline
    return $path
}

function Create-Instance([string]$OfferId) {
    $onstart = $null
    try {
        $onstart = New-OnstartFile
        $json = (Invoke-Vast @("create", "instance", $OfferId, "--image", "nvidia/cuda:12.4.0-cudnn-runtime-ubuntu22.04", "--disk", "$DiskGb", "--ssh", "--env", "-p 7860:7860/tcp", "--onstart", $onstart, "--label", "GrokVideoStudio", "--cancel-unavail", "--raw")) -join "`n"
        $result = $json | ConvertFrom-Json
        $id = if ($result.new_contract) { $result.new_contract } elseif ($result.id) { $result.id } else { $null }
        if (-not $id) { throw "Instance was created but no instance ID was returned: $json" }
        [string]$id | Out-File $InstanceFile -Encoding ascii -NoNewline
        Write-OK "Instance created: $id"
        return [string]$id
    } catch {
        Write-Err $_.Exception.Message
        return $null
    } finally {
        if ($onstart -and (Test-Path $onstart)) { Remove-Item $onstart -Force -ErrorAction SilentlyContinue }
    }
}

function Get-Instance([string]$Id) {
    $json = (Invoke-Vast @("show", "instance", $Id, "--raw")) -join "`n"
    $result = $json | ConvertFrom-Json
    if ($result.PSObject.Properties["instances"]) { return @($result.instances)[0] }
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
                if ($mapping -and $mapping.Value) { $port = [string](@($mapping.Value)[0].HostPort) }
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
    $deadline = (Get-Date).AddMinutes(15)
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod "$($Url.TrimEnd('/'))/health" -TimeoutSec 10
            if ($health.status -eq "healthy") {
                Write-OK "Server is healthy. GPU: $($health.gpu_available); VRAM: $($health.total_vram_gb) GB"
                return
            }
        } catch { }
        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep 10
    }
    Write-Warn "Server is not healthy yet. The instance is still active and billing."
}

function Destroy-SavedInstance {
    if (-not (Test-Path $InstanceFile)) { Write-Warn "No saved instance ID found."; return }
    $id = (Get-Content $InstanceFile -Raw).Trim()
    Invoke-Vast @("destroy", "instance", $id, "--raw") | Out-Null
    Remove-Item $InstanceFile -Force
    Write-OK "Instance $id destroyed. Billing stopped."
}

Write-Header "GrokVideoStudio -> Vast.ai Cloud GPU"
if (-not (Ensure-VastCli)) { exit 1 }
if (-not (Ensure-ApiKey)) { exit 1 }

if ($ListInstances) { Invoke-Vast @("show", "instances") | ForEach-Object { Write-Host $_ }; exit 0 }
if ($Status) {
    if (Test-Path $InstanceFile) { Invoke-Vast @("show", "instance", (Get-Content $InstanceFile -Raw).Trim()) | ForEach-Object { Write-Host $_ } }
    else { Write-Warn "No saved instance ID found." }
    exit 0
}
if ($Teardown) { Destroy-SavedInstance; exit 0 }

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
Wait-ForHealth $url
Write-Host "Done." -ForegroundColor Green
