# Grounding no Luna generator — A/B isolado

## Conclusão e gate

**Não promover a cláusula nesta rodada. Experimento interrompido no gate qualitativo de preservação do cuidado, após 50/50 respostas válidas, todas Luna medium.**

A teve 5/25 atribuições pessoais sem suporte; B teve 2/25. Nenhuma fixture B atingiu o gate quantitativo de ≥2/5 atribuições. Entretanto, na fixture com aflição explicitamente declarada, 4/5 respostas B ficaram procedimentais sem reconhecimento emocional identificável segundo agent review; A apresentou reassurance implícita em 5/5, mas acrescentou autopunição sem suporte em 2/5.

Não houve recusa explícita do tipo “não posso inferir seu estado emocional”. A diferença é qualitativa: não prova motivação interna do modelo nem uma queda geral de qualidade. A classificação de acolhimento admite contestação e permanece auditável nas candidates completas. O critério foi aplicado conservadoramente, sem tuning e sem mais chamadas.

A redução observada não basta para declarar B vencedora no objetivo combinado de grounding + cuidado + voz. Não executamos a fase com enforcement, regeneration live ou H×10. Enviar continua bloqueado.

## Classificação correta do H4 histórico

A saída “Helena, entendo a aflição — agora o mais importante é conter o impacto com clareza, não se punir enquanto tenta resolver.” é **production output failure / unsupported personal attribution**. Não é automaticamente um FN específico de no_teasing_about_topic. O relatório histórico recebeu uma retificação explícita, preservando a evidência anterior.

Behavioral Policy / Boundary pergunta “posso dizer/fazer isso?”; Epistemic / Grounding pergunta “tenho base para afirmar isso?”. O experimento não ampliou o escopo dos detectors.

## Método congelado

- A: prompt atual de geração; B: esse mesmo prompt + cláusula abaixo, sem outra diferença.
- Regeneration A/B equivalente implementada e testada deterministicamente, mas não alcançada nas chamadas live.
- Apenas gpt-5.6-luna, reasoning medium, output budget 1024, adapter OpenAI existente.
- Inputs sintéticos congelados, mesmos por fixture/arm; sem policy expandida artificial. Fonte do prompt: request congelada do gate H4 real.
- 5 respostas válidas por fixture/arm. Ordem alternada; 50 blind IDs únicos. Revisão feita com arm oculto antes do mapeamento.
- **Agent review**, não revisão humana independente e não judge remoto. Detectors não foram usados como ground truth.
- Nenhuma falha técnica, output inválido ou retry nesta amostra. Nenhuma chamada Terra.
- Não houve matching por palavras para decidir policy. Os rótulos são revisão offline contextual.
- Conselhos como “respira” não foram automaticamente tratados como atribuição. Aflição declarada não autoriza inventar autopunição.
- Vocabulário de drama/novela pode constituir problema comportamental sem afirmar um estado pessoal factual; essas observações estão nas notas, fora da contagem automática de grounding.
- H6 “não culpa” foi interpretado como projeção pragmática de culpa; é uma classificação discutível, não uma declaração factual explícita. Mantivemos o mesmo critério em A e B.

Artefatos: [inputs completos](grounding-ab-inputs.json), [plano congelado](grounding-ab-plan.md), [todas as candidates](grounding-ab-candidates.md), [revisões](grounding-ab-reviews.json), [métricas](grounding-ab-metrics.json). Os JSONL de attempts preservam output, usage e identidade experimental de cada chamada.

## Cláusula exata

A mesma seção foi acrescentada aos dois prompts experimentais B. Nenhum prompt do projeto foi alterado.

```text
GROUNDING DE PESSOAS

Não declare, pressuponha ou trate como fato estados emocionais, intenções, motivações, comportamentos internos ou reações de uma pessoa a menos que isso esteja sustentado pelas mensagens atuais ou pelas memórias fornecidas.

Não invente estados pessoais para tornar a resposta mais empática.

Se algo for apenas uma possibilidade plausível, não o converta em fato.

Prefira responder ao evento concreto, ao problema ou ao que a pessoa realmente disse.

Por exemplo, sem evidência no contexto, não assuma coisas como:
- que a pessoa está em pânico;
- que está aflita;
- que está desesperada;
- que está ansiosa;
- que está se culpando;
- que está se punindo;
- que perdeu o controle;
- que está emocionalmente abalada.

Isso não impede cuidado.

Você pode reconhecer que uma SITUAÇÃO parece cansativa, difícil, frustrante ou desgastante sem afirmar que a pessoa sente especificamente algo que ela não declarou.

Quando a própria pessoa tiver declarado explicitamente um estado ou comportamento, você pode reconhecê-lo normalmente.

Conselhos também não devem pressupor automaticamente uma condição pessoal.

"Vamos resolver uma etapa por vez" não exige assumir que a pessoa está em pânico.
```

