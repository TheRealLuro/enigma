<h1 align="center">Enigma</h1>

<p align="center">
  <strong>Seeds. Mazes. Diffusion. Mystery.</strong>
</p>

<p align="center">
  A procedural maze platform where each maze carries a deterministic identity, a playable space, and a collectible visual presence.
</p>

<p align="center">
  <img alt="Status" src="https://img.shields.io/badge/status-active-success" />
  <img alt="Built With" src="https://img.shields.io/badge/built%20with-Python%20%7C%20Blazor-blue" />
  <img alt="AI Powered" src="https://img.shields.io/badge/AI-diffusion-purple" />
  <img alt="Backend" src="https://img.shields.io/badge/backend-Dockerized-informational" />
  <img alt="Database" src="https://img.shields.io/badge/database-MongoDB-green" />
  <img alt="Realtime" src="https://img.shields.io/badge/realtime-Redis-orange" />
  <img alt="License" src="https://img.shields.io/badge/license-proprietary-lightgrey" />
</p>

<p align="center">
  <a href="https://buymeacoffee.com/enigmamaze">
    <img alt="😴 Buy me an Energy Drink" src="https://img.shields.io/badge/%F0%9F%98%B4%20Buy%20us%20an-Energy%20Drink-ffdd57?logo=buymeacoffee&logoColor=000000" />
  </a>
</p>
<p align="center">
  helps keep it running and is very appreciated, but not required😛
</p>

---

## LINK: www.enigm4.fun
> you dont have to run it, we host it. have fun

## Creators

**Jason K.**  
**Colton C.**

Enigma is moving toward a maze ecosystem instead of a one-off maze generator. The target is not just procedural layouts. The target is generated spaces that feel worth exploring, remembering, and trading.


---

## What Is Enigma?

Enigma is a full-stack procedural maze project built around one rule:

> No two valid maze seeds should resolve into the same maze identity.

Each maze can carry:
- A deterministic layout
- A puzzle set embedded into rooms
- A generated visual theme
- Ownership and discovery history
- Marketplace and leaderboard data

The project combines procedural generation, AI-assisted visual identity, social systems, and live co-op into one stack.

---

## At A Glance

| Area | Current State |
|------|---------------|
| Gameplay | Playable single-player and co-op maze runs |
| Puzzles | Easy, medium, and hard puzzle sets with active room interaction work |
| Accounts | Signup, login, secure session flow, profile pages, friends |
| Economy | Marketplace, item shop, inventory, Maze Nuggets, ownership tracking |
| Realtime | Redis-backed co-op session sync with WebSocket updates |
| Art Pipeline | Seed-based map artwork generated through diffusion workflows |

---

## Core Concepts

### Seed-Based Generation
- Deterministic maze creation from a seed
- Layout validation and auditing
- Stable maze identity tied to the seed

### Puzzle-Embedded Exploration
- Room-gated progression
- Difficulty-based puzzle sets
- In-room interaction flow with puzzle overlays

### Diffusion-Based Visual Identity
Instead of showing plain maze diagrams, Enigma generates themed map artwork through an image pipeline.

`Seed -> Noise Generation -> Diffusion -> Theme Refinement -> Map Artwork`

Every maze is meant to feel identifiable, not disposable.

---

## Features

### Implemented

- Seed-based maze generation and validation
- Single-player maze runs
- Co-op session creation, invites, join, ready flow, and shared room progression
- WebSocket-backed multiplayer session sync
- Puzzle systems across easy, medium, and hard tiers
- AI-generated themed map artwork
- User signup, login, secure session flow, and protected routes
- Profile pages, friends, friend requests, and player search
- Map ownership, discovery tracking, and profile avatars from owned map art
- Marketplace map buying and selling
- Item shop, inventory, and run loadout selection
- Leaderboards for maps and players
- Loss handling for abandoned runs
- Dockerized backend with MongoDB and Redis support

### In Active Development

- Room-native puzzle interactions and more diegetic puzzle stations
- More co-op polish around movement feel and session lifecycle
- More theme and puzzle variety
- Economy balancing and progression tuning
- Production hosting hardening

---

## Architecture

### Frontend

