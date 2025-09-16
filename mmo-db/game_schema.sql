-- Recreate cleanly
DROP TABLE IF EXISTS players CASCADE;
DROP TYPE  IF EXISTS item_stack CASCADE;

CREATE TYPE item_stack AS (
  item_id integer,
  qty     integer
);

-- Helper function allowed in CHECK (no table refs, marked IMMUTABLE)
CREATE OR REPLACE FUNCTION all_qty_nonneg(inv item_stack[])
RETURNS boolean
LANGUAGE sql
IMMUTABLE
AS $$
  SELECT COALESCE(BOOL_AND( (e IS NULL) OR (e.qty >= 0) ), TRUE)
  FROM unnest(inv) AS e
$$;

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
  skill_health_xp            INTEGER   NOT NULL DEFAULT 5065,

  skill_attack_cur_level     SMALLINT  NOT NULL DEFAULT 1,
  skill_attack_xp            INTEGER   NOT NULL DEFAULT 0,

  skill_defence_cur_level    SMALLINT  NOT NULL DEFAULT 1,
  skill_defence_xp           INTEGER   NOT NULL DEFAULT 0,

  inventory                  item_stack[] NOT NULL
                             DEFAULT array_fill(NULL::item_stack, ARRAY[30])
);