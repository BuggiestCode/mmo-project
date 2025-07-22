CREATE TABLE players (
  id SERIAL PRIMARY KEY,
  user_id INTEGER NOT NULL UNIQUE, -- FK to users.id (enforced logically, not yet declared as FK)
  x INTEGER NOT NULL DEFAULT 0,
  y INTEGER NOT NULL DEFAULT 0,
  facing SMALLINT DEFAULT 0         -- optional: 0=N, 1=E, 2=S, 3=W
);