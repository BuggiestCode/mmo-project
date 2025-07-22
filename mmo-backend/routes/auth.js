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

      const token = signToken({ id: Number(user.id), username });
      return res.json({ token });
    } catch (err) {
      console.error("Login Error:", err);
      return res.status(500).send("Internal error during login.");
    }
  });

  return router;
};