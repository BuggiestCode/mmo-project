# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a real-time MMO game server built with Node.js, WebSocket, and Express. The server manages player connections, real-time gameplay, terrain loading, and movement validation with server-side authority.

## Architecture

### Core Components

- **game_server.js** - Main WebSocket server handling player connections, authentication, movement, and game state synchronization
- **terrainLoader.js** - Synchronous terrain chunk loading system with coordinate transformation matching Unity client
- **routes/connectedClient.js** - WebSocket client wrapper with connection state management
- **routes/player.js** - Player class for runtime state and database operations
- **terrain/** - JSON chunk files containing walkability data (16x16 tiles per chunk)

### Key Systems

- **Authentication**: JWT-based with `JWT_SECRET` environment variable required
- **Terrain System**: Server-authoritative chunk loading that blocks until ready, prevents client-server desync
- **Movement Validation**: Server validates all movement against loaded terrain data
- **Connection Management**: Soft reconnection support with 30-second timeout window
- **Game Loop**: 500ms tick rate for state synchronization

## Commands

### Development
```bash
node game_server.js              # Start the game server
```

### Environment Setup
- Set `JWT_SECRET` environment variable (required)
- Set `PORT` environment variable (optional, defaults to 8080)

### Database
- PostgreSQL schema: `db/schema.sql`
- Players table tracks user_id, position (x,y), and facing direction

## Deployment

### Docker
```bash
docker build -t mmo-game-server .
docker run -p 8080:8080 -e JWT_SECRET=your_secret mmo-game-server
```

### Fly.io
```bash
fly deploy
```

## Terrain System Details

### Coordinate System
- Chunks are 16x16 tiles
- World coordinates converted to chunk coordinates using `worldPositionToChunkCoord()`
- Local tile coordinates (0-15) calculated with `worldPositionToTileCoord()`
- **CRITICAL**: Coordinate system must match Unity client exactly

### Chunk Loading
- Synchronous loading blocks game tick until chunk is ready
- Reference counting tracks players per chunk
- Automatic cleanup after 30 seconds of no references
- Chunk files: `terrain/chunk_X_Y.json` with walkability array (256 booleans)

## WebSocket Protocol

### Client → Server
- `auth` - JWT authentication with token
- `move` - Movement request with dx, dy coordinates
- `quit` - Explicit disconnect

### Server → Client  
- `spawnPlayer` - New player joined
- `spawnOtherPlayers` - Initial load of existing players
- `quitPlayer` - Player disconnected
- `state` - Game state updates (500ms interval)

## Development Notes

- Server maintains authoritative game state
- All movement validated server-side before applying
- Graceful shutdown handles SIGTERM/SIGINT
- Connection state tracked for soft reconnection
- Player positions synchronized every 500ms via dirty flag system