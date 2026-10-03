ALTER TABLE matches
    ADD COLUMN next_update_sequence bigint NOT NULL DEFAULT 1;

CREATE TABLE match_updates
(
    match_id uuid NOT NULL
        REFERENCES matches(id)
            ON DELETE CASCADE,

    sequence bigint NOT NULL,

    occurred_at timestamptz NOT NULL,

    type text NOT NULL,

    payload jsonb NOT NULL DEFAULT '{}'::jsonb,

    PRIMARY KEY (match_id, sequence)
);

CREATE INDEX ix_match_updates_match_sequence
    ON match_updates (match_id, sequence);