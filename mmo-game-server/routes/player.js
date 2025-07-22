// routes/player.js
const express = require("express");
const router = express.Router();

// The runtime Player class
class Player {
  constructor(user_id, x = 0, y = 0) {
    this.user_id = user_id;
    this.x = x;
    this.y = y;
    this.facing = 0;
    this.dirty = false;
  }

  move(dx, dy) {
    this.x = dx;
    this.y = dy;
    this.dirty = true;
  }

  getSnapshot() {
    return {
      id: this.user_id,
      x: this.x,
      y: this.y,
      facing: this.facing,
    };
  }
}

// Assume you initialize your pool externally and pass it in, we create the server by HTTP request from auth.js to avoid
// our auth server needing access to the persistent game database.
module.exports = (db) => {
  // /players
  router.post("/players", async (req, res) => {
    const { user_id, x = 0, y = 0, facing = 0 } = req.body;

    if (!user_id) return res.status(400).send("Missing user_id");

    try {
      await db.query(
        `INSERT INTO players (user_id, x, y, facing)
         VALUES ($1, $2, $3, $4)
         ON CONFLICT (user_id) DO NOTHING`,
        [user_id, x, y, facing]
      );

      // Optional: create a new Player instance in memory if needed here
      res.status(201).json({ status: "created" });
    } catch (err) {
      console.error(err);
      res.status(500).send("Error creating player");
    }
  });

  return router;
};

module.exports.Player = Player;