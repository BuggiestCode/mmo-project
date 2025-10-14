-- Fix is_male column type from SMALLINT to BOOLEAN

-- Drop the incorrect column
ALTER TABLE players DROP COLUMN IF EXISTS isMale;

-- Add the column with correct name and type
ALTER TABLE players ADD COLUMN is_male BOOLEAN NOT NULL DEFAULT true;

-- If you have existing data where 0 = female and 1 = male, uncomment and run this:
-- UPDATE players SET is_male = CASE WHEN isMale = 1 THEN true ELSE false END;

-- Verify the change
\d players

ALTER TABLE players DROP COLUMN IF EXISTS head_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS amulet_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS body_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS legs_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS boots_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS main_hand_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS off_hand_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS ring_slot_equip_id;
ALTER TABLE players DROP COLUMN IF EXISTS cape_slot_equip_id;

-- Add the column with correct name and type
ALTER TABLE players ADD COLUMN head_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN amulet_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN body_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN legs_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN boots_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN main_hand_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN off_hand_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN ring_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
ALTER TABLE players ADD COLUMN cape_slot_equip_id INTEGER   NOT NULL DEFAULT -1;
