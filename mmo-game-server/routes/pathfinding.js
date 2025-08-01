const terrainLoader = require('../terrainLoader');

class PathfindingNode {
    constructor(x, y, g = 0, h = 0, parent = null) {
        this.x = x;
        this.y = y;
        this.g = g; // Cost from start
        this.h = h; // Heuristic cost to end
        this.f = g + h; // Total cost
        this.parent = parent;
    }

    equals(other) {
        return this.x === other.x && this.y === other.y;
    }

    toString() {
        return `${this.x},${this.y}`;
    }
}

class Pathfinding {
    constructor() {
        // 8-directional movement (including diagonals)
        this.directions = [
            { x: 0, y: 1 },   // North
            { x: 1, y: 1 },   // Northeast
            { x: 1, y: 0 },   // East
            { x: 1, y: -1 },  // Southeast
            { x: 0, y: -1 },  // South
            { x: -1, y: -1 }, // Southwest
            { x: -1, y: 0 },  // West
            { x: -1, y: 1 }   // Northwest
        ];
    }

    /**
     * Calculate Manhattan distance heuristic
     * @param {number} x1 
     * @param {number} y1 
     * @param {number} x2 
     * @param {number} y2 
     * @returns {number}
     */
    manhattanDistance(x1, y1, x2, y2) {
        return Math.abs(x1 - x2) + Math.abs(y1 - y2);
    }

    /**
     * Calculate Euclidean distance heuristic (better for diagonal movement)
     * @param {number} x1 
     * @param {number} y1 
     * @param {number} x2 
     * @param {number} y2 
     * @returns {number}
     */
    euclideanDistance(x1, y1, x2, y2) {
        return Math.sqrt(Math.pow(x1 - x2, 2) + Math.pow(y1 - y2, 2));
    }

    /**
     * Check if a world position is walkable
     * @param {number} worldX 
     * @param {number} worldY 
     * @returns {boolean}
     */
    isWalkable(worldX, worldY) {
        return terrainLoader.validateMovement(worldX, worldY);
    }

    /**
     * Get movement cost between two adjacent tiles
     * @param {number} fromX 
     * @param {number} fromY 
     * @param {number} toX 
     * @param {number} toY 
     * @returns {number}
     */
    getMovementCost(fromX, fromY, toX, toY) {
        const dx = Math.abs(toX - fromX);
        const dy = Math.abs(toY - fromY);
        
        // Diagonal movement costs more (√2 ≈ 1.414)
        if (dx === 1 && dy === 1) {
            return 1.414;
        }
        // Straight movement
        return 1.0;
    }

