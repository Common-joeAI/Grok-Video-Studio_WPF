# GrokVideoStudio - Local GPU Video Server Launcher (PowerShell)
# Run: .\start_server.ps1

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GrokVideoStudio Local Video Generation Server" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

# -- Check Python --
$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    Write-Host "[ERROR] Python not found on PATH." -ForegroundColor Red
    Write-Host "Install Python 3.10+ from https://python.org and check 'Add Python to PATH'."
    Read-Host "Press Enter to exit"
    exit 1
}

$pyVersion = & python --version 2>&1
Write-Host "Found: $pyVersion"
Write-Host ""

# -- Create venv on first run --
$venvActivate = ".venv\Scripts\Activate.ps1"
if (-not (Test-Path $venvActivate)) {
    Write-Host "[SETUP] First run detected - creating virtual environment..." -ForegroundColor Yellow
    & python -m venv .venv
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Failed to create virtual environment." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }

    & .venv\Scripts\Activate.ps1

    Write-Host "[SETUP] Upgrading pip..." -ForegroundColor Yellow
    & python -m pip install --upgrade pip

    Write-Host "[SETUP] Installing PyTorch with CUDA 12.4..." -ForegroundColor Yellow
    & pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] PyTorch install failed. Check your CUDA version." -ForegroundColor Red
        Write-Host "For CUDA 12.1: use cu121 instead of cu124 in the URL above."
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
