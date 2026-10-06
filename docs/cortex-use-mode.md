# Cortex Use graph mode

Launch with `tools/start-cortex-editor.ps1 -Graph <graph.json> -Use`, or `CortexEditorLauncher.Open(..., useMode: true)`.

Use mode exposes a graph as a step-by-step operation. The graph canvas is read-only; the operator can inspect nodes/outputs, fill the declared parameters and choose permitted models. Parameters are fixed before the first step and applied to a compiled execution copy. Every Continue advances one node through the existing kernel; previous outputs are retained and no earlier node is replayed. Review precedes the next action. Reset discards in-memory progress, never generated artifacts.

Graph v3 can include creator-supplied `usage` metadata:

```json
{
  "usage": {
    "title": "Recording brief",
    "fields": { "brief": ["title", "baseText", "durationMinutes", "speakers", "format"], "draft-script": ["model"] },
    "models": { "draft-script": ["gpt-5.6-luna", "gpt-6-luna"] },
    "actions": { "brief": "Continue", "draft-script": "Generate script", "recording-plan": "Create recording plan" }
  }
}
```

Only real configuration fields can be exposed. Unlisted fields/nodes, disallowed model choices and parameter changes after starting are rejected. Raw graph replacement/open/save, full-run and settings routes are forbidden in Use mode. Existing session-token/same-origin rules still apply. The original template file remains unchanged.

`TypedFlowExecution` is a local, one-node facade around the existing kernel. Ordinary full runs retain the default pure-node batching behavior. This facade is not a second scheduler or persistence system: progress lasts for the editor host session. Durable humanFeedback nodes still require their existing durable backend.


## Consumer-owned application assets and review

`CortexEditorLauncher.Open(..., useMode: true, applicationRoot: <consumer web root>)` serves the consumer's own `index.html` and assets through the existing local host. Cortex requires an existing index and locked Use mode. The product UI and domain presentation belong to the consumer repository; no consumer branding or video-specific screens are added to Cortex.

`usage.reviewOutputs` can expose output ports of the last completed node for human review. `/api/use/review` applies allowed, type-validated edits only while paused. The existing kernel updates both its published values and downstream input values; the template graph remains unchanged. Published side-effect outputs cannot be amended through this draft-review capability.
