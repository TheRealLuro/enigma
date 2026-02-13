# room format: {x_cordinate}{y_coordinate}{connection}{puzzletype}{type[N/S/F/R]}

import random
from collections import deque

DIR_INDEX = {"N": 0, "E": 1, "S": 2, "W": 3}
DIRS = [
    (0, -1, DIR_INDEX["N"], DIR_INDEX["S"]),  # N
    (0, 1, DIR_INDEX["S"], DIR_INDEX["N"]),   # S
    (1, 0, DIR_INDEX["E"], DIR_INDEX["W"]),   # E
    (-1, 0, DIR_INDEX["W"], DIR_INDEX["E"]),  # W
]

EXTRA_EDGE_PROB = 0.12
FARTHEST_SAMPLE_THRESHOLD = 2500
FARTHEST_SAMPLE_MAX = 200


room_types = {
    'A': [True, False, False, False], # 1
    'B': [False, True, False, False], # 2
    'C': [True, True, False, False],  # 3
    'D': [False, False, True, False], # 3
    'E': [True, False, True, False],  # 4
    'F': [False, True, True, False],  # 5
    'G': [True, True, True, False],   # 6
    'H': [False, False, False, True], # 4
    'I': [True, False, False, True],  # 5
    'J': [False, True, False, True],  # 6
    'K': [True, True, False, True],   # 7
    'L': [False, False, True, True],  # 7
    'M': [True, False, True, True],   # 5
    'N': [False, True, True, True],   # 9
    'O': [True, True, True, True],    # 10
}

ROOM_BY_CONNECTIONS = {tuple(v): k for k, v in room_types.items()}


puzzle_types = {
    'p': 'Pressure Plate Puzzle',
    'q': 'Quick Time Reaction Puzzle',
    'r': 'Riddle Puzzle',
    's': 'Sequence Memory Puzzle',
    't': 'Tile Rotation Puzzle',
    'u': 'Unlock Pattern Puzzle',
    'v': 'Valve Flow Puzzle',
    'w': 'Weight Balance Puzzle',
    'x': 'XOR Logic Puzzle',
    'y': 'Yarn / Path Untangle Puzzle',
    'z': 'Zone Activation Puzzle'
}


def parse_seed(seed):
    entries = [e for e in seed.strip().split("-") if e]
    rooms = []
    max_x = 0
    max_y = 0

    for entry in entries:
        comma_idx = entry.find(",")
        if comma_idx == -1:
            return None, ["Missing comma in entry"]
        x_part = entry[:comma_idx]
        rest = entry[comma_idx + 1:]
        if not x_part.isdigit():
            return None, ["Invalid x coordinate"]

        y_digits = []
        for ch in rest:
            if ch.isdigit():
                y_digits.append(ch)
            else:
                break
        if not y_digits:
            return None, ["Invalid y coordinate"]
        y_part = "".join(y_digits)
        tail = rest[len(y_part):]
        if len(tail) < 2:
            return None, ["Missing room_type or puzzle_type"]

        room_type = tail[0]
        puzzle_type = tail[1]
        nsfr = tail[2] if len(tail) > 2 else "N"

        if room_type not in room_types:
            return None, [f"Invalid room_type: {room_type}"]
        if puzzle_type not in puzzle_types:
            return None, [f"Invalid puzzle_type: {puzzle_type}"]
        if nsfr not in ("N", "S", "F", "R"):
            return None, [f"Invalid NSFR: {nsfr}"]

        x = int(x_part)
        y = int(y_part)

        rooms.append(
            {
                "cord": (x, y),
                "room_type": room_type,
                "puzzle_type": puzzle_type,
                "type": nsfr,
            }
        )
        max_x = max(max_x, x)
        max_y = max(max_y, y)

    size = max(max_x, max_y) + 1 if rooms else 0
    return {"rooms": rooms, "size": size}, []


