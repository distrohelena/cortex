-- Explicit evaluator declarations; no automatic association of legacy cases.
CREATE TABLE evaluation.case_input_bindings (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    contract_version integer NOT NULL CHECK (contract_version = 1),
    case_id uuid NOT NULL,
    case_artifact_id uuid NOT NULL,
    case_artifact_sha256 char(64) NOT NULL,
    input_revision_id uuid NOT NULL,
    input_revision_sha256 char(64) NOT NULL,
    provenance_revision_id uuid NOT NULL,
    provenance_revision_sha256 char(64) NOT NULL,
    origin varchar(32) NOT NULL CHECK (origin = 'evaluator_declaration'),
    declared_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, case_id)
        REFERENCES evaluation.cases (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, case_artifact_id, case_artifact_sha256)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, input_revision_id, input_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, provenance_revision_id, provenance_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256)
);
CREATE TRIGGER case_input_bindings_immutable BEFORE UPDATE OR DELETE ON evaluation.case_input_bindings
    FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT SELECT, INSERT ON evaluation.case_input_bindings TO evaluation_app;
    END IF;
END $$;
