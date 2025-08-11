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

      // Player will be created automatically when they first connect to game server

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

      // Check if user already has an active session in the database
      try {
        const sessionResult = await db.query(
          `SELECT world, connection_state FROM active_sessions WHERE user_id = $1`,
          [user.id]
        );
        
        if (sessionResult.rows.length > 0) {
          const session = sessionResult.rows[0];
          console.log('Session check for user', username, ':', session);
          
          // If user is actively connected (state 0), deny the login
          if (session.connection_state === 0) {
            console.log(`Blocking duplicate login for user ${username} (ID: ${user.id}) - active on ${session.world}`);
            return res.status(409).json({ 
              error: "User already logged in",
              message: `This account is already logged in on ${session.world}. Please log out from the other session first.`
            });
          }
          
          // If in soft disconnect state (1), allow login (reconnect case)
          console.log(`Allowing login for user ${username} - in soft disconnect state`);
        } else {
          console.log(`Allowing login for user ${username} - no active session`);
        }
      } catch (sessionError) {
        console.error('Session check failed:', sessionError);
        // On error, allow login but log the issue
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