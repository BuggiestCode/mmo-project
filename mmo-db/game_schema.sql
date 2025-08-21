CREATE TABLE players (
  id SERIAL PRIMARY KEY,
  user_id INTEGER NOT NULL UNIQUE, -- FK to users.id (enforced logically, not yet declared as FK)
  x INTEGER NOT NULL DEFAULT 0,
  y INTEGER NOT NULL DEFAULT 0,
  facing SMALLINT DEFAULT 0,        -- optional: 0=N, 1=E, 2=S, 3=W
  character_creator_complete BOOLEAN NOT NULL DEFAULT FALSE
  
  hair_swatch_col_index SMALLINT DEFAULT 0,
  skin_swatch_col_index SMALLINT DEFAULT 0,
  under_swatch_col_index SMALLINT DEFAULT 0,
  boots_swatch_col_index SMALLINT DEFAULT 0,
  hair_style_index SMALLINT DEFAULT 0,
);
