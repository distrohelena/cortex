-- Historical attempts retain NULL provenance; never invent a currency or grant.
ALTER TABLE evaluation.monetary_budget_approvals
    ADD CONSTRAINT monetary_approval_currency_unique UNIQUE (workspace_id, project_id, id, currency);
ALTER TABLE evaluation.attempts
    ADD COLUMN monetary_approval_id uuid,
    ADD COLUMN reservation_currency varchar(3),
    ADD CONSTRAINT attempt_monetary_binding_pair CHECK (
        (monetary_approval_id IS NULL AND reservation_currency IS NULL) OR
        (monetary_approval_id IS NOT NULL AND reservation_currency IS NOT NULL)),
    ADD CONSTRAINT attempt_monetary_approval_fk FOREIGN KEY
        (workspace_id, project_id, monetary_approval_id, reservation_currency)
        REFERENCES evaluation.monetary_budget_approvals (workspace_id, project_id, id, currency);
