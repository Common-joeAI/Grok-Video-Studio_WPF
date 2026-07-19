import os
import io
import gc
import uuid
import time
import queue
import base64
import logging
import threading
from typing import Optional, Dict, Any, Union

from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import torch
from PIL import Image

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(threadName)s: %(message)s",
    handlers=[logging.StreamHandler()]
)
logger = logging.getLogger("VideoServer")

app = FastAPI(
    title="GrokVideoStudio Local Server",
    description="Local LTX-Video generation server conforming to the xAI submit/poll API pattern.",
    version="1.0.0"
)

# Enable CORS so local web pages/WPF apps can easily query it
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Directories
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TEMP_DIR = os.path.join(BASE_DIR, "temp_videos")
os.makedirs(TEMP_DIR, exist_ok=True)

# Queues and State
job_queue = queue.Queue()
jobs: Dict[str, Dict[str, Any]] = {}
jobs_lock = threading.Lock()

# Define request schemas
class ImageInput(BaseModel):
    url: str  # Data URI base64_data_uri or URL

class VideoGenerationRequest(BaseModel):
    model: Optional[str] = "Lightricks/ltx-video"
    prompt: str
    duration: Optional[float] = 4.0  # defaults to 4.0 seconds
    resolution: Optional[Union[str, Dict[str, int]]] = None
    image: Optional[ImageInput] = None

# Calculate GPU memory and set defaults
try:
    if torch.cuda.is_available():
        free_mem, total_mem = torch.cuda.mem_get_info()
        total_vram_gb = total_mem / (1024 ** 3)
        logger.info(f"Detected CUDA GPU with {total_vram_gb:.2f} GB total VRAM.")
    else:
        total_vram_gb = 0.0
        logger.warning("CUDA is not available. Running on CPU is NOT recommended for LTX-Video.")
except Exception as e:
    logger.warning(f"Failed to query GPU memory: {e}. Defaulting to 8GB profile.")
    total_vram_gb = 8.0

