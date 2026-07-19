# GrokVideoStudio - Local GPU Video Server Launcher (PowerShell)
# Run: .\start_server.ps1

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GrokVideoStudio Local Video Generation Server" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

# -- Find a compatible Python (3.10-3.13 needed for PyTorch) --
$pyExe = $null
$pyVersionStr = $null

# Try py launcher with specific versions
$pyResult = & py -0p 2>&1
Write-Host "Installed Python versions (py launcher):"
Write-Host $pyResult
Write-Host ""

foreach ($ver in @("3.12", "3.11", "3.10", "3.13")) {
    try {
        $testResult = & py -$ver --version 2>&1
        if ($LASTEXITCODE -eq 0 -and $testResult -match "Python") {
            $pyExe = "py -$ver"
            $pyVersionStr = $testResult.Trim()
            Write-Host "Selected: $pyVersionStr (via $pyExe)" -ForegroundColor Green
            break
        }
    } catch {
        # This version not installed, try next
    }
}

# Fall back to plain python only if version is compatible
if (-not $pyExe) {
    $plainResult = & python --version 2>&1
    if ($LASTEXITCODE -eq 0 -and $plainResult -match "Python (\d+)\.(\d+)") {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $pyVersionStr = $plainResult.Trim()
        if ($major -eq 3 -and $minor -ge 10 -and $minor -le 13) {
            $pyExe = "python"
            Write-Host "Selected: $pyVersionStr (via python)" -ForegroundColor Green
        }
    }
}

if (-not $pyExe) {
    if ($pyVersionStr) {
        Write-Host "[ERROR] Found $pyVersionStr but PyTorch needs Python 3.10-3.13." -ForegroundColor Red
        Write-Host "Python 3.14 is too new - PyTorch has no wheels for it yet."
    } else {
        Write-Host "[ERROR] No Python found." -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Install Python 3.12 from: https://www.python.org/downloads/release/python-3120/" -ForegroundColor Yellow
    Write-Host "Make sure to check 'Add Python to PATH' during install."
    Write-Host "Then run: del -Recurse -Force .venv  (to remove the bad venv)"
    Write-Host "Then run this script again."
    Read-Host "Press Enter to exit"
    exit 1
}

# -- Delete venv if it was created with wrong Python version --
if (Test-Path ".venv\pyvenv.cfg") {
    $cfgContent = Get-Content ".venv\pyvenv.cfg" -Raw
    if ($cfgContent -match "version = (\d+)\.(\d+)") {
        $venvMajor = [int]$Matches[1]
        $venvMinor = [int]$Matches[2]
        if ($venvMajor -eq 3 -and $venvMinor -gt 13) {
            Write-Host "[WARN] Existing .venv was created with Python $venvMajor.$venvMinor (incompatible). Deleting..." -ForegroundColor Yellow
            Remove-Item -Recurse -Force .venv
        }
    }
}

# -- Determine CUDA version for PyTorch index URL --
$cudaUrl = "https://download.pytorch.org/whl/cu124"
Write-Host "Using PyTorch CUDA index: $cudaUrl"
Write-Host ""

# -- Create venv on first run --
$venvActivate = ".venv\Scripts\Activate.ps1"
if (-not (Test-Path $venvActivate)) {
    Write-Host "[SETUP] First run detected - creating virtual environment..." -ForegroundColor Yellow
    & $pyExe -m venv .venv
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Failed to create virtual environment." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }

    & .venv\Scripts\Activate.ps1

    Write-Host "[SETUP] Upgrading pip..." -ForegroundColor Yellow
    & python -m pip install --upgrade pip

    Write-Host "[SETUP] Installing PyTorch with CUDA..." -ForegroundColor Yellow
    & pip install torch torchvision --index-url $cudaUrl
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] PyTorch install failed." -ForegroundColor Red
        Write-Host "Try CUDA 12.1: edit this script and change cu124 to cu121"
        Read-Host "Press Enter to exit"
        exit 1
    }

    Write-Host "[SETUP] Installing remaining dependencies..." -ForegroundColor Yellow
    & pip install -r requirements.txt
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Failed to install requirements." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }

    Write-Host ""
    Write-Host "[SETUP] Done! Starting server..." -ForegroundColor Green
} else {
    & .venv\Scripts\Activate.ps1
}

# -- Start server --
Write-Host ""
$url = "http://localhost:7860"
Write-Host "[INFO] Starting LTX-Video server on $url" -ForegroundColor Green
Write-Host ""
& python video_server.py --port 7860

# -- Error handling: keep window open if server crashes --
if ($LASTEXITCODE -ne 0 -or $?) {
    Write-Host ""
    Write-Host "[ERROR] Server exited with code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Check the output above for errors." -ForegroundColor Yellow
}
Write-Host ""
Read-Host "Press Enter to close this window"
