# Transcript word timestamps

The existing `Video File → Transcribe → Transcript` graph retains its `text` and `segments` fields and adds `words` and `wordTimingMethod`:

```json
{
  "text": "Legalizar e ...",
  "segments": [{ "start": 0, "end": 5, "text": "Legalizar e ..." }],
  "words": [
    { "start": 0.13, "end": 0.95, "text": "Legalizar" },
    { "start": 0.95, "end": 1.05, "text": "e" }
  ],
  "wordTimingMethod": "whisper_cpp_token_timestamps"
}
```

Times are seconds on the original source media clock. The transcriber requests the full whisper.cpp JSON (`-ojf`) to enable token timestamps without splitting the existing sentence segments. Adjacent subword fragments are joined into words, with punctuation preserved and special control tokens excluded. Word intervals come from those token intervals; segment durations are never divided to invent missing word timing. Incomplete token alignment can leave a word without an entry. These are Whisper alignment estimates, not manually verified boundaries or forced-alignment results.

The fields are optional in the typed transcript contract so older segment-only JSON remains valid. The Depois do Slogan recording resolver copies the words inside the selected source interval into each shot without asking the model to generate timestamps. SQLite preserves the entire transcript. On the next requested analysis, a saved segment-only transcript is transcribed again to add word timings; an already upgraded transcript is reused.

Script-only previews before recording continue to use estimated timings.
