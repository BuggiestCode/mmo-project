const express = require("express");
const router = express.Router();
const bcrypt = require("bcrypt");
const jwt = require("jsonwebtoken");

// Expect these to be passed in by the main server
module.exports = (db, JWT_SECRET, signToken) => {
  router.post("/register", async (req, res) => {
    const { username, password } = req.body;
    if (!username || !password) return res.status(400).send("Missing fields");

    try {
      const hash = await bcrypt.hash(password, 10);
      const userResult = await db.query(
        `INSERT INTO users (username, password_hash) VALUES ($1, $2) RETURNING id`,
        [username, hash]
      );

      const userId = userResult.rows[0].id;

      // Create a matching player via HTTP to game server
      await fetch("https://mmogame-api.fly.dev/players", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Authorization": `Bearer ${process.env.INTER_APP_SECRET}`,
        },
        body: JSON.stringify({ user_id: userId }),
      });

      const token = signToken({ id: userId, username });
      return res.json({ token, status: "ok" });
    } catch (e) {
      if (e.code === "23505") return res.status(409).send("User exists");
      console.error(e);
      return res.status(500).send("Server error");
    }
  });
  
  // Login
  router.post("/login", async (req, res) => {
    try {
      const { username, password } = req.body;
      const { rows } = await db.query(
        `SELECT id, password_hash FROM users WHERE username=$1`,
        [username]
      );
      if (!rows.length) return res.status(401).send("No such user");

      const user = rows[0];
      const match = await bcrypt.compare(password, user.password_hash);
      if (!match) return res.status(401).send("Bad password");

      // Check if user already has an active game session (only if configured)
      const interAppSecret = process.env.INTER_APP_SECRET;
      console.log('Checking for duplicate login - INTER_APP_SECRET configured:', !!interAppSecret);
      
      if (interAppSecret) {
        try {
          const gameServerUrl = process.env.GAME_SERVER_URL || 'http://localhost:8081';
          console.log(`Checking game server at: ${gameServerUrl}/api/check-session/${user.id}`);
          
          const response = await fetch(`${gameServerUrl}/api/check-session/${user.id}`, {
            headers: {
              'Authorization': `Bearer ${interAppSecret}`
            }
          });

        if (response.ok) {
          const sessionData = await response.json();
          console.log('Game server session check response:', sessionData);
          
          // If user is actively connected, deny the login
          if (sessionData.connected) {
            console.log(`Blocking duplicate login for user ${username} (ID: ${user.id})`);
            return res.status(409).json({ 
              error: "User already logged in",
              message: "This account is already logged in from another location. Please log out from the other session first."
            });
          }
          
          console.log(`Allowing login for user ${username} - not currently connected`);
          // If in reconnect window, allow login (soft reconnect case)
          // Otherwise, allow login (user not connected)
        } else {
          // If we can't reach game server, log it but allow login
          console.warn('Could not verify game session status:', response.status);
        }
        } catch (gameServerError) {
          // If game server is down or unreachable, log but don't block login
          console.error('Game server check failed:', gameServerError);
        }
      } else {
        console.log('INTER_APP_SECRET not set - skipping duplicate login check');
      }

      const token = signToken({ id: Number(user.id), username });
      return res.json({ token });
    } catch (err) {
      console.error("Login Error:", err);
      return res.status(500).send("Internal error during login.");
    }
  });

  return router;
};