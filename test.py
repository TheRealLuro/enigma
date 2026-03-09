import argparse
import sys
import time
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parent
SERVER_DIR = ROOT_DIR / "enigma_server"
sys.path.insert(0, str(SERVER_DIR))

from enigma_server.imagegen import generate_map_image  
from enigma_server.apis.maze.maze import get_seed  


DEFAULT_DIFFICULTY = "hard"
DEFAULT_SIZE = 10



def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate and save noise + final diffusion map images."
    )
    parser.add_argument(
        "--difficulty",
        default=DEFAULT_DIFFICULTY,
        help=f"Difficulty prefix to prepend to seed (default: {DEFAULT_DIFFICULTY}).",
    )
    parser.add_argument(
        "--size",
        type=int,
        default=DEFAULT_SIZE,
        help=f"Maze size passed to get_seed(size) (default: {DEFAULT_SIZE}).",
    )
    parser.add_argument(
        "--noise-out",
        default="noise.png",
        help="Path for the pre-diffusion noise/base image (default: noise.png).",
    )
    parser.add_argument(
        "--final-out",
        default="final.png",
        help="Path for the final diffusion image (default: final.png).",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    raw_seed = get_seed(args.size)
    seed = f"{args.difficulty}-{raw_seed}"
    print(f"Seed: {seed}")


    start = time.perf_counter()
    final_img = generate_map_image(seed=seed, use_diffusion=True)
    final_img.save(args.final_out, format="PNG", optimize=True)
    final_time = time.perf_counter() - start

    print(f"Saved final image: {Path(args.final_out).resolve()} ({final_time:.2f}s)")


if __name__ == "__main__":
    main()
