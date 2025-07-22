const WebSocket = require('ws');
const { Player } = require('./routes/player');
const { ConnectedClient } = require('./routes/connectedClient');

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
        if (client) {
          client.player.move(data.dx || 0, data.dy || 0);
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

  for (const client of connectedClients.values()) {
    const player = client.player;
    if (player.dirty) {
      snapshot.push(player.getSnapshot());
    }
  }

  if (snapshot.length === 0) return;

  const payload = { type: 'state', players: snapshot };

  for (const client of connectedClients.values()) {
    client.send(payload);
  }
}, TICK_RATE);
