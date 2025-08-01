const fs = require('fs');
const path = require('path');

class TerrainLoader {
    constructor(options = {}) {
        this.chunks = new Map(); // Currently loaded chunks: chunkKey -> chunkData
        this.playerChunks = new Map(); // Track which chunk each player is in: playerId -> chunkKey
        this.chunkRefCounts = new Map(); // Reference count for each chunk: chunkKey -> count
        this.terrainPath = path.join(__dirname, 'terrain');
        
        // Configuration
        this.config = {
            unloadDelay: options.unloadDelay || 30000, // 30 seconds before unloading
            chunkSize: 16, // Match Unity's TerrainManager.ChunkSize
            ...options
        };
        
        // Cleanup timer
        this.cleanupInterval = setInterval(() => this.cleanupUnusedChunks(), this.config.unloadDelay);
        
        console.log(`Synchronous terrain loader initialized - server authoritative blocking chunk loading`);
    }

    /**
     * Convert world position to chunk coordinates (matches Unity's logic)
     * @param {number} worldX - World X coordinate
     * @param {number} worldY - World Y coordinate  
     * @returns {Object} {chunkX, chunkY} - Chunk coordinates
     */
    worldPositionToChunkCoord(worldX, worldY) {
        // Account for the chunk offset (chunks are centered, so adjust by half chunk size)
        // This MUST match Unity's TerrainManager.WorldPositionToChunkCoord exactly
        const adjustedX = worldX + (this.config.chunkSize * 0.5);
        const adjustedY = worldY + (this.config.chunkSize * 0.5);
        
        // Calculate chunk coordinates
        const chunkX = Math.floor(adjustedX / this.config.chunkSize);
        const chunkY = Math.floor(adjustedY / this.config.chunkSize);
        
        return { chunkX, chunkY };
    }

    /**
     * Convert world position to local tile coordinates within a chunk
     * @param {number} worldX - World X coordinate
     * @param {number} worldY - World Y coordinate
     * @returns {Object} {chunkX, chunkY, localX, localY} - Chunk and local coordinates
     */
    worldPositionToTileCoord(worldX, worldY) {
        const { chunkX, chunkY } = this.worldPositionToChunkCoord(worldX, worldY);
        
        // Calculate local coordinates within the chunk (0-15)
        const adjustedX = worldX + (this.config.chunkSize * 0.5);
        const adjustedY = worldY + (this.config.chunkSize * 0.5);
        
        // Handle negative modulo properly - JavaScript % can return negative values
        let localX = Math.floor(adjustedX % this.config.chunkSize);
        let localY = Math.floor(adjustedY % this.config.chunkSize);
        
        // Ensure local coordinates are always positive (0-15)
        if (localX < 0) localX += this.config.chunkSize;
        if (localY < 0) localY += this.config.chunkSize;
        
        return { chunkX, chunkY, localX, localY };
    }

    /**
     * Update what chunk a player is in based on their position
     * BLOCKS until chunk is loaded - maintains server authority
     * @param {string|number} playerId - Player's user_id
     * @param {number} worldX - Player's world X coordinate
     * @param {number} worldY - Player's world Y coordinate
     * @returns {boolean} True if chunk is loaded and ready
     */
    updatePlayerChunk(playerId, worldX, worldY) {
        const { chunkX, chunkY } = this.worldPositionToChunkCoord(worldX, worldY);
        const newChunkKey = `${chunkX},${chunkY}`;
        
        const previousChunkKey = this.playerChunks.get(playerId);
        
        // If player is already in this chunk, nothing to do
        if (previousChunkKey === newChunkKey) {
            return true;
        }
        
        // SYNCHRONOUS LOAD - This will block the game tick until chunk is ready
        if (!this.chunks.has(newChunkKey)) {
            console.log(`Player ${playerId} needs chunk ${newChunkKey} - BLOCKING until loaded`);
            const loadSuccess = this.loadChunkSync(newChunkKey);
            if (!loadSuccess) {
                console.error(`Failed to load chunk ${newChunkKey} for player ${playerId}`);
                return false;
            }
        }
        
        // Update reference counts
        if (previousChunkKey) {
            // Decrease reference for old chunk
            const oldRefCount = this.chunkRefCounts.get(previousChunkKey) || 0;
            this.chunkRefCounts.set(previousChunkKey, Math.max(0, oldRefCount - 1));
        }
        
        // Increase reference for new chunk
        const newRefCount = this.chunkRefCounts.get(newChunkKey) || 0;
        this.chunkRefCounts.set(newChunkKey, newRefCount + 1);
        
        // Update player's current chunk
        this.playerChunks.set(playerId, newChunkKey);
        
        console.log(`Player ${playerId} moved to chunk ${newChunkKey} (world: ${worldX}, ${worldY})`);
        return true;
    }

    /**
     * Remove a player from tracking (when they disconnect)
     * @param {string|number} playerId 
     */
    removePlayer(playerId) {
        const playerChunkKey = this.playerChunks.get(playerId);
        if (playerChunkKey) {
            // Decrease reference count for their chunk
            const refCount = this.chunkRefCounts.get(playerChunkKey) || 0;
            this.chunkRefCounts.set(playerChunkKey, Math.max(0, refCount - 1));
            
            this.playerChunks.delete(playerId);
            console.log(`Removed player ${playerId} from terrain tracking (was in chunk ${playerChunkKey})`);
        }
    }

