-- Never infer unused budget for attempts created by an older accounting contract.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM evaluation.attempts) THEN
        RAISE EXCEPTION 'existing attempts require explicit accounting reconciliation before upgrade';
    END IF;
END $$;

ALTER TABLE evaluation.experiments
    ADD COLUMN execution_state varchar(24) NOT NULL DEFAULT 'planned'
        CHECK (execution_state IN ('planned', 'running', 'completed', 'cancelled', 'indeterminate')),
    ADD COLUMN cancel_requested boolean NOT NULL DEFAULT false,
    ADD COLUMN reserved_calls bigint NOT NULL DEFAULT 0 CHECK (reserved_calls >= 0),
    ADD COLUMN reserved_input_tokens bigint NOT NULL DEFAULT 0 CHECK (reserved_input_tokens >= 0),
    ADD COLUMN reserved_output_tokens bigint NOT NULL DEFAULT 0 CHECK (reserved_output_tokens >= 0),
    ADD COLUMN reserved_cost numeric NOT NULL DEFAULT 0 CHECK (reserved_cost >= 0);

ALTER TABLE evaluation.trials
    ADD COLUMN lease_owner uuid,
    ADD COLUMN lease_generation bigint NOT NULL DEFAULT 0 CHECK (lease_generation >= 0);

ALTER TABLE evaluation.attempts
    ADD COLUMN lease_owner uuid NOT NULL,
    ADD COLUMN lease_generation bigint NOT NULL,
    ADD COLUMN provider varchar(32) NOT NULL,
    ADD COLUMN request_sha256 char(64) NOT NULL,
    ADD COLUMN request_bytes integer NOT NULL CHECK (request_bytes BETWEEN 0 AND 1048576),
    ADD COLUMN reserved_input_tokens bigint NOT NULL CHECK (reserved_input_tokens >= 0),
    ADD COLUMN reserved_output_tokens bigint NOT NULL CHECK (reserved_output_tokens >= 0),
    ADD COLUMN reserved_cost numeric NOT NULL CHECK (reserved_cost >= 0),
    ADD COLUMN started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD COLUMN outcome jsonb,
    ADD COLUMN finished_at timestamptz;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT UPDATE (execution_state, cancel_requested, reserved_calls, reserved_input_tokens,
            reserved_output_tokens, reserved_cost) ON evaluation.experiments TO evaluation_app;
        GRANT UPDATE (state, lease_owner, lease_generation) ON evaluation.trials TO evaluation_app;
        GRANT UPDATE (state, outcome, finished_at) ON evaluation.attempts TO evaluation_app;
    END IF;
END $$;
