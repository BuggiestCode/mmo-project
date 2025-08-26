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


***NPC IMPLEMENTATION TASK***
NPCZones:
handle their own lifetime and spawn, this system does not need changing, the code changes to NPC behaviour should take place purely with regards to the actual NPCs not their spawning, lifetime or chunks/zone loading and unloading.

Currently the NPC functionality is:

- Spawn in zone, randomly select valid roam target in zone, idle for tick if selection fails valid x times.

This is a good "Idle" state (true idle).

What I want is to implement an OSRS like combat system. I will be simplifying things to start off with:


I am going to break NPC AI down into a couple of states because they overlap a bit.

- If we enter the tick and the targetUnit is not on an adjacent(cardinal NSEW not diagonal) tile to us, we take a greedy path step towards them (OSRS does greedy pathing for in combat, not A*, allowing for safes-potting etc)
- If we enter the tick and the targetUnit is adjacent and our attack is not on cooldown, do Attack.
- If we are exiting the tick (we might have just attacked this tick or be on cooldown) and the targetUnit has a move queued that takes them away from the adjacent tick, greedy match their movement to try and remain adjacent (diag-movement is always allowed but attacks must happen cardinal)
- If the next move takes us out of our zone, return to Idle.
- NPCs are processed in order so if we (in the future) have npc vs npc combat, the one processed 2nd essentially gets movment priority because they see the move of the lower priority NPC first (implicit order).

plan:
OSRS-like Combat System Implementation Plan
 
Based on your clarifications, here's the updated plan: 
 
1. Create CombatService (Services/CombatService.cs)
 
- Centralized combat logic for NPCs and Players
- Greedy pathfinding: Single-step movement toward target 
- Adjacent checking: Cardinal only (NSEW) for attacks
- Movement prediction: Check target's queued movement for follow logic 
 
2. Update Player and NPC Models
 
- Add to both Player.cs and NPC.cs:
- IsAlive property 
- AttackCooldownRemaining (ticks until can attack) 
- AttackCooldown constant (ticks between attacks)
- Add to NPC.cs only:
- TargetPlayer reference (null when idle)
- AIState enum (Idle, InCombat)
 
3. Refactor GameLoopService (Services/GameLoopService.cs)
 
- Replace ProcessPlayerMovement() with UpdatePlayer(): 
a. Calculate player path/movement first
b. Process any player combat actions 
- Add UpdateNPC() method:
a. Check combat state and target 
b. Attack phase: If adjacent (cardinal), attack if cooldown expired
c. Movement phase: If not adjacent, greedy step toward target
d. Follow phase: Check target's queued move, match if moving away
- Execution order: 
a. All players update (paths calculated) 
b. All NPCs update (can see player intended moves) 
c. Apply movements and visibility updates
 
4. Implement Greedy Pathfinding (in CombatService) 
 
- GetGreedyStep(fromX, fromY, toX, toY): 
- Returns single best tile toward target 
- No full pathfinding, just distance reduction 
- Validates walkability of chosen tile 
 
5. Combat State Management (in NPCService) 
 
- Zone boundary checking: If greedy step exits zone → return to Idle 
- Target acquisition: Detect players within aggro range
- Combat exit: Clear target when player dies/disconnects 
 
6. Add Combat Methods
 
- Player.TakeDamage(amount) - placeholder
- NPC.TakeDamage(amount) - placeholder 
- Attack execution with cooldown setting 
 
7. Maintain Visibility System
 
- Keep TerrainService chunk updates in UpdatePlayer()
- Ensure NPC visibility updates work with new structure
 
This approach processes players first so NPCs can react to their intended movements, matching OSRS mechanics. 