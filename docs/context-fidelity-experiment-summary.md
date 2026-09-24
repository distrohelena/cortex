# Context Fidelity — fechamento experimental

## Resultado

- A/B generator-only: 120/120 válidas (60 A / 60 B), Luna medium. Materiais A=2, B=0; caso principal A=2/5, B=0/5. B teve10 flags leves de literalidade, nenhuma degradação forte nas fixtures normais. Agent review, não humano/independente.
- Composição:16/16 válidas,16 finais aceitos,3 Blocks/regenerations revalidadas,0 ABORT/EnforcementFailure,0 hard blockers finais claros.1 final de multiplicidade discutível,1 exagero social leve. Disclosure real foi bloqueado. Nenhum detector modificado.
- Cláusula incorporada somente na revisão experimental v2 do projeto do harness; baseline preservado.12 prompts generation/regeneration alterados por sufixo idêntico, inversão exige igualdade com v1.
- Nova ShadowRun:20/20,15 WOULD_SEND,2 esperar,3 calar;0 ABORT/regeneration/duplicação/outbound.0 hard blockers finais claros; H18 tem fidelity relacional leve/discutível e H8 tom discutível. Run fechada.
- Graphiti novo preflight PASS; PRIVATE91 recuperado live sem ser fornecido no input.0 Boundaries live:0 detectorcalls na ShadowRun, explicitamente sem coverage live desse caminho. Composição sintética teve30 detectorcalls reais.
- Luna estimativa total: USD0.05482708 (A0.01268635+B0.01305666+composição0.02278446+live0.00629961),184calls (120+49+15). Fórmula comparativa existente, não fatura oficial.40calls Gemma locais da ShadowRun preservadas, custo local não medido.

## Evidência

- [Cláusula exata](context-fidelity-clause-v1.txt)
- [A/B, fixtures, métricas e limitações](context-fidelity-ab-report.md)
- [Todas as120 candidates](context-fidelity-ab-candidates.md)
- [Composição:16runs/candidates/detectors](context-fidelity-composition-report.md)
- [Diff/version experimental](context-fidelity-project-v2.md)
- [20interações live completas, latência/custo/revisão](context-fidelity-live-shadow-report.md)
- [Resultados estruturados live](context-fidelity-live-shadow-results.json)

## Verificação final

- Core:491 passed,10 skipped,0failed.
- Focadas LLM/project/harness/Graphiti/composição:33passed,2live opt-ins skipped,0failed; recompiladas e executadas após fechar harness.
- Teste de projeto: vermelho por ausência de v2, depois export/green e igualdade inversa; parser/pin reais aceitam v2.
- dotnet format verify-no-changes nos4 arquivos novos:exit0.
- Build Filomena.App após fechar processo:0warnings,0errors.
- git diff --check:exit0 (warnings de line endings em arquivos preexistentes; nenhum reparo fora de scope).
- Validação dos artefatos:120revisões A/B,16composição,20live,20IDs/executions únicos,run_closed,0spoken transitions,0outbound; SHA v1 inalterado.
- Tentativa de build durante CLI encontrou DLL em uso (MSB3021); não era falha funcional do experimento. Repetição após /quit passou.

Nenhum commit de fechamento: worktree contém muito trabalho prévio não relacionado. Sem push. Nenhuma alteração de OpenClaw/Gateway/Observation/DREAMS. Nenhum Graphiti write nem novo subsystem. Config ignorada do harness mudou somente4 valores de pin, com rollback v1 documentado.

Recomendação: manter Context Fidelity experimental e revisar humanamente os casos discutíveis. Próximo slice de brain/homologação deve delimitar participação (calar em pergunta direta) e cobertura Boundary live antes de voltar ao transport. Não criar enforcement factual novo por estes resultados; não iniciar Damma/DM Live automaticamente.