def validate_seed(seed):
    parsed, errors = parse_seed(seed)
    if errors:
        return False, errors
    rooms = parsed["rooms"]
    size = parsed["size"]
    if size <= 0:
        return False, ["Empty seed"]

    grid_map = {}
    nsfr_counts = {"N": 0, "S": 0, "F": 0, "R": 0}
    for room in rooms:
        cord = room["cord"]
        if cord in grid_map:
            return False, [f"Duplicate coordinate {cord}"]
        grid_map[cord] = room
        nsfr_counts[room["type"]] += 1

    expected = size * size
    if len(grid_map) != expected:
        return False, [f"Missing rooms: expected {expected}, got {len(grid_map)}"]

    if nsfr_counts["S"] != 1 or nsfr_counts["F"] != 1:
        return False, ["Seed must contain exactly one S and one F"]

    if nsfr_counts["R"] > size // 2:
        return False, [f"Too many R rooms: {nsfr_counts['R']} > {size//2}"]

    # Check door consistency and bounds
    for (x, y), room in grid_map.items():
        connections = room_types[room["room_type"]]
        for dx, dy, cur_idx, nbr_idx in DIRS:
            nx, ny = x + dx, y + dy
            has_door = connections[cur_idx]
            if nx < 0 or ny < 0 or nx >= size or ny >= size:
                if has_door:
                    return False, [f"Door leads outside grid at {(x, y)}"]
                continue
            neighbor = grid_map.get((nx, ny))
            if neighbor is None:
                return False, [f"Missing neighbor at {(nx, ny)}"]
            neighbor_conn = room_types[neighbor["room_type"]][nbr_idx]
            if has_door != neighbor_conn:
                return False, [f"Mismatched door between {(x, y)} and {(nx, ny)}"]

    # Check S -> F path
    start = next(cord for cord, room in grid_map.items() if room["type"] == "S")
    goal = next(cord for cord, room in grid_map.items() if room["type"] == "F")
    q = deque([start])
    visited = {start}
    while q:
        x, y = q.popleft()
        if (x, y) == goal:
            return True, []
        connections = room_types[grid_map[(x, y)]["room_type"]]
        for dx, dy, cur_idx, nbr_idx in DIRS:
            if not connections[cur_idx]:
                continue
            nx, ny = x + dx, y + dy
            if (nx, ny) not in grid_map or (nx, ny) in visited:
                continue
            if not room_types[grid_map[(nx, ny)]["room_type"]][nbr_idx]:
                continue
            visited.add((nx, ny))
            q.append((nx, ny))

    return False, ["No path from S to F"]

# corners and edges have specific room types that can be used to ensure the outer rim walls are solid and the paths are valid.

# possible Bottom left corner: B, D, F
PTLC = ['B', 'D', 'F']

# possible Bottom right corner: D, H, L
PTRC = ['D', 'H', 'L']
    
# possible Top left corner: A, B, C
PBLC = ['A', 'B', 'C']

# possible top right corner: A, H, I
PBRC = ['A', 'H', 'I']

# possible left edge: A, B, C, D, E, F, G
PLE = ['A', 'B', 'C', 'D', 'E', 'F', 'G']

# possible bottom edge: B, D, F, H, J, L, N
PBE = ['B', 'D', 'F', 'H', 'J', 'L', 'N']

# possible right edge: A, D, E, H, I, L, M
PRE = ['A', 'D', 'E', 'H', 'I', 'L', 'M']

# possible top edge: A, B, C, H, I, J, K
PTE = ['A', 'B', 'C', 'H', 'I', 'J', 'K']


# The rest of the grid can be filled with any room type

# now lets get the cords for each grid size for those cases.


def get_grid_info(size):
    
    corners = [(0,0), (0,size-1), (size-1,0), (size-1,size-1)]
    left_edges = [(0, y) for y in range(1, size-1)]
    top_edges = [(x, 0) for x in range(1, size-1)]
    right_edges = [(size-1, y) for y in range(1, size-1)]
    bottom_edges = [(x, size-1) for x in range(1, size-1)]
    inside = [(x, y) for x in range(1, size-1) for y in range(1, size-1)]

    coordinates = {
        'corners': corners,
        'left_edges': left_edges,
        'bottom_edges': top_edges,
        'right_edges': right_edges,
        'top_edges': bottom_edges,
        'inside': inside
    }


    return coordinates
            
    # Time to assign room types, we start in the Bottom left corner left to right, after row complete move up start as rows left first and repeat until we reach the bottom right corner.

    # first create the checker 
def check_surrounding(room_to_check, grid_map, possible_types):
    current_x, current_y = room_to_check

    def neighbor_type(cord):
        return grid_map.get(cord)

    candidates = []
    for room_type in possible_types:
        if room_type not in room_types:
            continue
        connections = room_types[room_type]
        valid = True

        north = neighbor_type((current_x, current_y + 1))
        if north is not None:
            if room_types[north][DIR_INDEX["S"]] != connections[DIR_INDEX["N"]]:
                valid = False

        east = neighbor_type((current_x + 1, current_y))
        if east is not None:
            if room_types[east][DIR_INDEX["W"]] != connections[DIR_INDEX["E"]]:
                valid = False

        south = neighbor_type((current_x, current_y - 1))
        if south is not None:
            if room_types[south][DIR_INDEX["N"]] != connections[DIR_INDEX["S"]]:
                valid = False

        west = neighbor_type((current_x - 1, current_y))
        if west is not None:
            if room_types[west][DIR_INDEX["E"]] != connections[DIR_INDEX["W"]]:
                valid = False

        if valid:
            candidates.append(room_type)

    if not candidates:
        candidates = list(possible_types)

    return random.choice(candidates)


