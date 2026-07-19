<#
.SYNOPSIS
  One-click Vast.ai GPU provisioning for GrokVideoStudio.
  Rents a cloud GPU, deploys the LTX-Video server, and prints the URL
  to paste into Settings -> Local Server URL.

.DESCRIPTION
  Prerequisites:
    - Vast.ai account (register at https://vast.ai)
    - API key from https://vast.ai/console/api/
    - Credits loaded on your account

  Usage:
    .\vast_provision.ps1                    # Interactive -- pick GPU tier
    .\vast_provision.ps1 -Tier 4090         # RTX 4090 (~$0.30/hr)
    .\vast_provision.ps1 -Tier A100         # A100 80GB (~$1.50/hr)
    .\vast_provision.ps1 -Tier H100         # H100 80GB (~$2.50/hr)
    .\vast_provision.ps1 -Teardown          # Stop & destroy the instance
    .\vast_provision.ps1 -ListInstances     # Show running instances
#>

param(
    [ValidateSet("4090", "A100", "H100", "Auto")]
    [string]$Tier = "Auto",
    [string]$VastApiKey = "",
    [switch]$Teardown,
    [switch]$ListInstances,
    [switch]$Status
)

# If API key passed via command line, set it as env var
if ($VastApiKey -and $VastApiKey -ne "") {
    $env:VAST_API_KEY = $VastApiKey
}

$ErrorActionPreference = "Stop"
$VastCliVersion = "0.2.3"
$InstanceFile = Join-Path $PSScriptRoot ".vast_instance_id"
$ServerDir = $PSScriptRoot

#  Helpers 

function Write-Header($msg) {
    Write-Host "`n$("==>") $msg`n" -ForegroundColor Cyan
}

function Write-Step($msg) {
    Write-Host "  $msg" -ForegroundColor White
}

function Write-OK($msg) {
    Write-Host "  $msg" -ForegroundColor Green
}

function Write-Warn($msg) {
    Write-Host "  $msg" -ForegroundColor Yellow
}

function Write-Err($msg) {
    Write-Host "  $msg" -ForegroundColor Red
}

#  Install Vast CLI 

function Ensure-VastCli {
    Write-Step "Checking for Vast.ai CLI..."
    $vast = Get-Command vast -ErrorAction SilentlyContinue
    if ($vast) {
        Write-OK "Vast CLI already installed."
        return $true
    }

    Write-Step "Installing Vast.ai CLI via pip..."
    # vastai has no wheels for Python 3.14; need 3.10-3.13
    $pyExe = $null
    $pyVer = $null

    # Method 1: Try py launcher with specific versions, resolve actual exe path
    foreach ($ver in @("3.12", "3.11", "3.10", "3.13")) {
        try {
            $testResult = & py -$ver --version 2>&1
            if ($LASTEXITCODE -eq 0 -and $testResult -match "Python") {
                # Resolve the actual python.exe path so & can invoke it directly
                $resolvedPath = (& py -$ver -c "import sys; print(sys.executable)" 2>$null).Trim()
                if ($resolvedPath -and (Test-Path $resolvedPath)) {
                    $pyExe = $resolvedPath
                    $pyVer = $ver
                    Write-Step "Using Python $ver ($resolvedPath) for vastai install"
                    break
                }
            }
        } catch { }
    }

    # Method 2: Check known install paths directly (py launcher may not be configured)
    if (-not $pyExe) {
        $localProgs = Join-Path $env:LOCALAPPDATA "Programs\Python"
        if (Test-Path $localProgs) {
            foreach ($dir in (Get-ChildItem $localProgs -Directory | Sort-Object Name -Descending)) {
                if ($dir.Name -match "Python(\d)(\d+)") {
                    $major = [int]$Matches[1]
                    $minor = [int]$Matches[2]
                    if ($major -eq 3 -and $minor -ge 10 -and $minor -le 13) {
                        $exePath = Join-Path $dir.FullName "python.exe"
                        if (Test-Path $exePath) {
                            $pyExe = $exePath
                            $pyVer = "3.$minor"
                            Write-Step "Using Python 3.$minor ($exePath) for vastai install"
                            break
                        }
                    }
                }
            }
        }
    }

    # Method 3: Check python on PATH if it is a compatible version
    if (-not $pyExe) {
        $pathPy = (Get-Command python -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
        if ($pathPy) {
            $verResult = & $pathPy --version 2>&1
            if ($verResult -match "Python (\d+)\.(\d+)") {
                $major = [int]$Matches[1]
                $minor = [int]$Matches[2]
                if ($major -eq 3 -and $minor -ge 10 -and $minor -le 13) {
                    $pyExe = $pathPy
                    $pyVer = "3.$minor"
                    Write-Step "Using Python 3.$minor (PATH) for vastai install"
                }
            }
        }
    }

    if (-not $pyExe) {
        Write-Err "No compatible Python found. vastai needs Python 3.10-3.13."
        Write-Err "Python 3.14 is too new - no vastai wheels available."
        Write-Host "  Install Python 3.12 from: https://www.python.org/downloads/release/python-3120/" -ForegroundColor Yellow
        return $false
    }

    & $pyExe -m pip install "vastai" 2>&1 | Out-Null

    # Add user Scripts and Python dir to PATH for this session
    $pyPath = (& $pyExe -c "import sys; print(sys.prefix)" 2>$null)
    if ($pyPath) {
        $pipScripts = Join-Path $pyPath "Scripts"
        if (Test-Path $pipScripts) { $env:PATH += ";$pipScripts" }
    }
    # Also check APPDATA for user installs
    $userScripts = Join-Path $env:APPDATA "Python\Scripts"
    if (Test-Path $userScripts) { $env:PATH += ";$userScripts" }

    $vast = Get-Command vast -ErrorAction SilentlyContinue
    if ($vast) {
        Write-OK "Vast CLI installed."
        return $true
    }

    # Try user Scripts dir
    $userScripts = Join-Path $env:APPDATA "Python\Scripts"
    if (Test-Path (Join-Path $userScripts "vast.exe")) {
        $env:PATH += ";$userScripts"
        Write-OK "Vast CLI installed (user scripts)."
        return $true
    }

    Write-Err "Could not install Vast CLI. Try: pip install vastai"
    return $false
}

function Ensure-VastApiKey {
    Write-Step "Checking Vast.ai API key..."
    $apiKey = $env:VAST_API_KEY
    if ($apiKey) {
        vast set api-key $apiKey 2>&1 | Out-Null
        Write-OK "API key set from environment."
        return $true
    }

    # Check if already configured
    $keyFile = Join-Path $env:USERPROFILE ".vast_api_key"
    if (Test-Path $keyFile) {
        Write-OK "API key already configured."
        return $true
    }

    Write-Warn "No Vast.ai API key found."
    Write-Host ""
    Write-Host "  1. Go to https://vast.ai/console/api/" -ForegroundColor White
    Write-Host "  2. Copy your API key" -ForegroundColor White
    Write-Host "  3. Paste it below:" -ForegroundColor White
    Write-Host ""
    $apiKey = Read-Host "  API Key"

    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        Write-Err "No key provided."
        return $false
    }

    vast set api-key $apiKey 2>&1 | Out-Null
    $apiKey | Out-File -FilePath $keyFile -NoNewline
    Write-OK "API key saved."
    return $true
}

#  Search & Provision 

function Get-GpuFilter {
    switch ($Tier) {
        "4090" { return "gpu_name=RTX_4090" }
        "A100" { return "gpu_name=A100_SXM4-80GB" }
        "H100" { return "gpu_name=H100" }
        default {
            # Auto: pick cheapest GPU with 16GB+ VRAM under $0.50/hr
            return "gpu_ram>=16 dph<=0.50 verified>=0.9"
        }
    }
}

function Search-Instances {
    param([string]$Filter)

    Write-Step "Searching for available GPU instances..."
    $filterStr = if ($Tier -eq "Auto") { "gpu_ram>=16 dph<=0.50 verified>=0.9" } else { "gpu_name~=$Tier" }
    
    $raw = vast search offers $filterStr --raw 2>&1
    $lines = $raw -split "`n" | Where-Object { $_.Trim() -ne "" }
    
    if ($lines.Count -lt 2) {
        Write-Err "No instances found matching filter."
        return $null
    }

    # Parse the raw output -- columns: id, gpu_name, dph, gpu_ram, dlperf, etc.
    # Show top 5 cheapest
    Write-Host ""
    Write-Host "  Top 5 cheapest offers:" -ForegroundColor White
    Write-Host "  $($lines[0])" -ForegroundColor DarkGray
    Write-Host "  -----------------------------------------------" -ForegroundColor DarkGray

    $count = [Math]::Min(6, $lines.Count)
    for ($i = 1; $i -lt $count; $i++) {
        $parts = $lines[$i] -split "\s+"
        if ($parts.Count -ge 4) {
            Write-Host "  [$($i)] ID: $($parts[0]) | $($parts[1]) | $$($parts[2])/hr | $($parts[3])GB VRAM" -ForegroundColor White
        }
    }
    Write-Host ""

    # Auto-pick cheapest if Auto tier
    if ($Tier -eq "Auto") {
        $parts = $lines[1] -split "\s+"
        return $parts[0]
    }

    # Otherwise let user pick
    $choice = Read-Host "  Pick an offer [1-5, or enter for #1]"
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }
    
    $idx = [int]$choice
    if ($idx -ge 1 -and $idx -lt $lines.Count) {
        $parts = $lines[$idx] -split "\s+"
        return $parts[0]
    }

    # Fallback: first offer
    $parts = $lines[1] -split "\s+"
    return $parts[0]
}

