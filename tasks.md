# Enigma Game Implementation Spec

This document defines the requirements for implementing the Enigma game inside the existing project.  
The goal is to make this spec fully executable by an AI coding agent (Codex).

---

# 0. Core Requirements

## Tech Stack Constraints
- The game **must be implemented using C# Blazor**
- Required files:
  - `Game.razor`
  - `Game.cs` (or multiple `.cs` files if needed)
  - `game.js`
  - `app.css`
  - `index.html`
- You may add supporting `.cs` files if needed, but everything must integrate cleanly.

## Integration Requirements
The game must work **inside the existing project** without breaking existing functionality.

### Before implementing
1. Inspect all folders and files in `/Enigma` to understand:
   - Routing
   - Styling patterns
   - Component structure
   - existing game page Game.razor
   - game end is Gameend.razor
2. Read `backend.md` to understand available APIs.
3. Inspect implementations in:
   - `/enigma_server/apis`

Follow existing architecture and API patterns.

---

# 1. Visual System (Placeholder Mode)

## Feature flag
Implement visuals behind a feature flag:

```csharp
bool UsePlaceholderGraphics = true;
```

### Default behavior
- Room background = plain color
- Player = simple square (white or black)

---

## Future-ready graphics system
Structure the code for easy replacement with real assets.

### Requirements
- Use dictionaries keyed by:
  - `room_connection`
  - animation direction
- Keys must match gameplay logic.

---

## Background images

Future mapping:

```csharp
Dictionary<char, string> RoomBackgrounds;
```

- Selected by `room_connection` type
- Placeholder = plain color

---

## Player sprite system
- Single sprite sheet
- Placeholder = square
- Future:
  - Directional animations (Up, Down, Left, Right)
  - Animation rules:
    - Starts when movement starts
    - Stops when movement stops

---

# 2. Map Generation from Seed

The entire map is generated from a seed string.

## Seed Format

```
difficulty-x,y[Room_Connection][Puzzle_type][Room_type]-x,y[...]
```

Example:

```
medium-0,0ApS-1,0BpN-1,1CpF
```

Each room entry:

```
x,y[Room_Connection][Puzzle_type][Room_type]
```

---

## 2.1 Coordinates
- `x,y` = position on global room grid
- Used to:
  - Build layout
  - Detect neighbors

---

## 2.2 Room Connections

### Format
Single char representing doors in NESW order:

```
[N, E, S, W]
```

### Mapping

```python
room_connection = {
 'A': [True, False, False, False],
 'B': [False, True, False, False],
 'C': [True, True, False, False],
 'D': [False, False, True, False],
 'E': [True, False, True, False],
 'F': [False, True, True, False],
 'G': [True, True, True, False],
 'H': [False, False, False, True],
 'I': [True, False, False, True],
 'J': [False, True, False, True],
 'K': [True, True, False, True],
 'L': [False, False, True, True],
 'M': [True, False, True, True],
 'N': [False, True, True, True],
 'O': [True, True, True, True],
}
```

---

## 2.3 Puzzle Types

```python
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
```

---

## 2.4 Room Types

```python
Room_type = {
 'N': Normal,
 'S': Start,
 'F': Finish,
 'R': Reward
}
```

---

## Important: Every Room Has a Puzzle
All rooms contain a puzzle, including:
- Start rooms
- Finish rooms
- Reward rooms

Room type affects **extra behavior**, not puzzle existence.

---

# 3. Player Behavior

## Spawn
- Player starts in the `S` room.

---

## Movement Between Rooms

Direction logic:
- N → y + 1
- E → x + 1
- S → y - 1
- W → x - 1

---

## Rewards

### Puzzle rewards
Completing puzzles grants gold.

### Reward rooms
Gold spawns after puzzle completion.

### Difficulty multipliers

```
Easy   = 1.0
Medium = 1.25
Hard   = 2.0
```

---

# 4. Gameplay Rules

## No out-of-bounds
- Player must never leave the map
- Clamp movement

---

## Puzzle lock-in rule
Player cannot leave a room until:
- Puzzle is completed

---

## Fixed play area
- Room size = **1080px × 1080px**
- Player = 60px square

---

# 5. Rendering Model

## Single-room rendering
- Only render the current room
- Do NOT render the entire maze

---

# 6. HUD Elements

## Top-right display

```
Room: (x, y)
```

---

# 7. Game Completion

## Win Condition
1. Player enters Finish room
2. Completes puzzle
3. Black hole appears
4. Player touches black hole → Win

---

## End-game overlay
Display:
- Total gold collected
- Completion time (HH:MM:SS:MS)
- Username

---

## Post-game input
Allow user to enter a **Map Name**

---

# 8. Code Quality Expectations
- Modular architecture
- Strong typing
- Deterministic seed parsing
- Clean separation of:
  - Rendering
  - Game state
  - Movement logic
  - Puzzle logic
