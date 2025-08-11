const WebSocket = require('ws');
const express = require('express');
const cors = require('cors');
const http = require('http');
const { Pool } = require('pg');
const { Player } = require('./routes/player');
const { ConnectedClient } = require('./routes/connectedClient');
const terrainLoader = require('./terrainLoader');
const pathfinding = require('./routes/pathfinding');

const TICK_RATE = 500;
const PORT = process.env.PORT || 8080;  // Single port for both HTTP and WebSocket

const connectedClients = new Map();

const jwt = require("jsonwebtoken");
const JWT_SECRET = process.env.JWT_SECRET;
if (!JWT_SECRET) {
  throw new Error("JWT_SECRET is not defined in environment variables");
}

const INTER_APP_SECRET = process.env.INTER_APP_SECRET;
if (!INTER_APP_SECRET) {
  console.warn("INTER_APP_SECRET is not defined - duplicate login prevention will be disabled");
}

// Database connection for session management (auth database)
const AUTH_DATABASE_URL = process.env.AUTH_DATABASE_URL;
if (!AUTH_DATABASE_URL) {
  throw new Error("AUTH_DATABASE_URL is not defined in environment variables");
}

const authDb = new Pool({ connectionString: AUTH_DATABASE_URL });

// Database connection for game data (players table)
const DATABASE_URL = process.env.DATABASE_URL;
if (!DATABASE_URL) {
  throw new Error("DATABASE_URL is not defined in environment variables");
}

const gameDb = new Pool({ connectionString: DATABASE_URL });

// Get world name from environment or default
const WORLD_NAME = process.env.WORLD_NAME || 'world1';

// Clean up sessions for this world on startup
async function cleanupStaleSessionsOnStartup() {
  try {
    const result = await authDb.query(
      `DELETE FROM active_sessions WHERE world = $1`,
      [WORLD_NAME]
    );
    console.log(`Cleaned up ${result.rowCount} stale sessions for ${WORLD_NAME} on startup`);
  } catch (err) {
    console.error('Failed to cleanup stale sessions:', err);
  }
}

// Player database functions
async function loadOrCreatePlayer(userId) {
  try {
    // Try to load existing player
    const result = await gameDb.query(
      `SELECT user_id, x, y, facing FROM players WHERE user_id = $1`,
      [userId]
    );
    
    if (result.rows.length > 0) {
      const data = result.rows[0];
      console.log(`Loaded existing player ${userId} at position (${data.x}, ${data.y})`);
      return new Player(data.user_id, data.x, data.y);
    } else {
      // Create new player entry with default spawn position
      const defaultX = 0;
      const defaultY = 0;
      const defaultFacing = 0;
      
      await gameDb.query(
        `INSERT INTO players (user_id, x, y, facing) VALUES ($1, $2, $3, $4)`,
        [userId, defaultX, defaultY, defaultFacing]
      );
      
      console.log(`Created new player ${userId} at spawn position (${defaultX}, ${defaultY})`);
      return new Player(userId, defaultX, defaultY);
    }
  } catch (err) {
    console.error(`Failed to load/create player ${userId}:`, err);
    // Return default player on error
    return new Player(userId, 0, 0);
  }
}

// Session management functions
async function createSession(userId) {
  try {
    await authDb.query(
      `INSERT INTO active_sessions (user_id, world, connection_state) 
       VALUES ($1, $2, 0) 
       ON CONFLICT (user_id) 
       DO UPDATE SET world = $2, connection_state = 0, last_heartbeat = NOW()`,
      [userId, WORLD_NAME]
    );
    console.log(`Session created for user ${userId} on ${WORLD_NAME}`);
  } catch (err) {
    console.error(`Failed to create session for user ${userId}:`, err);
  }
}

async function updateSessionState(userId, state) {
  try {
    await authDb.query(
      `UPDATE active_sessions SET connection_state = $1, last_heartbeat = NOW() WHERE user_id = $2`,
      [state, userId]
    );
    console.log(`Session state updated for user ${userId} to ${state}`);
  } catch (err) {
    console.error(`Failed to update session state for user ${userId}:`, err);
  }
}

async function deleteSession(userId) {
  try {
    await authDb.query(`DELETE FROM active_sessions WHERE user_id = $1`, [userId]);
    console.log(`Session deleted for user ${userId}`);
  } catch (err) {
    console.error(`Failed to delete session for user ${userId}:`, err);
  }
}

// Create Express app for HTTP endpoints
const app = express();
app.use(cors());
app.use(express.json());

// Health check endpoint (no auth required)
app.get('/api/health', (req, res) => {
  res.json({ 
    status: 'ok', 
    service: 'game-server',
    world: WORLD_NAME,
    timestamp: Date.now(),
    connectedClients: connectedClients.size
  });
});

// Create HTTP server and attach both Express and WebSocket
const server = http.createServer(app);

// Create WebSocket server using the same HTTP server
const wss = new WebSocket.Server({ server });

