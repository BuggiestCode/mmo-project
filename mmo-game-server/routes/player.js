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
    
    // Path-based movement system
    this.currentPath = [];        // Array of {x, y} coordinates remaining in path
    this.nextTile = null;         // Current target tile being lerped to
    this.isMoving = false;        // Whether player is currently moving along a path
  }

  /**
   * Set a new movement path for the player
   * @param {Array<{x: number, y: number}>} path - Array of world coordinates to follow
   */
  setPath(path) {
    if (!path || path.length === 0) {
      this.clearPath();
      return;
    }

    this.currentPath = [...path]; // Copy the path
    this.isMoving = true;
    console.log(`Player ${this.user_id} set new path with ${path.length} steps`);
  }

  /**
   * Get the next tile to move to and advance the path
   * @returns {{x: number, y: number}|null} Next position or null if no path
   */
  getNextMove() {
    if (!this.isMoving || this.currentPath.length === 0) {
      return null;
    }

    // Pop the next tile from the path
    this.nextTile = this.currentPath.shift();
    
    // If path is empty after this move, we'll be done moving
    if (this.currentPath.length === 0) {
      this.isMoving = false;
    }

    this.dirty = true;
    console.log(`Player ${this.user_id} next move: (${this.nextTile.x}, ${this.nextTile.y}), ${this.currentPath.length} steps remaining`);
    return this.nextTile;
  }

  /**
   * Update player position (called when lerp completes on client)
   * @param {number} x 
   * @param {number} y 
   */
  updatePosition(x, y) {
    this.x = x;
    this.y = y;
    this.dirty = true;
  }

  /**
   * Clear current path and stop movement
   */
  clearPath() {
    this.currentPath = [];
    this.nextTile = null;
    this.isMoving = false;
    console.log(`Player ${this.user_id} path cleared`);
  }

  /**
   * Get current position that should be used for pathfinding
   * If currently lerping to nextTile, use that as the starting point
   * @returns {{x: number, y: number}}
   */
  getPathfindingStartPosition() {
    // If we have a nextTile, pathfind from there (since we're lerping towards it)
    if (this.nextTile) {
      return { x: this.nextTile.x, y: this.nextTile.y };
    }
    
    // Otherwise use current position
    return { x: this.x, y: this.y };
  }

  /**
   * Check if player has an active path
   * @returns {boolean}
   */
  hasActivePath() {
    return this.isMoving && (this.currentPath.length > 0 || this.nextTile !== null);
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
      isMoving: this.isMoving,
      nextTile: this.nextTile
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