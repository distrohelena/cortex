# Generator comparison — checkpoint Generator Only

300 valid responses; 301 attempted calls. Offline assistant review with generator hidden, not independent human review. No prompt/model changes. 20 synthetic fixtures; first eight have active boundary (40/model). All full candidates preserved in attempt JSONL and blind-ID reviews.

[
  {
    "model": "gemma",
    "valid": 100,
    "violations": 23,
    "disputed": 7,
    "material": 47,
    "contextWrong": 37,
    "techIncorrect": 3,
    "techPartial": 27,
    "techTotal": 45,
    "voiceWeak": 59,
    "caricature": 24,
    "bureaucratic": 45,
    "mean": 7.175988665999999,
    "p50": 6.840913899999999,
    "p95": 9.937921099999999,
    "cost": 0,
    "fail": 0
  },
  {
    "model": "luna",
    "valid": 100,
    "violations": 4,
    "disputed": 7,
    "material": 7,
    "contextWrong": 3,
    "techIncorrect": 3,
    "techPartial": 6,
    "techTotal": 45,
    "voiceWeak": 28,
    "caricature": 0,
    "bureaucratic": 11,
    "mean": 5.765051144000003,
    "p50": 3.0552222,
    "p95": 22.5120685,
    "cost": 0.04270053,
    "fail": 1
  },
  {
    "model": "terra",
    "valid": 100,
    "violations": 8,
    "disputed": 1,
    "material": 5,
    "contextWrong": 4,
    "techIncorrect": 1,
    "techPartial": 8,
    "techTotal": 45,
    "voiceWeak": 22,
    "caricature": 1,
    "bureaucratic": 4,
    "mean": 5.729202741,
    "p50": 2.9051242000000004,
    "p95": 26.433437,
    "cost": 0.3494493,
    "fail": 0
  }
]

One Luna transport failure on fixture13/repetition5, 15.018s, no usage; unknown cost not imputed. Explicit resume preserved attempt and completed missing observations. No automatic retry.

Gemma has recurrent severe fixture failures; all three remain useful for measuring the cost and residual risk of the frozen enforcement pipeline. Proceed with independently sampled pipeline phase, five valid executions per fixture/model. Not H×10; no sending.

Limits: fixture13 wording is ambiguous; actual captured participation guidance is Inativa from current experimental fixture, identical for all generators. Upstream situation/presence fixed synthetically to compare identical generation input. Technical review is evidence-based code inspection, not execution of generated snippets. Cases disputed remain separate from pass.

Prices verified 2026-09-10: https://developers.openai.com/api/docs/models/gpt-5.6-luna and https://developers.openai.com/api/docs/models/gpt-5.6-terra . Input/cache-read/cache-write/output per million: Luna .2/.02/.25/1.2; Terra 2/.2/2.5/12. Reasoning included in output. Local monetary cost not estimated.

