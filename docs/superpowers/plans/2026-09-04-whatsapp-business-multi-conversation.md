# WhatsApp Business multi-conversation implementation plan

1. Add failing tests for canonical group/DM conversation identities, account-independent group namespaces, and conversation-scoped actors.
2. Add failing passive/outbound capture tests for DM/group metadata and structural DM addressee binding.
3. Add failing plugin integration tests proving capture, recall, exact recall, and `memory_store` stay in the current conversation.
4. Add failing guard tests for independently configured DM and group response policies and silent suppression of unknown chats.
5. Implement the shared conversation resolver and route every memory lifecycle path through it.
6. Generalize capture metadata and guard configuration without changing Graphiti's external `group_id` API.
7. Prepare the offline OpenClaw configuration: admit all chats for observation, enable the receive hook, isolate DM sessions, allow Helena's configured JID to respond, and retain the existing group's trigger policy.
8. Run plugin, guard, typecheck, Python, FalkorDB integration, and diff verification suites. Do not start the gateway or relink WhatsApp.

