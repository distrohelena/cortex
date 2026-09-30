CREATE TABLE evaluation.attempt_charge_declarations (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    trial_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    declaration jsonb NOT NULL,
    evidence_id uuid NOT NULL,
    evidence_sha256 char(64) NOT NULL,
    declared_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, attempt_id),
    FOREIGN KEY (workspace_id, project_id, experiment_id) REFERENCES evaluation.experiments (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, trial_id) REFERENCES evaluation.trials (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, attempt_id) REFERENCES evaluation.attempts (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, evidence_id, evidence_sha256) REFERENCES evaluation.artifacts (workspace_id, project_id, id, sha256)
);
CREATE TRIGGER attempt_charge_declarations_immutable BEFORE UPDATE OR DELETE ON evaluation.attempt_charge_declarations
    FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT SELECT, INSERT ON evaluation.attempt_charge_declarations TO evaluation_app;
    END IF;
END $$;
