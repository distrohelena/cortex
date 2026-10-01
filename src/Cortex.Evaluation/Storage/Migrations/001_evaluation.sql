CREATE SCHEMA IF NOT EXISTS evaluation;

CREATE TABLE IF NOT EXISTS evaluation.revisions (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    sha256 char(64) NOT NULL,
    kind varchar(128) NOT NULL,
    document jsonb NOT NULL,
    document_bytes bytea NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, sha256),
    UNIQUE (workspace_id, project_id, id, sha256)
);

CREATE TABLE IF NOT EXISTS evaluation.artifacts (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    sha256 char(64) NOT NULL,
    kind varchar(128) NOT NULL,
    is_holdout boolean NOT NULL,
    payload bytea,
    redacted_at timestamptz,
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, sha256),
    UNIQUE (workspace_id, project_id, id, sha256),
    CHECK ((payload IS NULL) = (redacted_at IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS evaluation.cases (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    case_key varchar(256) NOT NULL,
    artifact_id uuid NOT NULL,
    artifact_sha256 char(64) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, case_key),
    FOREIGN KEY (workspace_id, project_id, artifact_id)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, artifact_id, artifact_sha256)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id, sha256)
);

CREATE TABLE IF NOT EXISTS evaluation.experiments (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    manifest jsonb NOT NULL,
    created_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    target_revision_id uuid NOT NULL,
    target_revision_sha256 char(64) NOT NULL,
    baseline_revision_id uuid NOT NULL,
    baseline_revision_sha256 char(64) NOT NULL,
    dataset_revision_id uuid NOT NULL,
    dataset_revision_sha256 char(64) NOT NULL,
    evaluator_revision_id uuid NOT NULL,
    evaluator_revision_sha256 char(64) NOT NULL,
    policy_revision_id uuid NOT NULL,
    policy_revision_sha256 char(64) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, target_revision_id, target_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, baseline_revision_id, baseline_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, dataset_revision_id, dataset_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, evaluator_revision_id, evaluator_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, policy_revision_id, policy_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256)
);

CREATE TABLE IF NOT EXISTS evaluation.experiment_candidates (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    revision_id uuid NOT NULL,
    revision_sha256 char(64) NOT NULL,
    ordinal integer NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (workspace_id, project_id, experiment_id, revision_id),
    UNIQUE (workspace_id, project_id, experiment_id, ordinal),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, revision_id, revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256)
);

