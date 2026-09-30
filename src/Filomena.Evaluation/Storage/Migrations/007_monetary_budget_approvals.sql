-- Explicit operator declaration for an unstarted plan. No currency is inferred
-- for legacy reservations; no existing manifest or monetary amount is rewritten.
CREATE TABLE evaluation.monetary_budget_approvals (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    contract_version integer NOT NULL CHECK (contract_version = 1),
    plan_sha256 char(64) NOT NULL CHECK (plan_sha256 ~ '^[0-9a-f]{64}$'),
    currency varchar(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    maximum_cost numeric NOT NULL CHECK (maximum_cost >= 0 AND maximum_cost <> 'NaN'::numeric),
    approved_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, experiment_id),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id)
);
CREATE TRIGGER monetary_budget_approvals_immutable BEFORE UPDATE OR DELETE ON evaluation.monetary_budget_approvals
    FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT SELECT, INSERT ON evaluation.monetary_budget_approvals TO evaluation_app;
    END IF;
END $$;
