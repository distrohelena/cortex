# Unknown sender display name

The delivered window M reply addressed the user as `Pessoa`. The native reader
had manufactured that literal in `remetente.nome`, despite having only a trusted
actor identity and no trusted display name. The canonicalizer repeated the same
fallback when the incoming label was absent/null. Model projection preserved it
as a name, so the model was given false display-name data.

Both fallbacks now use an empty string, which the existing person schema already
accepts. Stable actor identity, namespace, session binding and private-reference
masking are unchanged. Supplied display names remain intact. No name is inferred
from JID, message content or transport display-name metadata. No prompt change,
output string replacement, deployment, model call, memory write or send occurred.

Regression: native poll/mapping through the real model projection failed with
expected empty / actual Pessoa before the fix. Canonicalizer cases for missing
and null names also failed the same way; empty and real-name controls passed.
All five cases pass after correction, including private-ID masking assertions.

Verification: `dotnet test Filomena.slnx --no-restore --verbosity quiet` succeeded:
Core 591 passed / 10 skipped; OpenClaw 113 passed; Llm 386 passed / 37 skipped.
Total 1,090 passed / 47 skipped / zero failures. `git diff --check` passed, with
existing CRLF conversion notices. Tests built the solution. Initial sandbox build
could not write existing generated files; rerun with approved local permissions
succeeded. No live test flags were enabled.

Historical checkpoint content was not rewritten. Existing historical labels and
the actually sent phrase still exist in the prior checkpoint; this patch removes
the fabricated name from new inbound mapping, not from immutable prior evidence.
It does not guarantee what a model will say given that old history. A subsequent
live test must distinguish new input correctness from historical contamination.
