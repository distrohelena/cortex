ALTER TABLE evaluation.trials
    ADD COLUMN result_artifact_id uuid,
    ADD COLUMN result_artifact_sha256 char(64),
    ADD CONSTRAINT trials_result_pair CHECK ((result_artifact_id IS NULL) = (result_artifact_sha256 IS NULL)),
    ADD CONSTRAINT trials_result_artifact FOREIGN KEY (workspace_id,project_id,result_artifact_id,result_artifact_sha256)
        REFERENCES evaluation.artifacts (workspace_id,project_id,id,sha256);
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'evaluation_app') THEN
        GRANT UPDATE (result_artifact_id,result_artifact_sha256) ON evaluation.trials TO evaluation_app;
    END IF;
END $$;
