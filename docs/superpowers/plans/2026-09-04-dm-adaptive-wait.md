# DM completeness arbiter and adaptive waiting

Approved design: restore the DM arbiter, separate from group participation.
A greeting is complete; an unfinished thought may warrant waiting, then one
contextual follow-up. Silence is not evidence of distress. Timer expiry causes
re-evaluation, never compulsory sending or WaitLimitReached/Ignore.

Implementation:
1. Extend decision with optional recheck seconds and reply/follow-up purpose.
   Keep group compatibility and the existing canonical identities unchanged.
2. Supply monotonic process observation timing, pending context and permitted
   follow-up flag. New input during arbitration invalidates its decision.
3. Clamp suggested wait to configurable bounds. After bounded timed checks,
   park pending context until new inbound; do not busy-poll indefinitely.
4. Generate a single contextual clarification only when the arbiter explicitly
   selects follow-up after the configured silence threshold. After any response
   attempt, do not schedule another message without new inbound.
5. Keep recent/buffer caps. Graphiti capture remains unchanged. DM-only outbound
   allowlist remains unchanged. No Gateway operations or live messages.
6. Test with fake clocks: complete greeting, delayed continuation, silence-driven
   follow-up, parking/resumption, stale decisions, cancellation, independent chats,
   invalid model output, timing clamps, generation instructions and group regressions.

Initial DM configuration: default wait 15 seconds, minimum 5, maximum 120,
follow-up allowed after 45 seconds of silence, three timer-driven rechecks.
All settings live under PhaseB:Turns for backward-compatible config binding.
Output budgets remain 8192 and timeout 180 seconds, configurable.
