# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a multiplayer online game (MMO) with a distributed architecture consisting of:
- **Unity Frontend**: WebGL-based game client with real-time terrain rendering and multiplayer support
- **Auth/Backend Server**: Node.js/Express server handling authentication and serving the Unity build
- **Game World Servers**: WebSocket-based game servers managing player state and world simulation
- **PostgreSQL Database**: Dual database setup for authentication and game state persistence

## Architecture

### System Components

**Frontend (Unity WebGL)**
- Located in `mmo-frontend/MMO_FrontEnd/`
- WebSocket connection to game servers at `wss://mmo-world#-production.fly.dev` or `wss://mmo-world#-staging.fly.dev` where # is the world number
- Real-time terrain chunk loading from JSON data
- Click-to-move player movement with pathfinding
- Overhead chat system and UI panels

**Auth/Backend Server** (`mmo-backend/`)
- Express server on port 8080
- JWT authentication with bcrypt password hashing
- Serves Unity WebGL build from `/public`
- PostgreSQL connection for user management

**Game Server** (`mmo-game-server/`)
- WebSocket server handling real-time game state
- Player position synchronization across clients
- Terrain data serving and chunk management
- Session management to prevent duplicate logins
- Pathfinding system for player movement

**Database Structure**
- **Auth DB** (`mmo_auth`): Users table, active_sessions table
- **Game DB** (`mmo_game`): Players table with position/state data

### Key Data Flow
1. User authenticates via backend → receives JWT token
2. Unity client connects to game server with JWT
3. Game server validates token and creates session
4. Real-time position/chat updates via WebSocket
5. Terrain chunks loaded on-demand from server

## Development Commands

### Backend Services

```bash
# Auth/Backend Server
cd mmo-backend
npm install
node server.js  # Starts on port 8080

# Game Server
cd mmo-game-server
npm install
node game_server.js  # Starts WebSocket server on port 8080
```

### Unity Development

1. Open `mmo-frontend/MMO_FrontEnd/` in Unity Editor 2022.3+
2. Main scenes:
   - `Assets/Scenes/MainMenu.unity` - Login/authentication
   - `Assets/Scenes/PlayScene.unity` - Main game world
   - `Assets/Scenes/Terrain_Test_Area.unity` - Terrain testing

### Building Unity WebGL
Use the custom Unity Editor tool:
- Window → Build and Deploy → Build to `mmo-backend/public`

### Database Setup

```bash
# Create databases
psql -U postgres
CREATE DATABASE mmo_auth;
CREATE DATABASE mmo_game;

# Import schemas
psql -U postgres -d mmo_auth < mmo-db/auth_schema.sql
psql -U postgres -d mmo_game < mmo-db/game_schema.sql
```

## Deployment (Fly.io)

### Environment Variables Required

**Auth/Backend Server**:
- `AUTH_DATABASE_URL`: PostgreSQL connection for auth DB
- `JWT_SECRET`: Shared secret for JWT signing
- `PORT`: Server port (default 8080)

**Game Server**:
- `AUTH_DATABASE_URL`: Connection to auth database
- `GAME_DATABASE_URL`: Connection to game database  
- `JWT_SECRET`: Must match auth server's secret
- `WORLD_NAME`: Unique identifier for world instance
- `PORT`: WebSocket server port (default 8080)

### Deployment Process

```bash
# Deploy backend/auth server
cd mmo-backend
fly deploy -a mmo-auth-frontend-staging --config fly-staging.toml

# Deploy game world server
cd mmo-game-server
fly deploy -a mmo-world1-staging --config worlds/staging/fly-world1-staging.toml
```

## Code Organization

### Unity Scripts Structure
- `Assets/Scripts/WebsocketHandling/` - Network communication
- `Assets/Scripts/Terrain/` - Terrain generation and rendering
- `Assets/Scripts/Player/` - Player movement and state
- `Assets/Scripts/MainMenu/` - Authentication UI and logic
- `Assets/Scripts/Chat/` - Chat system implementation
- `Assets/Editor/` - Custom Unity editor tools

### Backend Structure
- `mmo-backend/routes/auth.js` - Authentication endpoints
- `mmo-backend/server.js` - Main Express server
- `mmo-game-server/game_server.js` - WebSocket game server
- `mmo-game-server/routes/` - Game logic modules
- `mmo-game-server/terrain/` - Terrain chunk JSON data

## Testing & Debugging

### Unity Testing
- Use Play Mode in Unity Editor for runtime testing
- TerrainManager has context menu options for testing chunk loading
- Check Unity Console for WebSocket connection status

### Backend Testing
- No automated tests configured yet
- Use `console.log` for debugging
- Monitor Fly.io logs: `fly logs -a <app-name>`

### Common Issues
- WebSocket connection failures: Check JWT_SECRET matches across services
- Terrain not loading: Verify chunk JSON files exist in `terrain/` directory
- Double login prevention: Active sessions table manages connection state

## Important Implementation Details

### Terrain System
- 16x16 tile chunks with 17x17 vertex grids for seamless edges
- Chunks loaded based on player proximity
- Elevation and walkability data stored per chunk
- JSON format for terrain data exchange

### Player Movement
- Client-side click detection with raycast
- Server validates movement against walkability grid
- Position interpolation for smooth movement
- Pathfinding calculated server-side

### Session Management
- Single session per user enforced via database
- Heartbeat system to detect disconnections
- Automatic cleanup of stale sessions on server restart

### Security Considerations
- JWT tokens expire after 24 hours
- Passwords hashed with bcrypt
- Environment variables for all secrets
- No sensitive data in client-side code