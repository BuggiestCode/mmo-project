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

**Game Server** (`mmo-game-server-dotnet/MMOGameServer/`)
- ASP.NET Core WebSocket server built with .NET 8
- Dependency injection with singleton services for shared state
- Real-time player movement with A* pathfinding
- Background game loop service for deterministic state updates
- Concurrent chunk-based terrain management system
- JWT authentication middleware with session management

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

# Game Server (.NET)
cd mmo-game-server-dotnet/MMOGameServer
dotnet restore
dotnet run  # Starts WebSocket server on port 8080

# Alternative build and run
dotnet build
dotnet bin/Debug/net8.0/MMOGameServer.dll

# Development with environment variables
cd mmo-game-server-dotnet/MMOGameServer
export AUTH_DATABASE_URL="postgres://username:password@localhost:5432/mmo_auth"
export GAME_DATABASE_URL="postgres://username:password@localhost:5432/mmo_game" 
export JWT_SECRET="your-secret-key"
export WORLD_NAME="world1-dotnet"
dotnet run

# Production build
dotnet publish -c Release -o ./publish
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

# Deploy game world server (.NET)
cd mmo-game-server-dotnet/MMOGameServer
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

### C# Game Server Structure (`mmo-game-server-dotnet/MMOGameServer/`)
- `Program.cs` - ASP.NET Core startup and service configuration
- `Middleware/WebSocketMiddleware.cs` - WebSocket connection handling and message routing
- `Services/` - Core game services using dependency injection:
  - `GameWorldService.cs` - Client connection management and broadcasting
  - `DatabaseService.cs` - PostgreSQL operations for auth and game data
  - `TerrainService.cs` - Chunk loading, caching, and walkability validation
  - `PathfindingService.cs` - A* pathfinding algorithm implementation
  - `GameLoopService.cs` - Background service for deterministic game tick processing
- `Models/` - Data models:
  - `Player.cs` - Player state, movement, and path management
  - `ConnectedClient.cs` - WebSocket client wrapper with authentication state
- `terrain/` - Terrain chunk JSON data (shared format with Unity)
- `worlds/` - Fly.io deployment configurations for staging/production

## Testing & Debugging

### Unity Testing
- Use Play Mode in Unity Editor for runtime testing
- TerrainManager has context menu options for testing chunk loading
- Check Unity Console for WebSocket connection status

### Backend Testing
**Node.js Backend**:
- No automated tests configured yet
- Use `console.log` for debugging

**C# Game Server**:
- Built-in .NET logging with configurable levels (Debug, Info, Warning, Error)
- Health check endpoints: `/health` and `/api/health`
- Use `dotnet run` for development with hot reload
- Visual Studio/VS Code debugging support with breakpoints
- WebSocket testing available at `ws://localhost:5096/ws` (development)
- Swagger UI available at `http://localhost:5096/swagger` (if enabled)
- Monitor Fly.io logs: `fly logs -a <app-name>`

**C# Development Tools**:
```bash
# Watch mode for automatic rebuild on file changes
dotnet watch run

# Run with specific logging level
dotnet run --configuration Debug
ASPNETCORE_ENVIRONMENT=Development dotnet run

# Database connection testing
dotnet run --urls "http://localhost:8080"
```

### Common Issues
- WebSocket connection failures: Check JWT_SECRET matches across services
- Terrain not loading: Verify chunk JSON files exist in `terrain/` directory
- Double login prevention: Active sessions table manages connection state

**C# Game Server Specific**:
- Missing environment variables: Server throws on startup if AUTH_DATABASE_URL or GAME_DATABASE_URL not set
- Terrain path issues: Service tries multiple fallback paths (working dir, relative paths)
- Database connection: SSL disabled for Fly.io internal connections
- Port conflicts: Default development port is 5096, production uses PORT environment variable
- Authentication timeout: Clients have 5 seconds to send auth message after WebSocket connection

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

## C# Game Server Architecture Details

### Service-Oriented Design
The C# game server uses ASP.NET Core's dependency injection system with singleton services to maintain shared state across WebSocket connections:

- **GameWorldService**: Manages all connected clients and handles broadcasting
- **DatabaseService**: Handles PostgreSQL connections for both auth and game databases
- **TerrainService**: Loads and caches terrain chunks with automatic cleanup
- **PathfindingService**: Implements A* algorithm for server-side movement validation
- **GameLoopService**: Background hosted service running at 500ms intervals

### Key Architectural Decisions

**Deterministic Game Loop**:
- Fixed 500ms tick rate matching the original JavaScript implementation
- Parallel movement calculation followed by sequential state updates
- Dirty flag system to minimize network traffic

**Concurrent Chunk Management**:
- Thread-safe `ConcurrentDictionary` for terrain chunk storage
- Reference counting system for automatic memory management
- Multiple path fallback for terrain directory location

**Robust Session Management**:
- 5-second authentication timeout for new connections
- Automatic cleanup of stale sessions on server startup
- Duplicate login prevention with graceful reconnection handling
- 30-second disconnect timeout with 2-minute idle timeout

**Performance Optimizations**:
- Async/await throughout for non-blocking I/O operations
- Batched database operations where possible
- Efficient JSON serialization using System.Text.Json
- Connection pooling through Npgsql