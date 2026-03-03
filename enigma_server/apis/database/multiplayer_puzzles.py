from __future__ import annotations


CO_OP_PUZZLE_CATALOG = {
    "easy": {
        "p": {
            "name": "Pressure Systems",
            "description": "Two plates. Both players must stand on their own plate simultaneously.",
            "core_mechanic": "Simple coordination",
        },
        "q": {
            "name": "Synchronized Reaction",
            "description": "Both players stop their pulse meters within half a second of each other.",
            "core_mechanic": "Shared timing",
        },
        "r": {
            "name": "Divided Knowledge Riddle",
            "description": "One player sees the question and the other sees the choices.",
            "core_mechanic": "Split information",
        },
        "s": {
            "name": "Split Memory Sequence",
            "description": "Player one sees the first half of the sequence and player two sees the second half.",
            "core_mechanic": "Combined recall",
        },
        "t": {
            "name": "Dual Grid Rotation",
            "description": "Each player rotates half of the grid to connect a shared path.",
            "core_mechanic": "Shared board logic",
        },
        "u": {
            "name": "Opposing Pattern Input",
            "description": "Player one enters the shown pattern and player two enters the reverse.",
            "core_mechanic": "Mirrored input",
        },
        "v": {
            "name": "Flow Transfer Network",
            "description": "Opening a valve on one side changes flow for the other side.",
            "core_mechanic": "Cross-board balancing",
        },
        "w": {
            "name": "Distributed Weight Balance",
            "description": "Each player places weights so the combined total hits the target.",
            "core_mechanic": "Shared arithmetic",
        },
        "x": {
            "name": "Binary Echo System",
            "description": "Both players must reach the same binary output.",
            "core_mechanic": "Cooperative bit matching",
        },
        "y": {
            "name": "Crossing Signal Lines",
            "description": "Each player routes half the lines without causing a shared crossing.",
            "core_mechanic": "Split routing",
        },
        "z": {
            "name": "Spatial Sync Zones",
            "description": "Both players must stand in the correct zones at the same time.",
            "core_mechanic": "Movement sync",
        },
    },
    "medium": {
        "p": {
            "name": "Pressure Systems",
            "description": "Three plates appear but only two are valid, and player roles affect which plates can be activated.",
            "core_mechanic": "Role-based coordination",
        },
        "q": {
            "name": "Synchronized Reaction",
            "description": "Meters move at different speeds and both stops must land in the target together.",
            "core_mechanic": "Asymmetric timing",
        },
        "r": {
            "name": "Divided Knowledge Riddle",
            "description": "Each player sees partial clues and must reconcile contradictions.",
            "core_mechanic": "Cooperative deduction",
        },
        "s": {
            "name": "Split Memory Sequence",
            "description": "Both players see distorted versions of one sequence and must reconstruct the truth.",
            "core_mechanic": "Shared correction",
        },
        "t": {
            "name": "Dual Grid Rotation",
            "description": "Rotating a tile on one board rotates a mirrored tile on the other board.",
            "core_mechanic": "Mirrored transforms",
        },
        "u": {
            "name": "Opposing Pattern Input",
            "description": "The pattern rule is hidden and players must infer whether it is mirrored or inverted.",
            "core_mechanic": "Rule discovery",
        },
        "v": {
            "name": "Flow Transfer Network",
            "description": "Flow must be balanced between both players' rooms or the puzzle resets.",
            "core_mechanic": "Shared conservation",
        },
        "w": {
            "name": "Distributed Weight Balance",
            "description": "Weights multiply differently for each player, forcing communication to solve the equation.",
            "core_mechanic": "Split multipliers",
        },
        "x": {
            "name": "Binary Echo System",
            "description": "One player flips bits while the other sees output only and must guide the solve.",
            "core_mechanic": "Distributed logic",
        },
        "y": {
            "name": "Crossing Signal Lines",
            "description": "Drawing a line on one side blocks a route on the other side.",
            "core_mechanic": "Cross-board constraints",
        },
        "z": {
            "name": "Spatial Sync Zones",
            "description": "Zones activate in opposite order for each player and must be timed together.",
            "core_mechanic": "Opposed sequencing",
        },
    },
    "hard": {
        "p": {
            "name": "Pressure Systems",
            "description": "Four resonance plates interact and both players may need to swap mid-charge to reach harmonic equilibrium.",
            "core_mechanic": "Nonlinear cooperation",
        },
        "q": {
            "name": "Synchronized Reaction",
            "description": "Player one sees the target window while player two sees the pulse. Neither player has the full picture alone.",
            "core_mechanic": "Information asymmetry",
        },
        "r": {
            "name": "Divided Knowledge Riddle",
            "description": "Each player sees different truth-lie statements and only the merged view is solvable.",
            "core_mechanic": "Combined truth assignment",
        },
        "s": {
            "name": "Split Memory Sequence",
            "description": "Two overlapping sequences appear and each player sees different interference patterns.",
            "core_mechanic": "Interference filtering",
        },
        "t": {
            "name": "Dual Grid Rotation",
            "description": "Boards are rotated relative to each other, so each rotation must be mentally transformed.",
            "core_mechanic": "Rotational cooperation",
        },
        "u": {
            "name": "Opposing Pattern Input",
            "description": "Player one sees the transformation rule while player two sees the pattern to transform.",
            "core_mechanic": "Split transform logic",
        },
        "v": {
            "name": "Flow Transfer Network",
            "description": "A hidden conservation law spans both rooms and must be deduced through shared experimentation.",
            "core_mechanic": "Shared hidden system",
        },
        "w": {
            "name": "Distributed Weight Balance",
            "description": "Pads on one board change multipliers on the other board, turning the puzzle into cooperative algebra.",
            "core_mechanic": "Cross-board equations",
        },
        "x": {
            "name": "Binary Echo System",
            "description": "Each player sees different transformation operators and must jointly compute the target state.",
            "core_mechanic": "Distributed computation",
        },
        "y": {
            "name": "Crossing Signal Lines",
            "description": "One player sees true intersections while the other sees false ones and must separate signal from noise.",
            "core_mechanic": "Topological misdirection",
        },
        "z": {
            "name": "Spatial Sync Zones",
            "description": "Each zone changes the next required zone for the other player based on last movement.",
            "core_mechanic": "Recursive coordination",
        },
    },
}
