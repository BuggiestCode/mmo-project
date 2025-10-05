-- Recreate cleanly
DROP TABLE IF EXISTS players CASCADE;
DROP TABLE IF EXISTS adminwhitelist CASCADE;

CREATE TABLE players (
  id                         SERIAL   PRIMARY KEY,
  user_id                    INTEGER  NOT NULL UNIQUE,
  x                          INTEGER  NOT NULL DEFAULT 0,
  y                          INTEGER  NOT NULL DEFAULT 0,
  facing                     SMALLINT          DEFAULT 0,
  character_creator_complete BOOLEAN  NOT NULL DEFAULT FALSE,
  hair_swatch_col_index      SMALLINT          DEFAULT 0,
  skin_swatch_col_index      SMALLINT          DEFAULT 0,
  under_swatch_col_index     SMALLINT          DEFAULT 0,
  boots_swatch_col_index     SMALLINT          DEFAULT 0,
  hair_style_index           SMALLINT          DEFAULT 0,
  facial_hair_style_index    SMALLINT  NOT NULL DEFAULT -1,
  is_male                    BOOLEAN           DEFAULT TRUE,

  skill_health_cur_level     SMALLINT  NOT NULL DEFAULT 10,
  skill_health_xp            INTEGER   NOT NULL DEFAULT 2203,

  skill_attack_cur_level     SMALLINT  NOT NULL DEFAULT 1,
  skill_attack_xp            INTEGER   NOT NULL DEFAULT 0,

  skill_strength_cur_level   SMALLINT  NOT NULL DEFAULT 1,
  skill_strength_xp          INTEGER   NOT NULL DEFAULT 0,

  skill_defence_cur_level    SMALLINT  NOT NULL DEFAULT 1,
  skill_defence_xp           INTEGER   NOT NULL DEFAULT 0,

  skill_defence_cur_level    SMALLINT  NOT NULL DEFAULT 1,
  skill_defence_xp           INTEGER   NOT NULL DEFAULT 0,

  inventory                  JSONB NOT NULL DEFAULT '[]'::jsonb
);

-- Game server bootstrap will attempt to insert the userID for account with username 'admin', if there is no user by this name it will fail, register the account and restart server (must be done for each env)
CREATE TABLE adminwhitelist (
  id                         SERIAL   PRIMARY KEY,
  user_id                    INTEGER  NOT NULL UNIQUE,
  FOREIGN KEY (user_id) REFERENCES players(user_id) ON DELETE CASCADE
);