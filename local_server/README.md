# GrokVideoStudio Local Video Generation Server

A high-performance local video generation server built with **FastAPI** and **Hugging Face Diffusers**. This server runs the **LTX-Video (Lightricks)** model locally on your GPU, exposing a robust, asynchronous HTTP API matching the xAI submit/poll/download pattern.

Optimized to run on consumer-grade hardware, including GPUs with **8GB VRAM** (such as the RTX 4060).

---

## Features

- **Asynchronous Generation Queue**: Submits jobs, returns a `request_id` immediately, and processes them one-by-one to avoid VRAM overload.
- **Dynamic VRAM Scaling**:
  - **8GB VRAM (e.g. RTX 4060)**: Automatically scales resolution to `768x512` (480p native aspect ratio) and enables **Model CPU Offloading**, **VAE Tiling**, and **VAE Slicing** to prevent Out Of Memory (OOM) errors.
  - **12GB+ VRAM**: Automatically scales resolution to `1280x720` (720p) and keeps the full pipeline on CUDA for maximum inference speed.
- **Dual Pipeline Support**: Dynamically loads and switches between text-to-video (`LTXPipeline`) and image-to-video (`LTXImageToVideoPipeline`) as requests arrive, releasing resources and clearing cache when switching.
- **CORS Enabled**: Ready to connect with web clients or desktop applications (such as C#/.NET WPF).
- **Progress Monitoring**: Real-time denoising step progress callbacks.
- **Automatic Cleanup**: Deletes generated video temp files older than 1 hour to manage disk space.

---

## System Requirements

- **Operating System**: Windows 10/11 or Linux (Ubuntu 20.04+ recommended)
- **Python**: version `3.10` or `3.11`
- **GPU**: NVIDIA GPU with CUDA support.
  - *Minimum*: NVIDIA GPU with 8GB VRAM (RTX 3060/4060 or higher).
  - *Recommended*: NVIDIA GPU with 12GB+ VRAM (RTX 3060 12GB, RTX 4070, or higher).
- **CUDA Toolkit**: CUDA 11.8 or 12.x installed.

---

## Installation & Setup

### Windows Users (Easy Startup)

1. Simply double-click **`start_server.bat`**.
2. The script will automatically:
   - Create a Python virtual environment (`venv`).
   - Upgrade `pip`.
   - Install all required libraries (PyTorch with CUDA support, Diffusers, FastAPI, etc.).
   - Launch the FastAPI server at `http://localhost:7860`.

### Linux / Manual Installation

1. Create a virtual environment and activate it:
   ```bash
   python3 -m venv venv
   source venv/bin/activate
   ```

2. Install PyTorch with CUDA support (ensure the index URL matches your CUDA version):
   ```bash
   pip install torch torchvision --index-url https://download.pytorch.org/whl/cu121
   ```

3. Install the remaining requirements:
   ```bash
   pip install -r requirements.txt
   ```

4. Start the server:
   ```bash
   python video_server.py
   ```

---

## API Reference

The server runs on **port 7860** by default.

### 1. Health Check
Checks GPU availability and VRAM status.

* **Method**: `GET`
* **URL**: `http://localhost:7860/`
* **Response**:
  ```json
  {
    "status": "healthy",
    "gpu_available": true,
    "total_vram_gb": 8.0,
    "queue_size": 0
  }
  ```

---

### 2. Submit Video Generation
Submits a text-to-video or image-to-video generation job. Returns immediately.

* **Method**: `POST`
* **URL**: `http://localhost:7860/v1/videos/generations`
* **Headers**: `Content-Type: application/json`
* **Body (Text-to-Video)**:
  ```json
  {
    "prompt": "A majestic eagle soaring over snow-capped mountain peaks at sunset, highly detailed, 4k",
    "duration": 4.0,
    "resolution": "480p"
  }
  ```
* **Body (Image-to-Video)**:
  ```json
  {
    "prompt": "Animate the water, gentle waves moving, cinematic lighting",
    "duration": 4.0,
    "image": {
      "url": "data:image/png;base64,iVBORw0KGgoAAAANSU..."
    }
  }
  ```
  *(Note: The `image.url` should contain a base64-encoded Data URI).*

* **Response**:
  ```json
  {
    "request_id": "bfd99e07-7756-4cf3-a7c8-ee1bc16999fa"
  }
  ```

---

### 3. Poll Generation Status
Check progress or get the download link of a submitted job.

* **Method**: `GET`
* **URL**: `http://localhost:7860/v1/videos/{request_id}`
* **Responses**:
  - **Running/Denoising**:
    ```json
    {
      "status": "running",
      "progress": 36,
      "video": null,
      "error": null
    }
    ```
  - **Completed**:
    ```json
    {
      "status": "done",
      "progress": 100,
      "video": {
        "url": "http://localhost:7860/v1/videos/bfd99e07-7756-4cf3-a7c8-ee1bc16999fa/download",
        "duration": 4.0
      },
      "error": null
    }
    ```
  - **Failed**:
    ```json
    {
      "status": "failed",
      "progress": 0,
      "video": null,
      "error": {
        "message": "Out of memory during VAE decoding step..."
      }
    }
    ```

---

### 4. Stream / Download Video
Streams the generated `.mp4` video. Supports range requests for smooth playback and seeking in HTML5 players.

* **Method**: `GET`
* **URL**: `http://localhost:7860/v1/videos/{request_id}/download`
* **Response**: MP4 Video Stream

---

## Memory Management Details

LTX-Video is an advanced 2-Billion parameter Diffusion Transformer (DiT). Generating video is extremely resource-intensive. To make it run seamlessly on an RTX 4060 (8GB), this server implements:
1. **Model CPU Offloading**: Replaces standard `.to('cuda')` with Diffusers' sub-module CPU offloading. This loads sub-modules on-demand into GPU memory and moves them back to CPU when done.
2. **VAE Tiling & Slicing**: Splits the frame sequence during video decoding into smaller slices/tiles to avoid peak VRAM spikes during the final MP4 export.
3. **Cache Clearing**: Explicitly triggers `torch.cuda.empty_cache()` and Python `gc.collect()` at the end of each generation step.
4. **Queue Serializer**: Forces requests to process one-by-one. If a user spawns multiple generations concurrently, they queue up safely instead of crashing the GPU.
