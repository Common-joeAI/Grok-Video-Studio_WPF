FROM nvidia/cuda:12.4.0-cudnn-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

# Install Python 3.12 + system deps
RUN apt-get update && apt-get install -y \
    python3.12 python3.12-venv python3-pip \
    libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Create working directory
WORKDIR /app

# Copy requirements and install
COPY requirements.txt .
RUN python3.12 -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"
RUN pip install --no-cache-dir --upgrade pip
RUN pip install --no-cache-dir torch torchvision --index-url https://download.pytorch.org/whl/cu124
RUN pip install --no-cache-dir -r requirements.txt

# Copy server code
COPY video_server.py .

# Expose the API port
EXPOSE 7860

# Health check
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD python3 -c "import urllib.request; urllib.request.urlopen('http://localhost:7860/health')" || exit 1

# Start the server
CMD ["uvicorn", "video_server:app", "--host", "0.0.0.0", "--port", "7860"]
