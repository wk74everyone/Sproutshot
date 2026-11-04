-- Persist the equipped profession; existing and new players default to the archer (1).
-- Profession availability is validated by the server-side operation, not this column.
ALTER TABLE public.sproutshot_players
    ADD COLUMN equipped_profession integer NOT NULL DEFAULT 1
    CONSTRAINT sproutshot_players_equipped_profession_positive
    CHECK (equipped_profession > 0);
