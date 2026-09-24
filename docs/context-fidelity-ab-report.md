# Context Fidelity — A/B generator-only

## Resultado

120 attempts, 120 válidas: 60 A / 60 B. Luna medium, budget 1024. A é o prompt DM Inativa atual com grounding B; B acrescenta somente [a cláusula](context-fidelity-clause-v1.txt). Nenhum prompt de produção foi alterado nesta fase.

| Métrica | A | B |
|---|---:|---:|
| Erros materiais | 2 | 0 |
| Multiplicidade/total por ocorrência materiais | 2 | 0 |
| Outros erros materiais (sujeito/negação/tempo/causa) | 0 | 0 |
| Erros leves | 4 | 1 |
| Literalidade/burocratização leve | 0 | 10 |
| Preocupação leve de voz | 0 | 5 |
| Degradação forte nas duas fixtures normais | 0/10 | 0/10 |
| Latência média | 2.96 s | 2.85 s |
| p50 / p95 (nearest-rank) | 2.85 / 4.39 s | 2.62 / 4.34 s |
| Tokens input / output | 79290 / 8567 | 92310 / 8617 |
| Custo estimado USD | 0.012686 | 0.013057 |
| USD/candidate válida | 0.000211 | 0.000218 |

Métricas de tipo podem se sobrepor; não há score agregado. Custo usa a fórmula comparativa existente: USD/M input 0,20; cached 0,02; cache-write 0,25; output 1,20. Estimativa, não fatura/preço oficial verificado. Usage completo em [métricas](context-fidelity-ab-metrics.json) e attempts.

## Multiplicidade principal — cinco repetições

| Repetição | A | B |
|---|---|---|
| 1 | correta | leve: duas ocorrências → duas tentativas; total mantido |
| 2 | material: “Duas horas apagadas duas vezes” | correta |
| 3 | correta | correta |
| 4 | correta | correta |
| 5 | material: “perder duas horas duas vezes” | correta |

As quatro outras fixtures críticas preservaram totais/distribuição/duração não fornecida, sem erro material em A ou B. As 12 fixtures e prompts exatos estão em [inputs](context-fidelity-ab-inputs.json). [Todas as candidates completas](context-fidelity-ab-candidates.md); [revisão por candidate](context-fidelity-ab-reviews.json).

## Método, voz e limitações

Agent review não cega; não é revisão humana independente. Nenhum detector foi usado como ground truth. O juiz é o agente atual; decisões subjetivas de estilo precisam de revisão de Helena, sobretudo dado o histórico da auditoria humana. Advice/reassurance não foi automaticamente classificado como atribuição factual.

B demonstrou ganho específico suficiente para a composição pequena: 2→0 materiais, sem gate de parada. Não demonstra garantia universal nem superioridade estatística. A exposição do exemplo exato da cláusula limita generalização; incluímos três outros totais/distribuição, contagem, negação, sujeito, tempo, causalidade, subconjunto e naturalidade.

B repetiu inventários e “registrado” em algumas fixtures, inclusive uma correção aritmética não solicitada. São custos reais, leves nesta revisão; não confundir ausência de humor em resposta técnica com perda forte de cuidado. Nos dois casos normais não houve degradação forte. B preservou cuidado e humor social, incluindo bossiness. No técnico ambos foram longos; exemplos de SemaphoreSlim foram úteis, sem transformar limite concorrente em requests/segundo. Comentários de rate limiting e deadlock em algumas candidates são simplificações técnicas, não blockers de fidelidade ao input.

Contexto controlado contém o rótulo “DM de homologação”; algumas candidates extrapolam para ambiente/suporte de homologação. Esse cue existe no input e pode influenciar ambas variantes. Não é memória real nem prova de namespace. Histórico/memória/limites vazios são fixtures explícitas, não Graphiti simulado como live.

A revisão anterior de “WhenAll paraleliza tudo” permanece quality issue, não alterada. Não houve Graphiti, enforcement, transport ou escrita de memória nesta fase.

## Próximo gate autorizado

Composição sintética pequena, 16 runs (2×8), com compiler/runtime/enforcement reais e upstream controlado; não é Shadow live. Usa o contexto Engajada correspondente do projeto atual (contém grounding B) + mesma cláusula factual. Regeneration mantém feedback determinístico e mesma regra. Após cada run, revisar antes da seguinte. Nenhuma incorporação nem nova ShadowRun antes desse gate.

