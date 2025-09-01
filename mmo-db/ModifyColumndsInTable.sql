-- Fix is_male column type from SMALLINT to BOOLEAN

-- Drop the incorrect column
ALTER TABLE players DROP COLUMN IF EXISTS isMale;

-- Add the column with correct name and type
ALTER TABLE players ADD COLUMN is_male BOOLEAN NOT NULL DEFAULT true;

-- If you have existing data where 0 = female and 1 = male, uncomment and run this:
-- UPDATE players SET is_male = CASE WHEN isMale = 1 THEN true ELSE false END;

-- Verify the change
\d players