    /**
     * SYNCHRONOUS chunk loading - blocks until chunk is loaded
     * This ensures server authority - no race conditions
     * @param {string} chunkKey - Format: "x,y"
     * @returns {boolean} True if loaded successfully
     */
    loadChunkSync(chunkKey) {
        if (this.chunks.has(chunkKey)) {
            return true; // Already loaded
        }

        const [chunkX, chunkY] = chunkKey.split(',').map(Number);
        const fileName = `chunk_${chunkX}_${chunkY}.json`;
        const filePath = path.join(this.terrainPath, fileName);

        try {
            if (!fs.existsSync(filePath)) {
                console.warn(`Chunk file not found: ${fileName}`);
                return false;
            }

            // SYNCHRONOUS file read - blocks until complete
            const data = fs.readFileSync(filePath, 'utf8');
            const chunkData = JSON.parse(data);

            if (this.validateChunkData(chunkData)) {
                this.chunks.set(chunkKey, {
                    ...chunkData,
                    loadTime: Date.now()
                });
                console.log(`SYNCHRONOUSLY loaded chunk ${chunkKey}`);
                return true;
            } else {
                console.warn(`Invalid chunk data in: ${fileName}`);
                return false;
            }
        } catch (error) {
            console.error(`Error loading chunk ${chunkKey}:`, error.message);
            return false;
        }
    }

    /**
     * Validate chunk data structure
     */
    validateChunkData(chunkData) {
        return (
            typeof chunkData.chunkX === 'number' &&
            typeof chunkData.chunkY === 'number' &&
            Array.isArray(chunkData.walkability) &&
            chunkData.walkability.length === 256
        );
    }

    /**
     * Get chunk data if it's loaded
     * @param {number} chunkX 
     * @param {number} chunkY 
     * @returns {Object|null}
     */
    getChunk(chunkX, chunkY) {
        const chunkKey = `${chunkX},${chunkY}`;
        return this.chunks.get(chunkKey) || null;
    }

    /**
     * Check if a tile is walkable (only works for loaded chunks)
     * @param {number} chunkX 
     * @param {number} chunkY 
     * @param {number} tileX - Local tile X (0-15)
     * @param {number} tileY - Local tile Y (0-15)
     * @returns {boolean|null} True/false if chunk is loaded, null if not loaded
     */
    isTileWalkable(chunkX, chunkY, tileX, tileY) {
        const chunk = this.getChunk(chunkX, chunkY);
        if (!chunk) {
            return null; // Chunk not loaded
        }
        
        if (tileX < 0 || tileX >= this.config.chunkSize || tileY < 0 || tileY >= this.config.chunkSize) {
            return false; // Invalid coordinates
        }

        const flatIndex = tileY * this.config.chunkSize + tileX;
        return chunk.walkability[flatIndex];
    }

    /**
     * Validate if a player can move to a specific world position
     * SYNCHRONOUSLY loads chunk if needed - USES CORRECTED COORDINATE SYSTEM
     * @param {number} worldX 
     * @param {number} worldY 
     * @returns {boolean} True if the tile is walkable
     */
    validateMovement(worldX, worldY) {
        // Use the corrected coordinate system that matches Unity
        const { chunkX, chunkY, localX, localY } = this.worldPositionToTileCoord(worldX, worldY);
        
        // Ensure chunk is loaded (blocks if needed)
        const chunkKey = `${chunkX},${chunkY}`;
        if (!this.chunks.has(chunkKey)) {
            console.log(`Movement validation requires chunk ${chunkKey} - BLOCKING until loaded`);
            if (!this.loadChunkSync(chunkKey)) {
                return false; // Chunk failed to load
            }
        }
        
        const walkable = this.isTileWalkable(chunkX, chunkY, localX, localY);
        console.log(`Movement validation: world(${worldX}, ${worldY}) -> chunk(${chunkX}, ${chunkY}) tile(${localX}, ${localY}) = ${walkable}`);
        return walkable === true;
    }

    /**
     * Unload a chunk if it's no longer referenced.
     * @param {string} chunkKey - The key for the chunk to unload
     */
    unloadChunk(chunkKey) {
        if (this.chunks.has(chunkKey)) {
            this.chunks.delete(chunkKey);
            console.log(`Chunk ${chunkKey} unloaded.`);
        }
    }

    /**
     * Cleanup unused chunks based on reference counts.
     */
    cleanupUnusedChunks() {
        let unloadedCount = 0;
        const chunksToUnload = [];

        for (const [chunkKey, refCount] of this.chunkRefCounts) {
            if (refCount <= 0 && this.chunks.has(chunkKey)) {
                chunksToUnload.push(chunkKey);
            }
        }

        for (const chunkKey of chunksToUnload) {
            this.chunks.delete(chunkKey);
            this.chunkRefCounts.delete(chunkKey);
            unloadedCount++;
        }

        if (unloadedCount > 0) {
            console.log(`Unloaded ${unloadedCount} unused chunks (${this.chunks.size} remaining)`);
        }
    }

    /**
     * Get statistics about current state
     */
    getStats() {
        return {
            loadedChunks: this.chunks.size,
            trackedPlayers: this.playerChunks.size,
            referencedChunks: Array.from(this.chunkRefCounts.values()).filter(count => count > 0).length,
            syncMode: true
        };
    }

    /**
     * Get which chunk a player is currently in
     * @param {string|number} playerId 
     * @returns {string|null} Chunk key like "0,0" or null if not tracked
     */
    getPlayerChunk(playerId) {
        return this.playerChunks.get(playerId) || null;
    }

    /**
     * Cleanup when shutting down
     */
    destroy() {
        if (this.cleanupInterval) {
            clearInterval(this.cleanupInterval);
        }
        this.chunks.clear();
        this.chunkRefCounts.clear();
        this.playerChunks.clear();
        console.log('Synchronous terrain loader destroyed');
    }
}

// Export singleton instance
const terrainLoader = new TerrainLoader();
module.exports = terrainLoader; 