def generate_connections_prim(size):
    connections = {(x, y): [False, False, False, False] for x in range(size) for y in range(size)}
    start = (random.randrange(size), random.randrange(size))
    visited = {start}
    frontier = []

    def add_frontier(cell):
        x, y = cell
        for dx, dy, cur_idx, nbr_idx in DIRS:
            nx, ny = x + dx, y + dy
            if 0 <= nx < size and 0 <= ny < size:
                frontier.append((cell, (nx, ny), cur_idx, nbr_idx))

    add_frontier(start)

    while frontier:
        idx = random.randrange(len(frontier))
        cell, nbr, cur_idx, nbr_idx = frontier.pop(idx)
        if nbr in visited:
            continue
        connections[cell][cur_idx] = True
        connections[nbr][nbr_idx] = True
        visited.add(nbr)
        add_frontier(nbr)

    if EXTRA_EDGE_PROB > 0:
        for x in range(size):
            for y in range(size):
                for dx, dy, cur_idx, nbr_idx in ((1, 0, DIR_INDEX["E"], DIR_INDEX["W"]),
                                                 (0, 1, DIR_INDEX["S"], DIR_INDEX["N"])):
                    nx, ny = x + dx, y + dy
                    if nx >= size or ny >= size:
                        continue
                    cell = (x, y)
                    nbr = (nx, ny)
                    if connections[cell][cur_idx]:
                        continue
                    if random.random() < EXTRA_EDGE_PROB:
                        connections[cell][cur_idx] = True
                        connections[nbr][nbr_idx] = True

    return connections

def validate_paths(grid):
    size = 0
    for room in grid:
        x, y = room['cord']
        size = max(size, x + 1, y + 1)

    start = (0, 0)
    goal = (size - 1, size - 1)
    outline = get_grid_info(size)

    def build_grid_map():
        return {room['cord']: room for room in grid}

    def bfs_shortest_path():
        grid_map = build_grid_map()
        if start not in grid_map or goal not in grid_map:
            return []

        q = deque([start])
        parent = {start: None}

        directions = DIRS

        while q:
            x, y = q.popleft()
            if (x, y) == goal:
                break

            room_type = grid_map[(x, y)]['room_type']
            if room_type not in room_types:
                continue

            connections = room_types[room_type]
            for dx, dy, cur_idx, nbr_idx in directions:
                if not connections[cur_idx]:
                    continue
                nx, ny = x + dx, y + dy
                if (nx, ny) not in grid_map:
                    continue
                neighbor_type = grid_map[(nx, ny)]['room_type']
                if neighbor_type not in room_types:
                    continue
                if not room_types[neighbor_type][nbr_idx]:
                    continue
                if (nx, ny) not in parent:
                    parent[(nx, ny)] = (x, y)
                    q.append((nx, ny))

        if goal not in parent:
            return []

        path = []
        cur = goal
        while cur is not None:
            path.append(cur)
            cur = parent[cur]
        path.reverse()
        return path

    start_room = next((r for r in grid if r['cord'] == start), None)
    if start_room is None:
        return grid
    start_type = start_room['room_type'] or random.choice(PTLC)
    start_room['room_type'] = start_type

    max_attempts = 2000
    attempts = 0
    path = bfs_shortest_path()

    while not path and attempts < max_attempts:
        attempts += 1

        for room in grid:
            if room['cord'] != start:
                room['room_type'] = None

        grid_map = {start: start_type}

        for room in grid:
            cord = room['cord']
            if cord == start:
                room['room_type'] = start_type
                continue

            if cord in outline['corners']:
                if cord == (0, 0):
                    possible = PTLC
                elif cord == (size - 1, 0):
                    possible = PTRC
                elif cord == (0, size - 1):
                    possible = PBLC
                elif cord == (size - 1, size - 1):
                    possible = PBRC
                else:
                    possible = list(room_types.keys())
            elif cord in outline['bottom_edges']:
                possible = PBE
            elif cord in outline['left_edges']:
                possible = PLE
            elif cord in outline['right_edges']:
                possible = PRE
            elif cord in outline['top_edges']:
                possible = PTE
            else:
                possible = list(room_types.keys())

            room['room_type'] = check_surrounding(cord, grid_map, possible)
            grid_map[cord] = room['room_type']

        path = bfs_shortest_path()

    return grid


