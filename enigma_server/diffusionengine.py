import threading

import torch
from diffusers import DPMSolverMultistepScheduler
from diffusers import StableDiffusionXLImg2ImgPipeline


_pipe = None
_pipe_lock = threading.Lock()


def _build_pipe():
    has_cuda = torch.cuda.is_available()
    device = "cuda" if has_cuda else "cpu"
    dtype = torch.float16 if has_cuda else torch.float32

    pipe = StableDiffusionXLImg2ImgPipeline.from_pretrained(
        "stabilityai/sdxl-turbo",
        torch_dtype=dtype,
    ).to(device)

    # Karras-style DPMSolver schedule for cleaner gradients and smoother structure.
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(
        pipe.scheduler.config,
        use_karras_sigmas=True,
        algorithm_type="dpmsolver++",
    )

    pipe.safety_checker = None
    pipe.requires_safety_checker = False

    if has_cuda:
        pipe.enable_attention_slicing()
        try:
            # Keep VAE dtype aligned with pipeline inputs to avoid dtype mismatch errors.
            pipe.vae.to(dtype=dtype)
            pipe.vae.enable_slicing()
            pipe.vae.enable_tiling()
        except Exception:
            pass

    return pipe


def get_pipe():
    global _pipe
    if _pipe is not None:
        return _pipe

    with _pipe_lock:
        if _pipe is None:
            print("Loading diffusion model into memory...")
            _pipe = _build_pipe()

    return _pipe


def preload_pipe():
    return get_pipe()
