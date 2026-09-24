# Revisão experimental Context Fidelity v2

A/B e composição pequena autorizaram este artefato experimental, não um rollout de transport.

- Artefato: `manual-shadow-project-v2.json`.
- Pin host: `manual-shadow-context-fidelity-v2`, versão 2.
- SHA256: `4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A`.
- Base v1 preservada: `425FA07CAD30B750BB8BB41573CABA971E96DDFD18DDBEE78B3667E152482301`.
- Diff semântico: acrescentar exatamente `\n\n` + `context-fidelity-clause-v1.txt` (sem newline final) aos prompts `dm/group-generation/regeneration` nos três estados: 12 prompts. Modelos, esforço, personality, participation guidance, grounding B, edges, FSM, Boundary/evaluator/schema e sinks permanecem idênticos.
- Teste inverso remove somente esse sufixo e exige igualdade integral com v1. O parser/pin real também aceita o artefato.
- Export local pelo teste `ContextFidelityProjectTests`, opt-in `FILOMENA_CONTEXT_PROJECT_EXPORT`; nenhuma chamada externa.
- Config local ignorada `config/manual-shadow.local.json`: alterados somente projectFile, projectId, projectVersion e hash esperado. Binding/namespace/credentials/journals intactos. Isso não é configuração pessoal/Gateway.
- Rollback experimental: selecionar os quatro valores v1 novamente; não retomar state de run encerrada.

O projeto histórico/base dos outros experimentos não foi sobrescrito. Nenhum core special-case ou subsystem criado. Rotas de grupo no artefato receberam o mesmo sufixo, mas grupos não foram habilitados nem executados live; o host continua DM exata e SHADOW, sem adapter de transporte.

Composição controlada usou upstream sintético e a fixture histórica (não representa uma live DM). A nova Manual ShadowRun é o gate real de session runtime/Graphiti/contexto, após novo preflight. Qualquer resultado discutível permanece no relatório, não apagado pela incorporação.
