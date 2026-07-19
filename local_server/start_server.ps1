# GrokVideoStudio - Local GPU Video Server Launcher (PowerShell)
# Run: .\start_server.ps1

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GrokVideoStudio Local Video Generation Server" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

# -- Find a compatible Python (3.10-3.13 needed for PyTorch) --
$pyExe = $null
$pyVersionStr = $null

# Try py launcher first - it can target specific versions
foreach ($ver in @("3.12", "3.11", "3.10", "3.13")) {
    $tryExe = "py -$ver"
    $tryResult = & cmd /c "$tryExe --version 2>&1"
    if ($LASTEXITCODE -eq 0 -and $tryResult -match "Python $ver") {
        $pyExe = $tryExe
        $pyVersionStr = $tryResult.Trim()
        Write-Host "Found compatible: $pyVersionStr (via $pyExe)" -ForegroundColor Green
        break
    }
}

# Fall back to plain python
if (-not $pyExe) {
    $plainResult = & cmd /c "python --version 2>&1"
    if ($LASTEXITCODE -eq 0 -and $plainResult -match "Python (\d+)\.(\d+)") {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $pyVersionStr = $plainResult.Trim()
        if ($major -eq 3 -and $minor -ge 10 -and $minor -le 13) {
            $pyExe = "python"
            Write-Host "Found compatible: $pyVersionStr" -ForegroundColor Green
        }
    }
}

if (-not $pyExe) {
    if ($pyVersionStr) {
        Write-Host "[ERROR] Found $pyVersionStr but PyTorch needs Python 3.10-3.13." -ForegroundColor Red
        Write-Host "Python 3.14 is too new - PyTorch has no wheels for it yet."
    } else {
        Write-Host "[ERROR] No compatible Python found." -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Install Python 3.12 from: https://www.python.org/downloads/release/python-3120/" -ForegroundColor Yellow
    Write-Host "Make sure to check 'Add Python to PATH' during install."
    Write-Host "Then run this script again."
    Read-Host "Press Enter to exit"
    exit 1
}

# -- Determine CUDA version for PyTorch index URL --
$cudaUrl = "https://download.pytorch.org/whl/cu124"
Write-Host "Using PyTorch CUDA index: $cudaUrl"
Write-Host ""

# -- Create venv on first run --
$venvActivate = ".venv\Scripts\Activate.ps1"
if (-not (Test-Path $venvActivate)) {
    Write-Host "[SETUP] First run detected - creating virtual environment..." -ForegroundColor Yellow
    & cmd /c "$pyExe -m venv .venv"
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
