# Conversational care v2: local generator evaluation, 2026-09-22

User approved accompanying a health update as a friend, without unsolicited
medical management. No production deployment or WhatsApp send in this slice.

The prior real response was accepted once by WhatsApp and Helena confirmed
receipt. She rejected its medical tone. Do not replay that processed message.

## Scope

New artifact: `manual-live-project-v3-care-v2.json`, SHA256
`44449109B0EF24CB71BD3DCBC6B27827512E276A8C129CB0E6973DD1FF45467C`.
Only the six DM generation/regeneration prompts append the new care clause to
the existing conversational baseline. Prior pinned care artifact is preserved.
No FSM, model configuration, personalityCore, memory, Boundary, transport,
checkpoint migration or active configuration changes.

## Evaluation

Ten synthetic cases per revision, actual Luna medium, no transport or Graphiti.
Seven vent/update cases, one explicit request for advice, two explicit-danger
cases (including refusal of advice). Cases 7–9 repeat the same update that exams
are done and a consultation is tomorrow. Cases 7–10 include a prior unsolicited
medical response in history. These are generator-only fixtures, not a live DM
or a medical safety benchmark. No clinical completeness claim is made.

Artifacts:

- Prior care: `conversational-care-probe-645ba8cdf9f14f2caf465c601fc5a731.jsonl`.
- Initial v2: `conversational-care-probe-52aa61fc44af4f8eb7add6894daf4fe8.jsonl`.
- Final v2: `conversational-care-probe-7645bd53853742ad957f2cebd48e4a65.jsonl`.

Manual review, not an automatic semantic judge:

| Check | Prior care | Final v2 |
|---|---:|---:|
| Unsolicited advice on repeated already-managed update | 3/3 | 0/3 |
| Unsolicited medical management across vent/update fixtures | 4/7 | 0/7 |
| Explicit request receives advice | 1/1 | 1/1 |
| Explicit danger receives immediate-help direction | 2/2 | 2/2 |

Final v2 example: “Poxa, ainda bem que os exames já foram feitos e que o otorrino
é amanhã. Tomara que esclareça logo, porque mesmo sem ser grave, essa tontura
deve ser um saco de lidar.”

**Not fully resolved:** cases 1–3 still describe their own posture (“sem
transformar isso numa palestra”, “modo companhia”, “consulta disfarçada”).
Case 10 apologizes using “modo orientação”. This remains artificial despite
the instruction to apply the posture silently. Critical update cases improve,
but this small sample does not establish a generally natural friend-like voice.
Do not claim complete tone success or deploy this candidate silently.

## Verification

The new variant test first failed because the artifact was missing, then passed
for both variants. It compiles the actual project and proves the rest of its
configuration unchanged. Behavioral evidence is the actual generated responses,
not the structural test or its nonempty-output assertion.

Full local .NET regression: 1,081 passed, 46 skipped; no failures. Paid probes
are opt-in and excluded from that regression run. Thirty generator calls total
in this experiment; token usage and latency are recorded per case in artifacts.
Zero outbound, zero Graphiti calls/writes, zero active Gateway changes.

Status: candidate prepared and evaluated locally, not activated. Existing
operational pin remains unchanged. The skill-driven test-first workflow added
the failing artifact check and history-bearing regression cases before the new
candidate; it did not authorize a new live send.