def determine_resolution(resolution: Any, vram_gb: float) -> tuple[int, int]:
    """
    Decide width and height.
    LTX-Video expects resolutions to be multiples of 32.
    """
    # Auto-detect resolution limit based on VRAM (8GB vs 12GB+)
    default_res = "480p" if vram_gb < 11.5 else "720p"
    target_res = resolution if resolution else default_res

    if isinstance(target_res, dict):
        width = target_res.get("width", 768)
        height = target_res.get("height", 512)
        # Snap to multiple of 32
        width = (width // 32) * 32
        height = (height // 32) * 32
        return width, height

    if not isinstance(target_res, str):
        target_res = default_res

    target_res = target_res.lower().strip()

    if "720p" in target_res:
        if "portrait" in target_res:
            return 720, 1280
        else:
            return 1280, 720
    else:  # "480p" or fallback
        # LTX-Video standard native 3:2 landscape (highly efficient on 8GB VRAM)
        if "portrait" in target_res:
            return 512, 768
        else:
            return 768, 512

def calculate_num_frames(duration: float) -> int:
    """
    LTX-Video generates at 25 FPS by default.
    The number of frames must be divisible by 32, or equal to 8k + 1.
    For Diffusers LTX-Video, num_frames must equal 8k + 1.
    """
    fps = 25
    frames = int(duration * fps)
    # Align to 8k + 1
    frames = ((frames - 1) // 8) * 8 + 1
    # Minimum 17 frames, maximum 257 frames for stability
    return max(17, min(257, frames))

def decode_base64_image(base64_str: str) -> Image.Image:
    """
    Decodes a base64 encoded image or data-uri.
    """
    if "," in base64_str:
        base64_str = base64_str.split(",", 1)[1]
    image_bytes = base64.b64decode(base64_str)
    image = Image.open(io.BytesIO(image_bytes))
    return image.convert("RGB")

def cleanup_old_files(max_age_seconds: int = 3600):
    """
    Cleans up temp video files older than 1 hour.
    """
    logger.info("Running periodic cleanup of temporary video files...")
    now = time.time()
    try:
        count = 0
        for filename in os.listdir(TEMP_DIR):
            if filename.endswith(".mp4"):
                filepath = os.path.join(TEMP_DIR, filename)
                mtime = os.path.getmtime(filepath)
                if now - mtime > max_age_seconds:
                    try:
                        os.remove(filepath)
                        count += 1
                        # Update status in jobs map if exists
                        req_id = filename[:-4]
                        with jobs_lock:
                            if req_id in jobs:
                                jobs[req_id]["status"] = "expired"
                                jobs[req_id]["video_path"] = None
                    except Exception as fe:
                        logger.error(f"Failed to remove file {filepath}: {fe}")
        if count > 0:
            logger.info(f"Cleaned up {count} expired video file(s).")
    except Exception as e:
        logger.error(f"Error during file cleanup: {e}")

# Worker Thread Function
def background_worker_thread():
    logger.info("Background video generation worker thread started.")
    current_pipeline_type = None  # None, "t2v", or "i2v"
    pipe = None
    last_cleanup_time = time.time()

    while True:
        try:
            # Check if cleanup is due (every 10 minutes)
            if time.time() - last_cleanup_time > 600:
                cleanup_old_files()
                last_cleanup_time = time.time()

            try:
                request_id = job_queue.get(timeout=1.0)
            except queue.Empty:
                continue

            if request_id is None:
                # Shutdown signal
                logger.info("Worker thread received shutdown signal.")
                break

            with jobs_lock:
                job = jobs.get(request_id)
            
            if not job:
                job_queue.task_done()
                continue

            logger.info(f"Starting job {request_id}...")
            with jobs_lock:
                job["status"] = "running"
                job["updated_at"] = time.time()

            try:
                prompt = job["prompt"]
                duration = job["duration"]
                resolution = job["resolution"]
                image_data = job["image_data"]

                width, height = determine_resolution(resolution, total_vram_gb)
                num_frames = calculate_num_frames(duration)
                is_i2v = image_data is not None
                required_pipeline_type = "i2v" if is_i2v else "t2v"

                logger.info(f"Configuring generation:")
                logger.info(f"  - Mode: {'Image-to-Video' if is_i2v else 'Text-to-Video'}")
                logger.info(f"  - Prompt: '{prompt}'")
                logger.info(f"  - Resolution: {width}x{height}")
                logger.info(f"  - Target Frames: {num_frames} (~{num_frames/25:.1f}s at 25fps)")

                # Device check
                if not torch.cuda.is_available():
                    raise RuntimeError("CUDA is not available. Local generation requires a GPU.")

                # Lazy load / switch pipeline
                if current_pipeline_type != required_pipeline_type or pipe is None:
                    logger.info(f"Switching pipeline from {current_pipeline_type} to {required_pipeline_type}...")
                    if pipe is not None:
                        del pipe
                        pipe = None
                        gc.collect()
                        torch.cuda.empty_cache()

                    if required_pipeline_type == "i2v":
                        from diffusers import LTXImageToVideoPipeline
                        logger.info("Loading LTXImageToVideoPipeline (Lightricks/ltx-video)...")
                        pipe = LTXImageToVideoPipeline.from_pretrained(
                            'Lightricks/ltx-video', 
                            torch_dtype=torch.float16
                        )
                    else:
                        from diffusers import LTXPipeline
                        logger.info("Loading LTXPipeline (Lightricks/ltx-video)...")
                        pipe = LTXPipeline.from_pretrained(
                            'Lightricks/ltx-video', 
                            torch_dtype=torch.float16
                        )

                    # Memory optimizations
                    pipe.vae.enable_tiling()
                    pipe.vae.enable_slicing()

                    if total_vram_gb < 11.5:
                        logger.info(f"GPU total VRAM {total_vram_gb:.1f}GB is under 11.5GB threshold. Enabling model CPU offload.")
                        pipe.enable_model_cpu_offload()
                    else:
                        logger.info(f"GPU total VRAM {total_vram_gb:.1f}GB is sufficient. Moving pipeline to CUDA directly.")
                        pipe.to('cuda')

                    current_pipeline_type = required_pipeline_type

                # Run progress logging callback
                num_inference_steps = 50  # native default for high-quality LTX-Video
                
                def make_step_callback(req_id, total_steps):
                    def callback(pipeline, step, timestep, callback_kwargs):
                        percent = int(((step + 1) / total_steps) * 100)
                        percent = min(100, max(0, percent))
                        with jobs_lock:
                            if req_id in jobs:
                                jobs[req_id]["progress"] = percent
                        logger.info(f"Job {req_id} - Generation Progress: {percent}% (Step {step+1}/{total_steps})")
                        return callback_kwargs
                    return callback

                step_callback = make_step_callback(request_id, num_inference_steps)

                # Execute pipeline
                generator = torch.manual_seed(int(time.time()) % 1000000)
                
                if is_i2v:
                    logger.info("Decoding input image base64 data...")
                    image = decode_base64_image(image_data)
                    logger.info(f"Resizing input image to target resolution: {width}x{height}")
                    image = image.resize((width, height), Image.Resampling.LANCZOS)
                    
                    output = pipe(
                        image=image,
                        prompt=prompt,
                        width=width,
                        height=height,
                        num_frames=num_frames,
                        num_inference_steps=num_inference_steps,
                        generator=generator,
                        callback_on_step_end=step_callback
                    ).frames[0]
                else:
                    output = pipe(
                        prompt=prompt,
                        width=width,
                        height=height,
                        num_frames=num_frames,
                        num_inference_steps=num_inference_steps,
                        generator=generator,
                        callback_on_step_end=step_callback
                    ).frames[0]

                # Save video file
                from diffusers.utils import export_to_video
                output_filename = f"{request_id}.mp4"
                output_path = os.path.join(TEMP_DIR, output_filename)
                
                logger.info(f"Exporting video frames to file: {output_path}")
                export_to_video(output, output_path, fps=25)
                logger.info(f"Successfully generated video for job {request_id}")

                with jobs_lock:
                    job["status"] = "done"
                    job["video_path"] = output_path
                    job["duration"] = num_frames / 25.0
                    job["progress"] = 100

            except Exception as e:
                import traceback
                error_trace = traceback.format_exc()
                logger.error(f"Error executing job {request_id}: {e}\n{error_trace}")
                with jobs_lock:
                    job["status"] = "failed"
                    job["error"] = str(e)
                    job["progress"] = 0

            finally:
                with jobs_lock:
                    job["updated_at"] = time.time()
                job_queue.task_done()
                
                # Cleanup cache
                gc.collect()
                if torch.cuda.is_available():
                    torch.cuda.empty_cache()

        except Exception as e:
            logger.error(f"Background worker experienced an unhandled error: {e}")
            time.sleep(2)

# Start background worker thread
worker_thread = threading.Thread(
    target=background_worker_thread,
    name="VideoGenWorker",
    daemon=True
)
worker_thread.start()


# FastAPI routes

@app.get("/")
def health_check():
    return {
        "status": "healthy",
        "gpu_available": torch.cuda.is_available(),
        "total_vram_gb": round(total_vram_gb, 2) if torch.cuda.is_available() else 0.0,
        "queue_size": job_queue.qsize()
    }

@app.post("/v1/videos/generations")
def submit_generation(request: VideoGenerationRequest):
    if not request.prompt or not request.prompt.strip():
        raise HTTPException(status_code=400, detail="Prompt is required")

    request_id = str(uuid.uuid4())
    
    # Store job properties
    image_data_uri = None
    if request.image:
        image_data_uri = request.image.url

    with jobs_lock:
        jobs[request_id] = {
            "status": "pending",
            "progress": 0,
            "prompt": request.prompt,
            "duration": request.duration,
            "resolution": request.resolution,
            "image_data": image_data_uri,
            "created_at": time.time(),
            "updated_at": time.time(),
            "video_path": None,
            "error": None
        }

    logger.info(f"Received generation request. Assigned Request ID: {request_id}")
    job_queue.put(request_id)
    
    return {"request_id": request_id}

@app.get("/v1/videos/{request_id}")
def get_generation_status(request_id: str, request: Request):
    with jobs_lock:
        job = jobs.get(request_id)

    if not job:
        raise HTTPException(status_code=404, detail="Job not found")

    status = job["status"]
    progress = job.get("progress", 0)
    
    video = None
    if status == "done":
        base_url = str(request.base_url).rstrip("/")
        download_url = f"{base_url}/v1/videos/{request_id}/download"
        video = {
            "url": download_url,
            "duration": job.get("duration", 4.0)
        }

    error = None
    if status == "failed":
        error = {
            "message": job.get("error", "Unknown generation error")
        }

    return {
        "status": status,
        "progress": progress,
        "video": video,
        "error": error
    }

@app.get("/v1/videos/{request_id}/download")
def download_video(request_id: str):
    with jobs_lock:
        job = jobs.get(request_id)

    if not job:
        raise HTTPException(status_code=404, detail="Job not found")

    status = job["status"]
    if status != "done":
        raise HTTPException(status_code=400, detail=f"Video is not ready. Current status: {status}")

    video_path = job.get("video_path")
    if not video_path or not os.path.exists(video_path):
        raise HTTPException(status_code=404, detail="Video file not found on disk")

    # Serve the file. FileResponse supports range requests natively.
    return FileResponse(
        path=video_path,
        media_type="video/mp4",
        filename=f"ltx-video-{request_id}.mp4"
    )

if __name__ == "__main__":
    import uvicorn
    logger.info("Starting local LTX-Video API server on port 7860...")
    uvicorn.run(app, host="0.0.0.0", port=7860)
