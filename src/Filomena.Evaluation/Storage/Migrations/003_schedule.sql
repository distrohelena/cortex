ALTER TABLE evaluation.experiments ADD COLUMN plan_sha256 char(64);
ALTER TABLE evaluation.trials
    ADD COLUMN schedule_ordinal integer CHECK (schedule_ordinal >= 0),
    ADD COLUMN trial_key char(64),
    ADD COLUMN candidate_revision_id uuid,
    ADD COLUMN candidate_revision_sha256 char(64),
    ADD COLUMN case_revision_id uuid,
    ADD COLUMN case_revision_sha256 char(64),
    ADD COLUMN repetition integer CHECK (repetition >= 0),
    ADD CONSTRAINT trials_schedule_ordinal_unique UNIQUE (workspace_id, project_id, experiment_id, schedule_ordinal),
    ADD CONSTRAINT trials_schedule_key_unique UNIQUE (workspace_id, project_id, experiment_id, trial_key),
    ADD CONSTRAINT trials_candidate_revision FOREIGN KEY (workspace_id, project_id, candidate_revision_id, candidate_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    ADD CONSTRAINT trials_case_revision FOREIGN KEY (workspace_id, project_id, case_revision_id, case_revision_sha256)
        REFERENCES evaluation.revisions (workspace_id, project_id, id, sha256),
    ADD CONSTRAINT trials_complete_schedule CHECK (
      (schedule_ordinal IS NULL AND trial_key IS NULL AND candidate_revision_id IS NULL
       AND candidate_revision_sha256 IS NULL AND case_revision_id IS NULL AND case_revision_sha256 IS NULL AND repetition IS NULL)
      OR
      (schedule_ordinal IS NOT NULL AND trial_key IS NOT NULL AND candidate_revision_id IS NOT NULL
       AND candidate_revision_sha256 IS NOT NULL AND case_revision_id IS NOT NULL AND case_revision_sha256 IS NOT NULL AND repetition IS NOT NULL));

-- Pre-existing unplanned experiments remain unclaimable. No historical schedule is inferred.
