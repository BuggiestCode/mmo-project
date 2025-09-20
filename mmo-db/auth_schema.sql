-- Recreate cleanly
DROP TABLE IF EXISTS users CASCADE;
DROP TABLE  IF EXISTS active_sessions CASCADE;

CREATE TABLE users (
    id            SERIAL PRIMARY KEY,
    username      TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,

    -- Ban/timeout system: NULL = not banned, timestamp = banned until (year 9999 = permanent)
    ban_until                  TIMESTAMPTZ DEFAULT NULL,
    ban_reason                 TEXT DEFAULT NULL
);

CREATE TABLE active_sessions (
  user_id INTEGER PRIMARY KEY,  -- Use user_id as PK, not serial (one session per user)
  world VARCHAR(50) NOT NULL,   -- String for world names like 'world1', 'us-east', etc.
  connection_state SMALLINT NOT NULL DEFAULT 0,  -- 0=connected, 1=soft_disconnect
  connected_at TIMESTAMP DEFAULT NOW(),
  last_heartbeat TIMESTAMP DEFAULT NOW(),
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);