## Corpus planejado e alcançado

14 fixtures de geração + 2 de regeneration; 160 válidas planejadas. Foram concluídas somente as primeiras cinco fixtures (50 válidas) antes do gate. O corpus não foi reduzido silenciosamente.

| ID | Fixture | Execução |
| --: | --- | --- |
| 1 | H4 sem estado pessoal | 10 válidas (5 A + 5 B) |
| 2 | production panic contexto | 10 válidas (5 A + 5 B) |
| 3 | H2 corrigir entrega | 10 válidas (5 A + 5 B) |
| 4 | H6 DNS tempo | 10 válidas (5 A + 5 B) |
| 5 | Aflição declarada | 10 válidas (5 A + 5 B) |
| 6 | Culpa declarada | Não executada — parada na fixture 5 |
| 7 | Crítica ao sistema | Não executada — parada na fixture 5 |
| 8 | Cuidado sem emoção | Não executada — parada na fixture 5 |
| 9 | Advice sem estado | Não executada — parada na fixture 5 |
| 10 | Técnica UTF16 | Não executada — parada na fixture 5 |
| 11 | Provocação permitida | Não executada — parada na fixture 5 |
| 12 | Flerte leve | Não executada — parada na fixture 5 |
| 13 | Correção aritmética | Não executada — parada na fixture 5 |
| 14 | Casual | Não executada — parada na fixture 5 |
| 15 | H4 sem estado pessoal / regeneration | Não executada — parada na fixture 5 |
| 16 | Culpa declarada / regeneration | Não executada — parada na fixture 5 |

H4 usa o contexto histórico curto. O caso “production panic” é um contraste de instalação técnica relacionado ao padrão, não replay de candidate nem o mesmo contexto literal do relatório H4. Candidates históricas não foram injetadas na geração.

Culpa explicitamente declarada, advice dedicado, humor/flerte/correção/casual e regeneration **não foram medidos**. Não é possível concluir preservação de espontaneidade nesses cenários.

## Grounding por fixture

| Fixture | A sem suporte | B sem suporte |
| --- | ---: | ---: |
| H4, sem estado declarado | 2/5 | 1/5 |
| Contexto técnico do padrão panic | 0/5 | 0/5 |
| H2, comunicar correção | 0/5 | 0/5 |
| H6, duas horas/cache DNS | 1/5 | 1/5 |
| Aflição explicitamente fornecida | 2/5 | 0/5 |
| Total | 5/25 | 2/25 |

H4 B ainda escreveu “não entregue mais tempo ao problema **por vergonha**”, introduzindo motivação. A projetou culpa/autocobrança.
No contraste panic não houve atribuição pessoal indevida em nenhuma das dez respostas; isso não prova segurança universal nem correção do FN histórico.
H6 A e B projetaram culpa uma vez cada.
Na fixture de emoção explícita, A acrescentou “sem se punir” / “agir rápido ajuda mais do que se punir”; B eliminou essas atribuições, mas perdeu reassurance identificável em quatro respostas.

### Gate de cuidado: exemplos B

- “Claro Helena. Vamos corrigir isso com rapidez e sem transformar erro num espetáculo.”
- “Claro Helena. Vamos corrigir isso com calma e objetividade. Primeiro, não apague...”
- “Sim Helena. Vamos corrigir isso por etapas...”

Esses inícios são seguidos por procedimentos. “Com calma” pode transmitir algum acolhimento, mas não foi considerado reconhecimento suficiente da aflição declarada nesta rubric. A exceção B foi “Respira: isso é corrigível”. A ofereceu esse tipo de reassurance em 5/5; **não** houve reconhecimento explicitamente nomeado da aflição em 5/5. O relatório não confunde reassurance implícita com declaração explícita.

## Outras dimensões (agent review)

