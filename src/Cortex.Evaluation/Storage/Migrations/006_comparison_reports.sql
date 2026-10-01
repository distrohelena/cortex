-- Private derived reports, not generic artifacts: direct artifact reads must not
-- bypass the source-evidence checks on comparison.show.
CREATE TABLE evaluation.comparison_reports (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    policy_revision_id uuid NOT NULL,
    policy_revision_sha256 char(64) NOT NULL,
    request_sha256 char(64) NOT NULL,
    report_sha256 char(64) NOT NULL,
    payload bytea NOT NULL CHECK (octet_length(payload) BETWEEN 1 AND 1048576),
    created_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, policy_revision_id, policy_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256)
);
CREATE TABLE evaluation.comparison_report_evidence (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    report_id uuid NOT NULL,
    artifact_id uuid NOT NULL,
    artifact_sha256 char(64) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, report_id, artifact_id),
    FOREIGN KEY (workspace_id, project_id, report_id)
        REFERENCES evaluation.comparison_reports (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, artifact_id, artifact_sha256)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id, sha256)
);
CREATE TRIGGER comparison_reports_immutable BEFORE UPDATE OR DELETE ON evaluation.comparison_reports
    FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();
CREATE TRIGGER comparison_report_evidence_immutable BEFORE UPDATE OR DELETE ON evaluation.comparison_report_evidence
    FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT SELECT, INSERT ON evaluation.comparison_reports, evaluation.comparison_report_evidence TO evaluation_app;
    END IF;
END $$;
