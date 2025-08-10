const WebSocket = require('ws');
const express = require('express');
const cors = require('cors');
const http = require('http');
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

// Create Express app for HTTP endpoints
const app = express();
app.use(cors());
app.use(express.json());

// Middleware to verify inter-service authentication
function verifyInterAppAuth(req, res, next) {
  // If INTER_APP_SECRET is not set, skip authentication (for development/testing)
  if (!INTER_APP_SECRET) {
    return next();
  }
  
  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Unauthorized' });
  }
  
  const token = authHeader.slice(7);
  if (token !== INTER_APP_SECRET) {
    return res.status(401).json({ error: 'Invalid auth token' });
  }
  
  next();
}

// Health check endpoint (no auth required)
app.get('/api/health', (req, res) => {
  res.json({ 
    status: 'ok', 
    service: 'game-server-http-api',
    timestamp: Date.now(),
    connectedClients: connectedClients.size
  });
});

// API endpoint to check if a user has an active session
app.get('/api/check-session/:userId', verifyInterAppAuth, (req, res) => {
  const userId = parseInt(req.params.userId);
  
  if (!connectedClients.has(userId)) {
    return res.json({ connected: false });
  }
  
  const client = connectedClients.get(userId);
  
  // Check if the client has an active WebSocket connection
  if (client.isConnected()) {
    return res.json({ 
      connected: true,
      lastHeartbeat: Date.now()
    });
  }
  
  // Check if they're in soft reconnect window (disconnected < 30s ago)
  if (client.disconnectedAt) {
    const timeSinceDisconnect = Date.now() - client.disconnectedAt;
    if (timeSinceDisconnect < 30000) {
      return res.json({
        connected: false,
        inReconnectWindow: true,
        disconnectedAt: client.disconnectedAt
      });
    }
  }
  
  // Client exists but is not connected and outside reconnect window
  return res.json({ connected: false });
});

// Create HTTP server and attach both Express and WebSocket
const server = http.createServer(app);

// Create WebSocket server using the same HTTP server
const wss = new WebSocket.Server({ server });

// Start the combined HTTP/WebSocket server
// Don't specify a host to listen on all available interfaces (IPv4 and IPv6)
server.listen(PORT, (err) => {
  if (err) {
    console.error('Failed to start server:', err);
    process.exit(1);
  } else {
    console.log(`Game server running on port ${PORT} (all interfaces)`);
    console.log(`- WebSocket endpoint: ws://[hostname]:${PORT}`);
    console.log(`- HTTP API endpoint: http://[hostname]:${PORT}/api/*`);
    console.log(`Environment: INTER_APP_SECRET configured: ${!!INTER_APP_SECRET}`);
  }
});

server.on('error', (err) => {
  console.error('Server error:', err);
});

function onDisconnect(client) {
  if (!client) return;

  // Remove from terrain tracking
  terrainLoader.removePlayer(client.userId);

  connectedClients.delete(client.userId);

  for (const otherClient of connectedClients.values()) {
    otherClient.send({ type: 'quitPlayer', id: client.userId });
  }

  if (client.ws) {
    client.ws.client = null;
  }
}

function heartbeatClients()
{
  const now = Date.now();
  for (const [userId, client] of connectedClients.entries()) {
    // Still connected, we have to check socket OPEN because we could have
    // lost the browser to a hard crash, network interrupt or tab closed
    // by task manager where we skip a .close web socket call.
    if (client.ws && client.ws.readyState === WebSocket.OPEN) continue; 

    if (now - client.disconnectedAt > 30000) {
      console.log(`Cleaning up user ${userId} (disconnect timeout)`);
      onDisconnect(client);
    }
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
          const player = new Player(user_id);
          const client = new ConnectedClient(user_id, username, ws, player);
          connectedClients.set(user_id, client);
          ws.client = client;

          console.log(`Client connected in ${user_id}.`);

          spawnNewPlayer(client);

        } else {
          const client = connectedClients.get(user_id);

           if (!client.isConnected()) {
            // Soft reconnect in time window
            client.ws = ws;
            ws.client = client;
            client.disconnectedAt = null; // Clear disconnect timestamp on reconnect

            console.log(`Reattached user ${user_id} to new socket`);

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

  ws.on('close', () => {
    // If we closed the socket without using the quit function
    const client = ws.client;
    if (!client) return;

    console.log(`Client ${client.userId} lost connection.`);
    client.disconnectedAt = Date.now();

    // Don't delete yet - just mark as pending timeout
    client.ws = null;
  })
});

// --------------------------------------- Main game tick ---------------------------------------
setInterval(() => {
  heartbeatClients();

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