// connectedClient.js

const WebSocket = require('ws');

class ConnectedClient {
  constructor(userId, username, ws, player) {
    // Session user jwt id, server session unique
    this.userId = userId;
    // Login + database character username, persistent character unique
    this.username = username;
    // Web socket connection
    this.ws = ws;
    // Transient helper object for game logic player info (position, facing dir)
    this.player = player;

    // DateTime: Gets set if the player disconnects without using explicit quit functionality for soft reconnect
    this.disconnectedAt = null;
  }

  send(msgObj) {
    if(this.isConnected()) {
      this.ws.send(JSON.stringify(msgObj));
    }
  }

  isConnected() 
  {
    return this.ws && this.ws.readyState === WebSocket.OPEN;
  }
}

module.exports = { ConnectedClient };