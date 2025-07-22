const express = require("express");
const router = express.Router();
const jwt = require("jsonwebtoken");

module.exports = (db, JWT_SECRET) => {
  // POST /chat
  router.post("/", async (req, res) => {
    const authHeader = req.headers.authorization;
    if (!authHeader?.startsWith("Bearer "))
      return res.status(401).send("Missing token");

    const token = authHeader.split(" ")[1];
    try {
      const decoded = jwt.verify(token, JWT_SECRET);
      const { chat_contents } = req.body;
      if (!chat_contents) return res.status(400).send("chat_contents is required");

      await db.query(
        `INSERT INTO messages (sender, chat_contents) VALUES ($1, $2)`,
        [decoded.username, chat_contents]
      );

      res.json({ status: "sent" });
    } catch (err) {
      console.error(err);
      res.status(403).send("Invalid token");
    }
  });

  // GET /chat
  router.get("/", async (req, res) => {
    const { rows } = await db.query(
      `SELECT sender, chat_contents, timestamp FROM messages ORDER BY timestamp DESC LIMIT 50`
    );
    res.json(rows.reverse());
  });

  return router;
};