- **Blazor / ASP.NET Core**
- Shared app shell, protected routes, and session-aware navigation
- Gameplay renderer for single-player and co-op rooms
- API proxy layer for backend communication

### Backend

- **Python / FastAPI**
- REST APIs for users, mazes, economy, marketplace, and multiplayer
- Redis-backed co-op session state
- Image generation and diffusion orchestration

### Datastores

**MongoDB**
- Users
- Maps
- Marketplace listings
- Inventory and economy state

**Redis**
- Multiplayer sessions
- Invites
- Real-time room sync

### Hosting

- Frontend host: Render
- Backend: Dockerized Python service
- Realtime: Redis
- Current development flow supports local Docker and public backend tunneling

---


## Maze Themes

Current curated theme labels:

- Neural Membrane
- Cartoon
- Dungeon
- Sewer
- Hedge
- Haunted House
- UnderWater
- Cave
- Forest
- City
- Lab

Themes are selected by the backend image pipeline and used throughout the UI for maps, profiles, and leaderboards.

---

## Diffusion System

The map-art pipeline is not just a screenshot pass over maze data.

Current image generation work includes:

- Seed-derived structural noise generation
- Theme-specific prompt packs
- Theme-specific diffusion refinement pipelines
- CPU/GPU-aware execution
- Map image persistence and maintenance tooling

The visual pipeline has evolved through several stages:

| Stage | Result |
|------|--------|
| Grid rendering | Functional but flat |
| Static noise | Unique but visually weak |
| Diffusion pipeline | Themed collectible map artwork |

---

## Marketplace And Economy

Enigma treats mazes as player-facing assets, not only levels.

Current economy systems include:

- Map buying and selling
- Map ownership transfer
- Item shop and inventory
- Maze Nuggets as the in-game currency
- Bank dividend rules
- Recycling flow for owned maps

This is still being balanced. The immediate goal is clarity first, then tighter scarcity and pricing.

---

## Roadmap

### Gameplay
- More room-native puzzles that use the full game screen
- Stronger puzzle interaction points in-room
- Better puzzle readability and less UI clutter
- Additional difficulty tuning and puzzle expansion

### Multiplayer
- Smoother remote player interpolation
- More robust session recovery and abandon detection
- More co-op-specific puzzle content
- Better live feedback in co-op rooms

### Social
- More profile polish
- Better friend and activity visibility
- More discoverability around player-created value

### Economy
- Better balance for map value, rewards, and item usefulness
- More item variety with clearer in-run purpose
- Continued marketplace polish

### AI And Content
- More theme coverage
- Better image-generation consistency
- More authored prompt-pack control and curation

---

## Performance Goals

- Fast room rendering on normal desktop hardware
- GPU acceleration for image generation when available
- Stable CPU fallback when GPU is unavailable
- Responsive co-op state updates without destroying play feel

---

## Tech Stack

| Layer | Technology |
|------|------------|
| Frontend | Blazor / ASP.NET Core |
| Backend | Python / FastAPI |
| Containers | Docker |
| Database | MongoDB |
| Realtime | Redis |
| Image Generation | Diffusion pipeline |
| Hosting | Container-based development flow with hosted frontend deployment |

---

## Current Focus

The current work is centered on:

- Making the game screen feel better to actually play
- Moving more puzzle logic into room interaction instead of side-panel UI
- Making more engaging puzzles and gameplay
- Tightening co-op synchronization and abandon handling
- Keeping profile, marketplace, and economy systems coherent as features expand
- Building real value into maps and progression

This is no longer just a login-and-leaderboard prototype. The project already has a playable loop, persistent accounts, map ownership, economy systems, and live co-op. The work now is about making the whole thing feel sharper, cleaner, and more deliberate.

---

## Vision

Enigma is aiming at:

- Infinite unique maze identities
- Collectible AI-themed worlds
- Social discovery and ownership
- A maze game that feels authored even when it is generated

Every maze should be:

- Discoverable
- Ownable
- Remembered

---

## Philosophy

Enigma is not just a maze generator.

It is a project where:

- Algorithms create structure
- AI creates identity
- Players create value

> Every maze is an enigma.

---

## Support

If you want to support development directly:

[😴 Buy me an Energy Drink](https://buymeacoffee.com/enigmamaze)
