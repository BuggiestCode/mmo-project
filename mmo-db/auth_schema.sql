-- Recreate cleanly
DROP TABLE IF EXISTS users CASCADE;
DROP TABLE IF EXISTS active_sessions CASCADE;
DROP TABLE IF EXISTS login_attempts CASCADE;
DROP TABLE IF EXISTS account_lockouts CASCADE;

DROP INDEX IF EXISTS idx_username_time;
DROP INDEX IF EXISTS idx_ip_time;

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

-- Table to track login attempts (both successful and failed)
CREATE TABLE IF NOT EXISTS login_attempts (
  id SERIAL PRIMARY KEY,
  username VARCHAR(255) NOT NULL,
  ip_address VARCHAR(45),
  attempt_time TIMESTAMP NOT NULL DEFAULT NOW(),
  successful BOOLEAN NOT NULL DEFAULT false
);

-- Indexes for efficient querying
CREATE INDEX IF NOT EXISTS idx_username_time
ON login_attempts (username, attempt_time);

CREATE INDEX IF NOT EXISTS idx_ip_time
ON login_attempts (ip_address, attempt_time);

-- Table to track account lockouts
CREATE TABLE IF NOT EXISTS account_lockouts (
  username VARCHAR(255) PRIMARY KEY,
  locked_until TIMESTAMP NOT NULL,
  reason VARCHAR(255)
);