function Provision-Instance {
    param([string]$OfferId)

    Write-Step "Creating instance from offer $OfferId..."
    
    # Create instance with our Docker image
    # Using a base CUDA image + onstart script to deploy our server
    $imageName = "nvidia/cuda:12.4.0-cudnn-runtime-ubuntu22.04"
    $onstartScript = @"
#!/bin/bash
set -e
apt-get update && apt-get install -y python3 python3-pip python3-venv libgl1 libglib2.0-0
python3 -m venv /opt/venv
export PATH=/opt/venv/bin:$PATH
pip install --upgrade pip
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
pip install diffusers>=0.32.0 transformers accelerate pillow fastapi uvicorn imageio imageio-ffmpeg
cd /app
uvicorn video_server:app --host 0.0.0.0 --port 7860 &
"@

    # Create temp file for onstart
    $onstartFile = Join-Path $env:TEMP "vast_onstart.sh"
    $onstartScript | Out-File -FilePath $onstartFile -Encoding ascii -NoNewline

    $result = vast create instance $OfferId `
        --image $imageName `
        --disk 20 `
        --onstart $onstartFile `
        --env "-p 7860:7860/tcp" `
        --raw 2>&1

    # Extract instance ID from result
    $instanceId = $null
    if ($result -match '"id"\s*:\s*(\d+)') {
        $instanceId = $matches[1]
    } elseif ($result -match '(\d+)') {
        $instanceId = $matches[1]
    }

    if ($instanceId) {
        $instanceId | Out-File -FilePath $InstanceFile -NoNewline
        Write-OK "Instance created: ID $instanceId"
        return $instanceId
    }

    Write-Err "Failed to create instance. Output: $result"
    return $null
}

function Copy-ServerFiles {
    param([string]$InstanceId)

    Write-Step "Copying server files to instance..."
    
    $serverFile = Join-Path $ServerDir "video_server.py"
    $requirementsFile = Join-Path $ServerDir "requirements.txt"

    # Wait for instance to be running
    Write-Step "Waiting for instance to boot..."
    $maxWait = 120
    $waited = 0
    do {
        Start-Sleep -Seconds 5
        $waited += 5
        $state = vast show instances --raw 2>&1 | Select-String $InstanceId
        if ($state -match "running") { break }
        if ($waited -ge $maxWait) {
            Write-Warn "Instance still initializing after $maxWait seconds -- continuing anyway."
            break
        }
        Write-Host "." -NoNewline -ForegroundColor DarkGray
    } while ($true)
    Write-Host ""

    # Copy files via vast CLI
    vast copy $InstanceId "$serverFile" /app/video_server.py 2>&1 | Out-Null
    vast copy $InstanceId "$requirementsFile" /app/requirements.txt 2>&1 | Out-Null
    
    Write-OK "Server files copied."
}

function Get-InstanceUrl {
    param([string]$InstanceId)

    Write-Step "Getting instance connection details..."
    
    $raw = vast show instances --raw 2>&1
    $lines = $raw -split "`n"
    
    foreach ($line in $lines) {
        if ($line -match $InstanceId) {
            # Try to extract IP and port mapping
            $parts = $line -split "\s+"
            foreach ($part in $parts) {
                # Look for IP:port pattern
                if ($part -match '(\d+\.\d+\.\d+\.\d+):(\d+)') {
                    $ip = $matches[1]
                    $port = $matches[2]
                    return "http://${ip}:$port"
                }
            }
            # Try columns-based parse
            if ($parts.Count -ge 10) {
                $ipCol = $parts | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1
                if ($ipCol) {
                    return "http://${ipCol}:7860"
                }
            }
        }
    }

    return $null
}