CREATE TABLE IF NOT EXISTS evaluation.trials (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    state varchar(32) NOT NULL DEFAULT 'pending',
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.attempts (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    trial_id uuid NOT NULL,
    attempt_number integer NOT NULL CHECK (attempt_number > 0),
    state varchar(32) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    UNIQUE (workspace_id, project_id, trial_id, attempt_number),
    FOREIGN KEY (workspace_id, project_id, trial_id)
        REFERENCES evaluation.trials (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.assessments (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    case_id uuid NOT NULL,
    trial_id uuid,
    evaluator_principal varchar(256) NOT NULL,
    evaluator_revision_id uuid NOT NULL,
    evaluator_revision_sha256 char(64) NOT NULL,
    criterion varchar(256) NOT NULL,
    author_kind varchar(64) NOT NULL,
    outcome varchar(16) NOT NULL CHECK (outcome IN ('pass', 'fail', 'unscored')),
    severity integer CHECK (severity IS NULL OR severity BETWEEN 0 AND 5),
    supersedes_assessment_id uuid,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, case_id)
        REFERENCES evaluation.cases (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, trial_id)
        REFERENCES evaluation.trials (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, evaluator_revision_id, evaluator_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    FOREIGN KEY (workspace_id, project_id, supersedes_assessment_id)
        REFERENCES evaluation.assessments (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.assessment_evidence (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    assessment_id uuid NOT NULL,
    artifact_id uuid NOT NULL,
    artifact_sha256 char(64) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, assessment_id, artifact_id),
    FOREIGN KEY (workspace_id, project_id, assessment_id)
        REFERENCES evaluation.assessments (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, artifact_id, artifact_sha256)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id, sha256)
);

CREATE TABLE IF NOT EXISTS evaluation.comparisons (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    left_assessment_id uuid NOT NULL,
    right_assessment_id uuid NOT NULL,
    result jsonb NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, left_assessment_id)
        REFERENCES evaluation.assessments (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, right_assessment_id)
        REFERENCES evaluation.assessments (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.hypotheses (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    body jsonb NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.decisions (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    decision jsonb NOT NULL,
    decided_by varchar(256) NOT NULL,
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, experiment_id)
        REFERENCES evaluation.experiments (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.activation_records (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    decision_id uuid NOT NULL,
    activated_by varchar(256) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, decision_id)
        REFERENCES evaluation.decisions (workspace_id, project_id, id)
);

CREATE TABLE IF NOT EXISTS evaluation.audit_log (
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    id uuid NOT NULL,
    actor varchar(256) NOT NULL,
    action varchar(64) NOT NULL,
    artifact_id uuid,
    reason varchar(1024) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (workspace_id, project_id, id),
    FOREIGN KEY (workspace_id, project_id, artifact_id)
        REFERENCES evaluation.artifacts (workspace_id, project_id, id)
);

CREATE OR REPLACE FUNCTION evaluation.redact_artifact(
    p_workspace_id uuid, p_project_id uuid, p_artifact_id uuid, p_actor varchar, p_reason varchar)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, evaluation
AS $$
BEGIN
    UPDATE evaluation.artifacts
       SET payload = NULL, redacted_at = clock_timestamp()
     WHERE workspace_id = p_workspace_id AND project_id = p_project_id AND id = p_artifact_id
       AND redacted_at IS NULL;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'artifact unavailable for redaction';
    END IF;

    INSERT INTO evaluation.audit_log (workspace_id, project_id, id, actor, action, artifact_id, reason)
    VALUES (p_workspace_id, p_project_id, gen_random_uuid(), p_actor, 'artifact.redact', p_artifact_id, p_reason);
END;
$$;

REVOKE ALL ON FUNCTION evaluation.redact_artifact(uuid, uuid, uuid, varchar, varchar) FROM PUBLIC;

CREATE OR REPLACE FUNCTION evaluation.reject_immutable_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'evaluation record is immutable';
END;
$$;

DROP TRIGGER IF EXISTS revisions_immutable ON evaluation.revisions;
CREATE TRIGGER revisions_immutable BEFORE UPDATE OR DELETE ON evaluation.revisions
FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();

DROP TRIGGER IF EXISTS assessments_immutable ON evaluation.assessments;
CREATE TRIGGER assessments_immutable BEFORE UPDATE OR DELETE ON evaluation.assessments
FOR EACH ROW EXECUTE FUNCTION evaluation.reject_immutable_change();

CREATE OR REPLACE FUNCTION evaluation.validate_assessment_supersession() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE previous evaluation.assessments%ROWTYPE;
BEGIN
    IF NEW.supersedes_assessment_id IS NOT NULL THEN
        SELECT * INTO previous FROM evaluation.assessments
         WHERE workspace_id = NEW.workspace_id AND project_id = NEW.project_id
           AND id = NEW.supersedes_assessment_id;
        IF NOT FOUND OR previous.case_id IS DISTINCT FROM NEW.case_id
           OR previous.trial_id IS DISTINCT FROM NEW.trial_id
           OR previous.criterion IS DISTINCT FROM NEW.criterion THEN
            RAISE EXCEPTION 'assessment supersession target does not match';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS assessment_supersession_valid ON evaluation.assessments;
CREATE TRIGGER assessment_supersession_valid BEFORE INSERT ON evaluation.assessments
FOR EACH ROW EXECUTE FUNCTION evaluation.validate_assessment_supersession();

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT USAGE ON SCHEMA evaluation TO evaluation_app;
        GRANT SELECT, INSERT ON evaluation.revisions, evaluation.artifacts, evaluation.cases,
            evaluation.experiments, evaluation.experiment_candidates, evaluation.trials, evaluation.attempts, evaluation.assessments,
            evaluation.assessment_evidence, evaluation.comparisons, evaluation.hypotheses,
            evaluation.decisions, evaluation.activation_records, evaluation.audit_log TO evaluation_app;
        GRANT EXECUTE ON FUNCTION evaluation.redact_artifact(uuid, uuid, uuid, varchar, varchar) TO evaluation_app;
    END IF;
END $$;
