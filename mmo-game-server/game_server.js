const WebSocket = require('ws');
const { Player } = require('./routes/player');
const { ConnectedClient } = require('./routes/connectedClient');
const terrainLoader = require('./terrainLoader');
const pathfinding = require('./routes/pathfinding');

const TICK_RATE = 500;
const PORT = process.env.PORT || 8080;

const connectedClients = new Map();

const jwt = require("jsonwebtoken");
const JWT_SECRET = process.env.JWT_SECRET;
if (!JWT_SECRET) {
  throw new Error("JWT_SECRET is not defined in environment variables");
}

const wss = new WebSocket.Server({ port: PORT }, () => {
  console.log(`Game server running on port ${PORT}`);
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

            console.log(`Reattached user ${user_id} to new socket`);

            // Send spawn logic (client may have reconnected but their browser was closed. 
            // If this was just a blip disconnect the front end will ignore the spawn requests)
            spawnNewPlayer(client);

            return;
          } else {
            // Duplicate login (fully connected already)
            console.warn(`User ${user_id} already has a live connection`);
            // Silently deny the new websocket request
            ws.close();
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