def assign_nsfr(grid, size):
    grid_map = {room['cord']: room for room in grid}
    all_coords = list(grid_map.keys())

    def reachable_from(start):
        if start not in grid_map:
            return {}
        q = deque([start])
        visited = {start: 0}
        while q:
            x, y = q.popleft()
            room_type = grid_map[(x, y)]['room_type']
            if room_type not in room_types:
                continue
            connections = room_types[room_type]
            for dx, dy, cur_idx, nbr_idx in DIRS:
                if not connections[cur_idx]:
                    continue
                nx, ny = x + dx, y + dy
                if (nx, ny) not in grid_map:
                    continue
                nbr_type = grid_map[(nx, ny)]['room_type']
                if nbr_type not in room_types:
                    continue
                if not room_types[nbr_type][nbr_idx]:
                    continue
                if (nx, ny) not in visited:
                    visited[(nx, ny)] = visited[(x, y)] + 1
                    q.append((nx, ny))
        return visited

    start = None
    goal = None
    best_dist = -1
    best_pairs = []

    n = len(all_coords)
    if n > FARTHEST_SAMPLE_THRESHOLD:
        sample_count = min(n, FARTHEST_SAMPLE_MAX)
        sample_starts = random.sample(all_coords, sample_count)
    else:
        sample_starts = all_coords

    for candidate_start in sample_starts:
        distances = reachable_from(candidate_start)
        if len(distances) <= 1:
            continue
        farthest_coord, farthest_dist = max(distances.items(), key=lambda item: item[1])
        if farthest_dist > best_dist:
            best_dist = farthest_dist
            best_pairs = [(candidate_start, farthest_coord)]
        elif farthest_dist == best_dist:
            best_pairs.append((candidate_start, farthest_coord))

    if best_pairs:
        start, goal = random.choice(best_pairs)

    if start is None:
        start = all_coords[0]
        remaining = [c for c in all_coords if c != start]
        goal = remaining[0] if remaining else start
    reward_cap = size // 2
    if reward_cap > 0:
        reward_count = random.randint(1, reward_cap)
    else:
        reward_count = 0

    candidates = [room['cord'] for room in grid if room['cord'] not in (start, goal)]
    reward_set = set(random.sample(candidates, reward_count)) if reward_count > 0 else set()

    for room in grid:
        cord = room['cord']
        if cord == start:
            room['type'] = 'S'
        elif cord == goal:
            room['type'] = 'F'
        elif cord in reward_set:
            room['type'] = 'R'
        else:
            room['type'] = 'N'


def generate_maze(size):
    connections = generate_connections_prim(size)
    grid = []

    for y in range(size):
        row = []
        for x in range(size):
            row.append((x, y))
        if y % 2 == 1:
            row.reverse()
        grid.extend(row)

    for cord in grid:
        room = {
            'cord': cord,
            'room_type': None,
            'puzzle_type': None,
            'NSFR': None
        }
        grid[grid.index(cord)] = room

    for room in grid:
        conn = connections[room['cord']]
        room['room_type'] = ROOM_BY_CONNECTIONS[tuple(conn)]

    maze = validate_paths(grid)
    assign_nsfr(maze, size)
    return maze
    


def assign_room_types(grid, outline):
    size = 0
    for x, y in outline['corners']:
        size = max(size, x + 1, y + 1)

    grid_map = {room['cord']: None for room in grid}

    for room in grid:
        cord = room['cord']
        if cord in outline['corners']:
            if cord == (0, 0):
                possible = PTLC
            elif cord == (size - 1, 0):
                possible = PTRC
            elif cord == (0, size - 1):
                possible = PBLC
            elif cord == (size - 1, size - 1):
                possible = PBRC
            else:
                possible = list(room_types.keys())
        elif cord in outline['bottom_edges']:
            possible = PBE
        elif cord in outline['left_edges']:
            possible = PLE
        elif cord in outline['right_edges']:
            possible = PRE
        elif cord in outline['top_edges']:
            possible = PTE
        else:
            possible = list(room_types.keys())

        room['room_type'] = check_surrounding(cord, grid_map, possible)
        grid_map[cord] = room['room_type']
    
    return grid

    

def get_seed(size):
    maze = generate_maze(size)

    parts = []

    for room in maze:
        x, y = room['cord']
        room_type = room['room_type'] 
        puzzle_type = random.choice(list(puzzle_types.keys()))
        nsfr = room.get('type', 'N')
        parts.append(f"{x},{y}{room_type}{puzzle_type}{nsfr}")
    
    return "-".join(parts)










    