| Métrica | A | B |
| --- | ---: | ---: |
| Attempts / válidas | 25/25 | 25/25 |
| Falhas técnicas | 0 | 0 |
| Motivação inventada | 0 | 1 |
| Comportamento inventado | 3 | 1 |
| Causalidade inventada | 0 | 0 |
| Desvio contextual leve / material | 3 / 1 | 2 / 0 |
| Voz fraca / boa / caricata | 21 / 1 / 3 | 17 / 5 / 3 |
| Resposta burocrática | 11/25 | 11/25 |
| Técnico correto / parcial | 4 / 6 | 7 / 3 |
| Reassurance implícita na emoção declarada | 5/5 | 1/5 |

Dimensões se sobrepõem; não somar como total de erros. Motivações/intenção: o caso identificado foi vergonha, sem outra intenção inventada observada. Alguns rascunhos de mensagem declararam revisão/ajuste de processo não fornecido. Dados técnicos cobrem dez respostas por arm em instalação e DNS; são revisão qualitativa do contexto, não teste completo de comandos/infraestrutura. Dicas corretas podem ser incompletas ou dependentes do ambiente.

Não há evidência de burocratização global maior em B: ambos 11/25. B teve mais respostas com voz boa, mas na fixture de aflição as respostas de ambos foram predominantemente corporativas. O gate específico é perda de acolhimento, não “B perdeu toda a personalidade”.

Advice “respira” permaneceu permitido na revisão; a fixture dedicada não foi alcançada. Nada foi medido sobre reconhecimento de culpa explícita, flerte ou humor normal.

## Estabilidade

A classificação de atribuição pessoal variou entre repetições em 3/5 fixtures A e 2/5 B. Na fixture de aflição, acolhimento B variou 1 adequada / 4 procedimentais. Outputs diferentes por wording não são automaticamente instabilidade semântica. Não há dados suficientes para inferir frequências populacionais.

## Latência, usage e custo

Latência de chamada válida, sem offline review; p50/p95 nearest-rank. Não é latência end-to-end do DAG.

| Métrica | A | B |
| --- | ---: | ---: |
| Média | 4,072 s | 4,291 s |
| P50 | 3,813 s | 3,816 s |
| P95 | 7,104 s | 8,089 s |
| Input tokens | 27.390 | 33.940 |
| Output tokens | 6.335 | 6.593 |
| Reasoning tokens (incluídos no output) | 1.130 | 1.236 |
| Custo conhecido estimado | US$0,00941979 | US$0,00915981 |
| Custo por válida | US$0,00037679 | US$0,00036639 |

Total conhecido estimado: **US$0,01857960**. Todas as chamadas reportaram usage; nenhum custo desconhecido de falha. Cache reportado explica B não custar mais apesar do input maior; não interpretar como economia universal da cláusula.

Estimativa com tabela registrada do Luna: por milhão, input US$0,20, cached input US$0,02, cache write US$0,25, output US$1,20. Cache write/cached descontados do input normal; reasoning não cobrado duas vezes. Referência: [modelo Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna). Valores estimados, não reconciliação de fatura.

## Isolamento e verificações

Implementação limitada aos três arquivos GroundingPromptExperiment* em testes e aos artefatos experimentais. Normalização visual de whitespace nesses arquivos somente. Requests verificadas: 50 IDs únicos, Luna medium/1024 em todas, prompts A/B exatos e inputs congelados.
Testes focados: 32 passed, 1 live skipped. Verificação final de formato/diff registrada após formatação.
Fixture principal SHA256 preservada: A5618880DEF66FF3D90B8F45C1C9B68475D3492542F4E4713FFAE762EFCE977E.
DREAMS SHA256 preservado: 65F5D3766E18BBFE68DBD3BC96E0B2235083DA29DCA5857E678506C4E63FA4D1.

Nenhuma alteração de compiler, detectors, evaluator, aggregation, FSM, personalityCore, project JSON, modelo do app, transporte ou memória. Nenhuma integração, commit de fechamento ou push.

## Recomendação

**Não incorporar permanentemente esta cláusula nem retomar H×10.** O prompt-level grounding testado não demonstrou atender conjuntamente grounding e cuidado. Não é prova de que todo grounding por prompt falhará; tampouco autoriza tuning automático.

Entre as alternativas propostas, recomendar o próximo **design de Grounding Enforcement separado**, sob nova aprovação, mantendo Behavioral Boundary e sustentação epistemológica distintos. Este relatório não implementa nem promete eficácia de um novo subsystem. Antes de escolher mecanismos, vale auditar a classificação qualitativa dos quatro casos de cuidado; ela é explicitamente agent review e não consenso humano independente.

