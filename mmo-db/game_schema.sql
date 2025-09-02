CREATE TABLE players (
  id SERIAL PRIMARY KEY,
  user_id INTEGER NOT NULL UNIQUE, -- FK to users.id (enforced logically, not yet declared as FK)
  x INTEGER NOT NULL DEFAULT 0,
  y INTEGER NOT NULL DEFAULT 0,
  facing SMALLINT NOT NULL DEFAULT 0,        -- optional: 0=N, 1=E, 2=S, 3=W
  character_creator_complete BOOLEAN NOT NULL DEFAULT FALSE,
  hair_swatch_col_index SMALLINT NOT NULL DEFAULT 0,
  skin_swatch_col_index SMALLINT NOT NULL DEFAULT 0,
  under_swatch_col_index SMALLINT NOT NULL DEFAULT 0,
  boots_swatch_col_index SMALLINT NOT NULL DEFAULT 0,
  hair_style_index SMALLINT NOT NULL DEFAULT 0,
  facial_hair_style_index SMALLINT NOT NULL DEFAULT -1,
  is_male BOOLEAN NOT NULL DEFAULT true

  skill_health_cur_level SMALLINT NOT NULL DEFAULT 10,
  skill_health_xp INTEGER NOT NULL DEFAULT 5064,  -- Σ[XP Required(L) = CEIL((10 * (L - 1)^3)/4)] -> 1822 = level 10

  skill_attack_cur_level SMALLINT NOT NULL DEFAULT 1,
  skill_attack_xp INTEGER NOT NULL DEFAULT 0,

  skill_defence_cur_level SMALLINT NOT NULL DEFAULT 1,
  skill_defence_xp INTEGER NOT NULL DEFAULT 0
);

-- DROP
ALTER TABLE players DROP COLUMN IF EXISTS skill_attack_max_level;
ALTER TABLE players DROP COLUMN IF EXISTS skill_defence_max_level;
ALTER TABLE players DROP COLUMN IF EXISTS skill_health_max_level;

-- ADD
ALTER TABLE players ADD COLUMN skill_attack_xp INTEGER NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN skill_defence_xp INTEGER NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN skill_health_xp INTEGER NOT NULL DEFAULT 5064;


ALTER TABLE players ADD COLUMN skill_attack_cur_level SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE players ADD COLUMN skill_defence_cur_level SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE players ADD COLUMN skill_health_cur_level SMALLINT NOT NULL DEFAULT 10;

