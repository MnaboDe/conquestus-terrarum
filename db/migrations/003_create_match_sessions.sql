CREATE TABLE match_sessions
(
    match_id uuid PRIMARY KEY REFERENCES matches (id) ON DELETE CASCADE,
    join_code text NOT NULL CONSTRAINT uq_match_sessions_join_code UNIQUE,
    created_at timestamptz NOT NULL,
    CHECK (join_code ~ '^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$')
);

CREATE TABLE match_players
(
    match_id uuid NOT NULL REFERENCES match_sessions (match_id) ON DELETE CASCADE,
    slot smallint NOT NULL CHECK (slot IN (1, 2)),
    player_id uuid NOT NULL,
    token_hash bytea UNIQUE CHECK (octet_length(token_hash) = 32),
    joined_at timestamptz,
    PRIMARY KEY (match_id, slot),
    UNIQUE (match_id, player_id),
    CHECK ((token_hash IS NULL) = (joined_at IS NULL)),
    CHECK (slot <> 1 OR token_hash IS NOT NULL)
);
