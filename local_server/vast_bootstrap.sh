#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
exec > >(tee -a /var/log/grok-video-studio-bootstrap.log) 2>&1

RAW_BASE="https://raw.githubusercontent.com/Common-joeAI/Grok-Video-Studio_WPF/main/local_server"
mkdir -p /app

apt-get update
apt-get install -y --no-install-recommends python3 python3-pip python3-venv libgl1 libglib2.0-0 ffmpeg ca-certificates curl
rm -rf /var/lib/apt/lists/*

curl --fail --location --retry 5 --retry-delay 3 "$RAW_BASE/video_server.py" -o /app/video_server.py
curl --fail --location --retry 5 --retry-delay 3 "$RAW_BASE/requirements.txt" -o /app/requirements.txt
test -s /app/video_server.py
test -s /app/requirements.txt

grep -v -E '^(torch|torchvision)([<=>[:space:]].*)?$' /app/requirements.txt > /app/requirements-vast.txt

python3 -m venv /opt/venv
/opt/venv/bin/python -m pip install --upgrade pip setuptools wheel
/opt/venv/bin/python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
/opt/venv/bin/python -m pip install -r /app/requirements-vast.txt

cd /app
exec /opt/venv/bin/python -m uvicorn video_server:app --host 0.0.0.0 --port 7860