    /**
     * Find path using A* algorithm
     * @param {number} startX 
     * @param {number} startY 
     * @param {number} endX 
     * @param {number} endY 
     * @param {number} maxDistance - Maximum search distance to prevent infinite loops
     * @returns {Array<{x: number, y: number}>|null} Path as array of coordinates, or null if no path
     */
    findPath(startX, startY, endX, endY, maxDistance = 50) {
        // Early validation
        if (!this.isWalkable(startX, startY)) {
            console.warn(`Pathfinding: Start position (${startX}, ${startY}) is not walkable`);
            return null;
        }
        
        if (!this.isWalkable(endX, endY)) {
            console.warn(`Pathfinding: End position (${endX}, ${endY}) is not walkable`);
            return null;
        }

        // If start and end are the same, return empty path
        if (startX === endX && startY === endY) {
            return [];
        }

        const openSet = [];
        const closedSet = new Set();
        const startNode = new PathfindingNode(startX, startY, 0, this.euclideanDistance(startX, startY, endX, endY));
        
        openSet.push(startNode);

        let iterations = 0;
        const maxIterations = 1000; // Prevent infinite loops

        while (openSet.length > 0 && iterations < maxIterations) {
            iterations++;

            // Find node with lowest f cost
            openSet.sort((a, b) => a.f - b.f);
            const currentNode = openSet.shift();
            
            closedSet.add(currentNode.toString());

            // Check if we reached the goal
            if (currentNode.x === endX && currentNode.y === endY) {
                return this.reconstructPath(currentNode);
            }

            // Check all neighbors
            for (const direction of this.directions) {
                const neighborX = currentNode.x + direction.x;
                const neighborY = currentNode.y + direction.y;
                const neighborKey = `${neighborX},${neighborY}`;

                // Skip if already processed
                if (closedSet.has(neighborKey)) {
                    continue;
                }

                // Skip if too far from start (prevents excessive searching)
                if (this.manhattanDistance(startX, startY, neighborX, neighborY) > maxDistance) {
                    continue;
                }

                // Skip if not walkable
                if (!this.isWalkable(neighborX, neighborY)) {
                    continue;
                }

                const movementCost = this.getMovementCost(currentNode.x, currentNode.y, neighborX, neighborY);
                const g = currentNode.g + movementCost;
                const h = this.euclideanDistance(neighborX, neighborY, endX, endY);
                
                // Check if this neighbor is already in open set with a better path
                const existingNode = openSet.find(node => node.x === neighborX && node.y === neighborY);
                if (existingNode && existingNode.g <= g) {
                    continue;
                }

                // Create new node or update existing
                const neighborNode = new PathfindingNode(neighborX, neighborY, g, h, currentNode);
                
                if (existingNode) {
                    // Update existing node with better path
                    const index = openSet.indexOf(existingNode);
                    openSet[index] = neighborNode;
                } else {
                    // Add new node to open set
                    openSet.push(neighborNode);
                }
            }
        }

        console.warn(`Pathfinding: No path found from (${startX}, ${startY}) to (${endX}, ${endY}) after ${iterations} iterations`);
        return null;
    }

    /**
     * Reconstruct path from goal node back to start
     * @param {PathfindingNode} goalNode 
     * @returns {Array<{x: number, y: number}>}
     */
    reconstructPath(goalNode) {
        const path = [];
        let currentNode = goalNode;

        while (currentNode.parent) {
            path.unshift({ x: currentNode.x, y: currentNode.y });
            currentNode = currentNode.parent;
        }

        return path;
    }

    /**
     * Get the next tile in the path towards a destination
     * @param {number} startX 
     * @param {number} startY 
     * @param {number} endX 
     * @param {number} endY 
     * @returns {{x: number, y: number}|null} Next position to move to, or null if no valid path
     */
    getNextMove(startX, startY, endX, endY) {
        const path = this.findPath(startX, startY, endX, endY);
        
        if (!path || path.length === 0) {
            return null;
        }

        // Return the first step in the path
        return path[0];
    }

    /**
     * Validate if a direct move from current position to target is valid
     * @param {number} fromX 
     * @param {number} fromY 
     * @param {number} toX 
     * @param {number} toY 
     * @returns {boolean}
     */
    validateDirectMove(fromX, fromY, toX, toY) {
        // Check if target is walkable
        if (!this.isWalkable(toX, toY)) {
            return false;
        }

        // Check if it's adjacent (within 1 tile including diagonals)
        const dx = Math.abs(toX - fromX);
        const dy = Math.abs(toY - fromY);
        
        if (dx > 1 || dy > 1) {
            return false; // Not adjacent
        }

        if (dx === 0 && dy === 0) {
            return false; // Same position
        }

        return true;
    }

    /**
     * Calculate the full path and return it for debugging/preview
     * @param {number} startX 
     * @param {number} startY 
     * @param {number} endX 
     * @param {number} endY 
     * @returns {Array<{x: number, y: number}>|null}
     */
    getFullPath(startX, startY, endX, endY) {
        return this.findPath(startX, startY, endX, endY);
    }
}

// Export singleton instance
const pathfinding = new Pathfinding();
module.exports = pathfinding;