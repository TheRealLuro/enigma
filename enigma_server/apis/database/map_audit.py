import numpy as np
import sys
from pathlib import Path


difficulties = {
        "easy" : 1,
        "medium" : 1.25,
        "hard" : 1.5,
        
    }


def map_audit(seed):

    rooms = seed.split("-")

    difficulty = rooms[0]
    rooms.remove(rooms[0])
    number_of_rooms = len(rooms)
    how_many_r = 0
    

    for room in rooms:
        if room[5] == "R":
            how_many_r += 1
        else: continue

    diff_mult = difficulties[difficulty]
    r_count_mult = how_many_r / 1.5
    nor_mult = number_of_rooms / 16 # 2x2 is the smallest so it should give a very low mult

    # each map base value is 100
    value = (((100 * diff_mult) * r_count_mult) * nor_mult)

    return value