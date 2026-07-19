@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   GrokVideoStudio Local Video Generation Server
echo ===================================================
echo.

:: Check Python installation
where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Python was not found in your PATH.
    echo Please install Python 3.10 or newer and make sure to check "Add Python to PATH".
    pause
    exit /b 1
)

:: Get Python version
for /f "tokens=2 delims= " %%I in ('python --version 2^>^&1') do set PYTHON_VER=%%I
echo Found Python %PYTHON_VER%

:: Setup virtual environment
if not exist "venv" (
    echo [INFO] Creating Python virtual environment (venv)...
    python -m venv venv
    if !errorlevel! neq 0 (
        echo [ERROR] Failed to create virtual environment.
        pause
        exit /b 1
    )
)

:: Activate venv
echo [INFO] Activating virtual environment...
call venv\Scripts\activate.bat
if !errorlevel! neq 0 (
    echo [ERROR] Failed to activate virtual environment.
    pause
    exit /b 1
)

:: Install / Update dependencies
echo [INFO] Installing/updating requirements. This might take a while...
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
if !errorlevel! neq 0 (
    echo [ERROR] Failed to install requirements.
    echo Please make sure you have CUDA Toolkit installed if compiling from source.
    pause
    exit /b 1
)

:: Run Server
echo.
echo [INFO] Starting LTX-Video generation server...
echo [INFO] Accessible at http://localhost:7860
echo.
python video_server.py

pause
