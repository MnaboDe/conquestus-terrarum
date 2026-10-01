CREATE TABLE matches (
    id uuid PRIMARY KEY,
    state jsonb NOT NULL CHECK (jsonb_typeof(state) = 'object'),
    version bigint NOT NULL CHECK (version >= 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