// Start the combined HTTP/WebSocket server
// Don't specify a host to listen on all available interfaces (IPv4 and IPv6)
server.listen(PORT, async (err) => {
  if (err) {
    console.error('Failed to start server:', err);
    process.exit(1);
  } else {
    console.log(`Game server running on port ${PORT} (all interfaces)`);
    console.log(`- WebSocket endpoint: ws://[hostname]:${PORT}`);
    console.log(`- HTTP API endpoint: http://[hostname]:${PORT}/api/*`);
    console.log(`Environment: INTER_APP_SECRET configured: ${!!INTER_APP_SECRET}`);
    
    // Clean up any stale sessions from previous runs
    await cleanupStaleSessionsOnStartup();
  }
});

server.on('error', (err) => {
  console.error('Server error:', err);
});

async function onDisconnect(client) {
  if (!client) return;

  // Save player position to database before disconnecting
  if (client.player) {
    try {
      await gameDb.query(
        `UPDATE players SET x = $1, y = $2, facing = $3 WHERE user_id = $4`,
        [client.player.x, client.player.y, client.player.facing, client.userId]
      );
      console.log(`Saved player ${client.userId} position (${client.player.x}, ${client.player.y})`);
    } catch (err) {
      console.error(`Failed to save player ${client.userId} position:`, err);
    }
  }

  // Remove from terrain tracking
  terrainLoader.removePlayer(client.userId);

  connectedClients.delete(client.userId);

  // Delete session from database
  await deleteSession(client.userId);

  for (const otherClient of connectedClients.values()) {
    otherClient.send({ type: 'quitPlayer', id: client.userId });
  }

  if (client.ws) {
    client.ws.client = null;
  }
}

async function heartbeatClients()
{
  const now = Date.now();
  
  for (const [userId, client] of connectedClients.entries()) {
    // Still connected, we have to check socket OPEN because we could have
    // lost the browser to a hard crash, network interrupt or tab closed
    // by task manager where we skip a .close web socket call.
    if (client.ws && client.ws.readyState === WebSocket.OPEN) {
      // Check if player has been idle too long (2 minutes = 120000ms)
      if (client.lastActivity && (now - client.lastActivity) > 120000) {
        console.log(`Forcing logout for idle user ${userId} (no activity for 2+ minutes)`);
        await onDisconnect(client);
        if (client.ws) {
          client.ws.close();
        }
        continue;
      }
      continue;
    }

    if (now - client.disconnectedAt > 30000) {
      console.log(`Cleaning up user ${userId} (disconnect timeout)`);
      await onDisconnect(client);
    }
  }

  // Keep machine awake if there are active connections
  if (connectedClients.size > 0) {
    // This prevents Fly.io from scaling to zero by creating minimal activity
    process.stdout.write('');
  }
}

function spawnNewPlayer(client)
{
  // Load initial chunk for spawn position (BLOCKS until ready)
  const chunkReady = terrainLoader.updatePlayerChunk(
    client.userId, 
    client.player.x, 
    client.player.y
  );
  
  // If no chunk spawned, catastrophic failure - needs to be handled more gracefully in the future
  if (!chunkReady) {
    console.error(`Failed to load spawn chunk for player ${client.userId}`);
    // Handle spawn failure - maybe default to safe coordinates
    return;
  }

  // Spawn myself on all clients (including my own)
  for (const otherClient of connectedClients.values()) {
    otherClient.send({
      type: 'spawnPlayer',
      player: {
        id: String(client.userId),
        username: client.username,
        xPos: client.player.x,
        yPos: client.player.y
      }
    });
  }

  // Build the initial payload of other user data to send to the player
  const otherClientsObject = Array.from(connectedClients.values())
  .filter(c => c.userId !== client.userId)
  .map(c => ({
    id: c.userId,
    username: c.username,
    xPos: c.player.x,
    yPos: c.player.y
  }));
  
  client.send({ type: 'spawnOtherPlayers', players: otherClientsObject });
}

// --------------------------------------- WebSocket message receiver ---------------------------------------