function Wait-ServerReady {
    param([string]$Url)

    Write-Step "Waiting for LTX-Video server to be ready..."
    $maxWait = 300  # 5 minutes for model download
    $waited = 0

    while ($waited -lt $maxWait) {
        try {
            $response = Invoke-RestMethod -Uri "$Url/health" -TimeoutSec 10 -ErrorAction Stop
            if ($response.status -eq "healthy") {
                Write-OK "Server is healthy! GPU: $($response.gpu_available), VRAM: $($response.total_vram_gb)GB"
                return $true
            }
        } catch {
            # Not ready yet
        }

        Start-Sleep -Seconds 10
        $waited += 10
        Write-Host "." -NoNewline -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Warn "Server not ready after $maxWait seconds. It may still be downloading models."
    Write-Warn "Check back in a few minutes -- the URL is still valid."
    return $false
}

function Stop-Instance {
    Write-Header "Tearing down Vast.ai instance"

    if (-not (Test-Path $InstanceFile)) {
        Write-Err "No saved instance ID found. Use 'vast show instances' to find it manually."
        return
    }

    $instanceId = Get-Content $InstanceFile -Raw
    Write-Step "Destroying instance $instanceId..."
    
    vast destroy instance $instanceId --raw 2>&1 | Out-Null
    Remove-Item $InstanceFile -Force
    Write-OK "Instance destroyed."
}

function Show-Instances {
    Write-Header "Vast.ai Instances"
    vast show instances 2>&1
}

function Show-Status {
    if (-not (Test-Path $InstanceFile)) {
        Write-Warn "No active instance."
        return
    }
    $instanceId = Get-Content $InstanceFile -Raw
    Write-Header "Instance $instanceId Status"
    vast show instances --raw 2>&1 | Select-String $instanceId
}

#  Main 

if ($ListInstances) { Show-Instances; exit 0 }
if ($Status) { Show-Status; exit 0 }
if ($Teardown) { Stop-Instance; exit 0 }

Write-Header "GrokVideoStudio -> Vast.ai Cloud GPU"

# Tier selection
if ($Tier -eq "Auto") {
    Write-Host "  Available GPU tiers:" -ForegroundColor White
    Write-Host "  [1] RTX 4090 (24GB)  ~$0.30-0.40/hr  (recommended)" -ForegroundColor White
    Write-Host "  [2] A100 (80GB)     ~$1.00-1.50/hr  (heavy duty)" -ForegroundColor White
    Write-Host "  [3] H100 (80GB)     ~$2.00-3.00/hr  (maximum)" -ForegroundColor White
    Write-Host "  [4] Auto            cheapest 16GB+   (budget)" -ForegroundColor White
    Write-Host ""
    $choice = Read-Host "  Pick a tier [1-4, enter for 1]"
    switch ($choice) {
        "2" { $Tier = "A100" }
        "3" { $Tier = "H100" }
        "4" { $Tier = "Auto" }
        default { $Tier = "4090" }
    }
}

Write-Host "  Selected tier: $Tier" -ForegroundColor Cyan
Write-Host ""

# Step 1: Ensure CLI
if (-not (Ensure-VastCli)) { exit 1 }

# Step 2: API key
if (-not (Ensure-VastApiKey)) { exit 1 }

# Step 3: Search for offers
$offerId = Search-Instances
if (-not $offerId) {
    Write-Err "No suitable offers found."
    exit 1
}
Write-OK "Selected offer: $offerId"

# Step 4: Create instance
$instanceId = Provision-Instance -OfferId $offerId
if (-not $instanceId) { exit 1 }

# Step 5: Copy server files
Copy-ServerFiles -InstanceId $instanceId

# Step 6: Get URL
$url = Get-InstanceUrl -InstanceId $instanceId
if (-not $url) {
    Write-Warn "Could not auto-detect URL. Run 'vast show instances' to find the IP."
    Write-Step "Look for the ports column -- find 7860 -> connect_ip:port"
    Write-Host ""
    $manualUrl = Read-Host "  Paste the URL (http://IP:PORT)"
    if ($manualUrl) { $url = $manualUrl }
}

if ($url) {
    Write-Host ""
    Write-Host "  ===================================================" -ForegroundColor Green
    Write-Host "  SERVER URL: $url" -ForegroundColor Green
    Write-Host "  ===================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Paste this into Settings -> Local Server URL" -ForegroundColor White
    Write-Host "  Then click Test Connection" -ForegroundColor White
    Write-Host ""
    Write-Host "  To stop the instance later:" -ForegroundColor DarkGray
    Write-Host "    .\vast_provision.ps1 -Teardown" -ForegroundColor DarkGray
    Write-Host ""
    Write-Step "Checking server health (this may take a few minutes for model download)..."
    Wait-ServerReady -Url $url
} else {
    Write-Warn "Could not determine URL automatically."
    Write-Host "  Run: vast show instances" -ForegroundColor White
    Write-Host "  Find your instance and look for the connect URL" -ForegroundColor White
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
