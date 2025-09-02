## Project Overview

This is a multiplayer online game (MMO) with a distributed architecture consisting of:

**Frontend (Unity WebGL)** WebGL-based game client with real-time terrain rendering and multiplayer support
- Located in `mmo-frontend/MMO_FrontEnd/`
- WebSocket connection to game servers at `wss://mmo-world#-production.fly.dev` or `wss://mmo-world#-staging.fly.dev` where # is the world number
- Real-time terrain chunk loading from JSON data
- Click-to-move player movement with pathfinding
- Overhead chat system and UI panels

**Auth/Backend Server** (`mmo-backend/`) Node.js/Express server handling authentication and serving the Unity build
- Express server on port 8080
- JWT authentication with bcrypt password hashing
- Serves Unity WebGL build from `/public`
- PostgreSQL connection for user management

**Game Server** (`mmo-game-server-dotnet/MMOGameServer/`) WebSocket-based game servers managing player state and world simulation
- ASP.NET Core WebSocket server built with .NET 8
- Dependency injection with singleton services for shared state
- Real-time player movement with A* pathfinding
- Background game loop service for deterministic state updates
- Concurrent chunk-based terrain management system
- JWT authentication middleware with session management

**Database Structure** Dual database setup for authentication/sessions and game state persistence
- **Auth DB** (`mmo_auth`): Users table, active_sessions table
- **Game DB** (`mmo_game`): Players table with position/state data

### Key Data Flow
1. User authenticates via backend → receives JWT token
2. Unity client connects to game server with JWT
3. Game server validates token and creates session
4. Real-time position/chat updates via WebSocket
5. Terrain chunks loaded on-demand from server
6. Terrain chunks kept alive by NPC zones for semi persistence Hot -> Warm -> Cold

### Environment Variables

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

### Backend Structure
- `mmo-backend/routes/auth.js` - Authentication endpoints
- `mmo-backend/server.js` - Main Express server

### C# Game Server Structure (`mmo-game-server-dotnet/MMOGameServer/`)
- `Program.cs` - ASP.NET Core startup and service configuration with DI registration
- `Middleware/WebSocketMiddleware.cs` - Simplified connection lifecycle management
- `Messages/` - Message system architecture:
  - `Contracts/` - Base interfaces and enums (`IGameMessage`, `MessageType`, `IMessageHandler`)
  - `Requests/` - Strongly-typed request messages (`AuthMessage`, `MoveMessage`, etc.)
  - `Responses/` - Typed response DTOs (`AuthResponse`, `ErrorResponse`)
  - `Converters/` - Custom JSON polymorphic deserialization (`GameMessageJsonConverter`)
- `Handlers/` - Message handlers organized by domain:
  - `Authentication/` - `AuthHandler`, `LogoutHandler`
  - `Player/` - `MoveHandler`, `CharacterCreationHandler`, `CharacterAttributesHandler`
  - `Communication/` - `ChatHandler`, `PingHandler`
  - `Session/` - `HeartbeatHandler`
- `Services/` - Core game services using dependency injection:
  - `GameWorldService.cs` - Client connection management and broadcasting
  - `DatabaseService.cs` - PostgreSQL operations for auth and game data
  - `TerrainService.cs` - Chunk loading, caching, and walkability validation
  - `PathfindingService.cs` - A* pathfinding algorithm implementation
  - `GameLoopService.cs` - Background service for deterministic game tick processing
  - `MessageProcessor.cs` - Message deserialization and routing coordination
  - `MessageRouter.cs` - Handler discovery and dispatch service
- `Models/` - Data models:
  - `Player.cs` - Player state, movement, and path management
  - `ConnectedClient.cs` - WebSocket client wrapper with authentication state
- `terrain/` - Terrain chunk JSON data (shared format with Unity)
- `worlds/` - Fly.io deployment configurations for staging/production

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


***Current task: Experience on runtime players which integrates with GameDatabase schema***

Current Schema:
  skill_health_cur_level SMALLINT NOT NULL DEFAULT 10,
  skill_health_xp INTEGER NOT NULL DEFAULT 1822,  -- [XP Required(L) = CEIL((10 * (L - 1)^3)/4)] -> 1822 = level 10

  skill_attack_cur_level SMALLINT NOT NULL DEFAULT 1,
  skill_attack_xp INTEGER NOT NULL DEFAULT 0,

  skill_defence_cur_level SMALLINT NOT NULL DEFAULT 1,
  skill_defence_xp INTEGER NOT NULL DEFAULT 0


- We have a public Skill(SkillType type, int baseLevel) class. When we deserialize a player, we want to fetch the skill_SKILLNAME_xp as well as the cur_level.
base_level is then inferred from the xp and cached on the skill object instance using the formula [XP Required(L) = ΣCEIL((10 * (L - 1)^3)/4)] where L is the target level so for lvl 10 we need 5064xp, hence why we init the player's health at that value.
- Cur level is the level with modifications, buffs and debuffs applied, in the case of health this is the 'curValue' after taking damage so we infer 10maxHP from xp and I was struck by a goblin for 4 damage skill_health_cur_level = 6 (once serialized.)
- Speaking of serialization, we need to add saving the current xp and cur level to this database during our periodic saves as well as on quit.
- This should all be pretty easy to tack on since it's really just a serialization system for the curHP/maxHP but rather than saving maxHP we are saving an xp amount so I can kill 2 birds with one stone on the database.
- I will need accessor methods to modify the cur XP as well, the final Skill should have:
    CurrentValue
    BaseValue
    CurrentXP

    When we mod CurrentXP we do a re-inferrence on BaseValue to check if that levelled us up, BaseValue is just a cache of the level the skill so we don't have to calculate it every time.
    In instances where we DO have to calculate a level from XP or check if XP is over a next level threshold, I have added a .csv to the project which contains the level thresholds starting at 1=0 and going up to 200=990025038.
    The max XP for a skill in the game is 1 billion.
    "C:\Users\JackS\Documents\MMO\mmo-game-server-dotnet\MMOGameServer\levels\xp_thresholds.csv"