wss.on('connection', (ws) => {
  ws.on('message', async (msg) => {

    try {
      const data = JSON.parse(msg);

      // Track activity for idle timeout (except for auth messages)
      if (ws.client && data.type !== 'auth') {
        ws.client.lastActivity = Date.now();
      }

      if (!ws.client && data.type !== 'auth') 
      {
          console.warn("Unauthenticated client attempted action:", data.type);
          return;
      }
      else if (data.type === 'auth')
      {
        const decoded = jwt.verify(data.token, JWT_SECRET);
        const user_id = decoded.id;
        const username = decoded.username;

        if (!connectedClients.has(user_id)) {
          // Load or create player from database
          const player = await loadOrCreatePlayer(user_id);
          const client = new ConnectedClient(user_id, username, ws, player);
          client.lastActivity = Date.now(); // Initialize activity tracking
          connectedClients.set(user_id, client);
          ws.client = client;

          console.log(`Client connected in ${user_id}.`);

          // Create session in database
          await createSession(user_id);

          spawnNewPlayer(client);

        } else {
          const client = connectedClients.get(user_id);

           if (!client.isConnected()) {
            // Soft reconnect in time window
            client.ws = ws;
            ws.client = client;
            client.disconnectedAt = null; // Clear disconnect timestamp on reconnect
            client.lastActivity = Date.now(); // Reset activity tracking on reconnect

            console.log(`Reattached user ${user_id} to new socket`);

            // Update session state back to connected
            await updateSessionState(user_id, 0);

            // Send spawn logic (client may have reconnected but their browser was closed. 
            // If this was just a blip disconnect the front end will ignore the spawn requests)
            spawnNewPlayer(client);

            return;
          } else {
            // Duplicate login (fully connected already)
            console.warn(`User ${user_id} already has a live connection`);
            
            // Send error message before closing
            ws.send(JSON.stringify({
              type: 'error',
              code: 'ALREADY_LOGGED_IN',
              message: 'User already has an active session on another connection'
            }));
            
            // Give client time to receive the message before closing
            setTimeout(() => ws.close(), 100);
            return;
          }
        }
      }
      else if (data.type === 'move') 
      {
        const client = ws.client
        if (client) 
        {
          const targetX = data.dx;
          const targetY = data.dy;

          console.log(`${targetX}, ${targetY}`)
          
          if (typeof targetX !== 'number' || typeof targetY !== 'number') {
            console.warn(`Invalid move coordinates from player ${client.userId}`);
            return;
          }
          
          // Get starting position for pathfinding (current position or next tile if lerping)
          const startPos = client.player.getPathfindingStartPosition();
          
          console.log(`Player ${client.userId} requesting move from (${startPos.x}, ${startPos.y}) to (${targetX}, ${targetY})`);
          
          // Calculate path using A*
          const path = pathfinding.getFullPath(startPos.x, startPos.y, targetX, targetY);
          
          if (path && path.length > 0) {
            // Valid path found - set it on the player
            client.player.setPath(path);
            
            // Send confirmation to client with the full path for preview
            client.send({
              type: 'pathSet',
              path: path,
              startPos: startPos
            });
            
            console.log(`Player ${client.userId} path set: ${path.length} steps`);
          } else {
            // No valid path found
            console.log(`No valid path found for player ${client.userId} to (${targetX}, ${targetY})`);
            client.send({
              type: 'pathFailed',
              target: { x: targetX, y: targetY },
              reason: 'No valid path'
            });
          }
        }
      }
      else if (data.type === 'quit') 
      {
        onDisconnect(ws.client);
      }
      else if(data.type === 'chat')
      {
        console.log(data.chat_contents);
        for (const client of connectedClients.values()) {
          // Inform all "other" clients that a message has been received
          if(client.userId != ws.client.userId)
          {
            client.send({
              type: 'chat',
              sender: ws.client.username,
              chat_contents: data.chat_contents,
              timestamp: data.timestamp
            });
          }
        }
      }
    } catch (e) {
      console.error('Invalid message:', e);
    }
  });

// --------------------------------------- WebSocket connection closed / lost ---------------------------------------

  ws.on('close', async () => {
    // If we closed the socket without using the quit function
    const client = ws.client;
    if (!client) return;

    console.log(`Client ${client.userId} lost connection.`);
    client.disconnectedAt = Date.now();

    // Update session state to soft disconnect
    await updateSessionState(client.userId, 1);

    // Don't delete yet - just mark as pending timeout
    client.ws = null;
  })
});

// --------------------------------------- Main game tick ---------------------------------------
setInterval(() => {

  const snapshot = [];

  // Process movement for all players
  for (const client of connectedClients.values()) {
    const player = client.player;
    
    // Check if player has an active path and get next move
    if (player.hasActivePath()) {
      const nextMove = player.getNextMove();
      
      if (nextMove) {
        // Update chunk tracking for the new position
        terrainLoader.updatePlayerChunk(client.userId, nextMove.x, nextMove.y);
        
        // Update player's actual position (this will be the client's target for lerping)
        player.updatePosition(nextMove.x, nextMove.y);
        
        console.log(`Tick: Player ${client.userId} moved to (${nextMove.x}, ${nextMove.y})`);
      }
    }
    
    // Add to snapshot if player state changed
    if (player.dirty) {
      snapshot.push(player.getSnapshot());
      player.dirty = false; // Reset dirty flag after snapshot
    }
  }

  // Send general state updates if needed
  if (snapshot.length > 0) {
    const payload = { type: 'state', players: snapshot };

    for (const client of connectedClients.values()) {
      client.send(payload);
    }
  }
}, TICK_RATE);

// Separate interval for client cleanup (30 seconds)
setInterval(() => {
  heartbeatClients();
}, 30000);

// --------------------------------------- Clean shutdown ---------------------------------------
process.on('SIGTERM', () => {
  console.log('Shutting down game server...');
  terrainLoader.destroy();
  process.exit(0);
});

process.on('SIGINT', () => {
  console.log('Shutting down game server...');
  terrainLoader.destroy();
  process.exit(0);
});