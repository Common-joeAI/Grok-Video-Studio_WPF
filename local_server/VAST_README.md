# Cloud GPU via Vast.ai (Rent Instead of Buy)

Don't have a powerful enough GPU? Rent one on [Vast.ai](https://vast.ai) for pennies per run.

## Quick Start

```powershell
cd local_server

# Interactive (pick GPU tier)
.\vast_provision.ps1

# Or specify a tier directly
.\vast_provision.ps1 -Tier 4090    # RTX 4090 (24GB) ~$0.30/hr
.\vast_provision.ps1 -Tier A100    # A100 (80GB)  ~$1.50/hr
.\vast_provision.ps1 -Tier H100    # H100 (80GB)  ~$2.50/hr
.\vast_provision.ps1 -Tier Auto    # Cheapest 16GB+ under $0.50/hr
```

The script will:
1. Install the Vast.ai CLI
2. Ask for your API key (from https://vast.ai/console/api/)
3. Search for available GPUs
4. Rent one and deploy the LTX-Video server
5. Print the URL to paste into **Settings → Local Server URL**

## Managing Instances

```powershell
# Check status
.\vast_provision.ps1 -Status

# List all instances
.\vast_provision.ps1 -ListInstances

# Stop & destroy (stop paying)
.\vast_provision.ps1 -Teardown
```

## Cost Estimates

| GPU       | VRAM  | $/hr   | 37-clip chain (~90 min) |
|-----------|-------|--------|-------------------------|
| RTX 4090  | 24GB  | $0.30  | ~$0.45                  |
| A100      | 80GB  | $1.50  | ~$2.25                  |
| H100      | 80GB  | $2.50  | ~$3.75                  |

Compare to your 4060 (free, but slower) — rent only when you need speed.

## How It Works

1. The script provisions a Vast.ai instance with a CUDA Docker image
2. It copies `video_server.py` and `requirements.txt` to the instance
3. The server auto-starts and downloads the LTX-Video model
4. You paste the returned URL into Settings and click Test Connection
5. The app talks to the cloud GPU exactly like it talks to localhost

No code changes needed — just swap the URL.
