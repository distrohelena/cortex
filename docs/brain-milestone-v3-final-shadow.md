# Brain Milestone — mini-shadow final v3

BRAIN MILESTONE PASS — voltar ao transport

## Escopo e evidência

Nova ShadowRun `26bbd70a7ec148d7800f72b3bb9f528e`, encerrada com 12 executions únicas. Projeto `manual-shadow-direct-rule-v3`, versão 3, SHA-256 `47d1d4521bcf013e919ddcb3de5900485dda7f8bacefa7437fedfbdffc9bc55e`, conferido antes/depois. Nenhuma alteração de projeto, prompt, FSM, modelo, Boundary, Graphiti data ou transport nesta rodada.

Fonte: manual_shadow_harness; sessão lógica DM trusted já configurada pelo host. Não houve WhatsApp inbound nem lifecycle legado. Evidência experimental local: [JSONL](../state/manual-shadow/manual-shadow-4f5d666c983a4fa6b73644b614ed51fa.jsonl); SHA-256 `4ae0d1b7608757fa96694bbebe0d1ce00c3104d774d19a0759df6811f65255a5`. Métricas estruturadas: [JSON](brain-milestone-v3-final-shadow-metrics.json). Candidates completas: [auditoria](brain-milestone-v3-final-shadow-audit.md).

Invocação usada:

```powershell
dotnet src/Filomena.App/bin/Debug/net10.0/Filomena.App.dll --manual-shadow config/manual-shadow-direct-rule.local.json
```

Uma primeira tentativa, ainda no sandbox, falhou por permissão ao criar o arquivo de evidência, antes de preflight/modelos/run. A execução autorizada fora do sandbox iniciou uma nova run. Isso não foi falha de Graphiti ou do brain.

## Graphiti LIVE read-only: PASS

Duas provas configuradas, 18 checks cada, todos PASS: reachability; namespace esperado por metadata; participante canônico; schema/identidade/provenance exact e semantic; PRIVATE91 positivo; DRIFT42 e PLEX73 excluídos; social recall; Boundary recall; distinção entre sucesso vazio e falha.

Boundary recall no preflight: success_empty. Social recall: success_with_memory. Durante as seis respostas, seis recalls Boundary retornaram 0 itens e seis recalls Hybrid retornaram 1 item, categoria fato. Compiler/caminho de Boundary reais; autorização por `trusted-empty-boundaries:v1`. **Zero invocações dos detectors live**: não reivindicamos coverage live de enforcement não vazio. A cobertura sintética aprovada anteriormente permanece separada, não foi repetida.

Não induzimos outage no backend ativo. O check live confirmou respostas bem-sucedidas sem fallback de erro; regressões locais cobrem empty versus failure. Nenhuma escrita de memória.

## Corpus e resultados

| # | Cenário | Envolvimento | Ação | Resultado |
|---|---|---|---|---|
| 1 | Chamada casual | direto | responder (regra direta) | WOULD_SEND |
| 2 | Pergunta técnica | direto | responder (regra direta) | WOULD_SEND |
| 3 | Continuação técnica | indireto | responder | WOULD_SEND |
| 4 | Ajuda/cuidado + total | direto | responder (regra direta) | WOULD_SEND |
| 5 | Continuação do cuidado | indireto | esperar | NO_SEND |
| 6 | Migração 1 | nenhum | calar | NO_SEND |
| 7 | C++ sem convite | nenhum | calar | NO_SEND |
| 8 | Banal | nenhum | calar | NO_SEND |
| 9 | Provocação Uno | direto | responder (regra direta) | WOULD_SEND |
| 10 | Migração 2 | nenhum | calar | NO_SEND |
| 11 | Técnico extra | direto | responder (regra direta) | WOULD_SEND |
| 12 | Migração 3 | nenhum | calar | NO_SEND |

- Direct obrigatórias (#1, #2, #4): **3/3 responder**. Incluindo social/técnica extras: **5/5 direto → responder**, zero esperar/calar, **0 chamadas a Decidir Presença** nesse caminho. Trace: direct_involvement_rule.
- Continuidade: **2/2 indireto**; uma responder/continuidade, outra esperar/Atenta enquanto a pessoa lista ajustes. Nenhuma pergunta pendente ignorada.
- Migração/recuo: **0/3 erros claros**. #6 recua Atenta → Inativa; #10/#12 ficam Inativa, sem acompanhar nem responder.
- Interesse sem convite: nenhum → calar e Atenta. Banal: nenhum → calar/Inativa.
- **6 WOULD_SEND**, **6 sem envio** (5 calar, 1 esperar), **0 ABORT**, **0 regenerations**.
- **0 hard blockers** na revisão contextual: zero duplicatas, zero erro material de fidelity/personal grounding observado, zero leakage/outage mascarado, zero outbound.

As continuações reintroduzem no inbound manual trechos realmente vistos nas candidates anteriores. Não injetamos falas de Filomena no histórico como se tivessem sido enviadas. Conversas entre terceiros são trechos relatados pela mesma identidade trusted de Helena, não eventos reais de grupo ou novos participantes forjados.

## Estado/history e limite da prova

12 batches/executions únicos. Todas as seis respostas tiveram SendGuard shadowed/provenance approved e wouldTransitionTo=participation-engaged-v1, sem transitionApplied. O histórico de falas enviadas não é escrito por esse host; isso também foi verificado pelas regressões locais.

Estados iniciais das executions: dez Inativa, duas Atenta. Estado final Inativa. Engajada permanece **hipotética**, portanto esta run não comprova dinâmica após fala recebida. A garantia direta 5× em cada um dos três estados foi comprovada no gate anterior aceito, não repetida nem atribuída a esta run.

## Latência real

Médias em segundos, medidas pelos nodes reais (total inclui batching, runtime e recalls):

| Caminho | n | Ler Situação | Decidir Presença | Geração | Graphiti recalls | Total |
|---|---:|---:|---:|---:|---:|---:|
| Direct | 5 | 6.916 | 0.000 | 3.544 | 0.134 | 12.183 |
| Non-direct (inclui silêncios) | 7 | 8.076 | 6.388 | 0.526 | 0.018 | 16.525 |
| Non-direct com resposta | 1 | 9.512 | 5.463 | 3.682 | 0.125 | 20.296 |

Média geral **14,716 s**, mínimo 9,680 s, máximo 20,296 s. Direct: 12,183 s; non-direct: 16,525 s, diferença observada 26,28%. **Não é estimativa causal pareada**: o mix não-direto inclui seis silêncios e só uma geração. A economia estrutural provada é uma chamada a Decidir Presença por direct; essa etapa custou em média **6,388 s** nos sete caminhos não-diretos. A redução histórica de 41–42% no gate anterior continua evidência separada.

## Modelos e custo

Configuração preservada: Ler Situação/Decidir Presença usam o modelo local atual google/gemma-4-e4b; geração Luna gpt-5.6-luna medium, grounding B + Context Fidelity B. Nenhum modelo foi substituído.

- 19 chamadas locais de participação (12 Situação, 7 Presença).
- **6 chamadas Luna**, todas geração; 0 regeneration, 0 detector, 0 Terra.
- Luna: **10.906 input + 1.053 output = 11.959 tokens**; input cached 6.720, cache-write 4.168, input regular 18.
- Estimativa **US$ 0,0024436 total**, US$ 0,0002036 por interação e US$ 0,0004073 por candidate.
- Base comparativa experimental já usada: por milhão, input regular 0,20; cached 0,02; cache-write 0,25; output 1,20. **Não é fatura/preço oficial verificado**; não inclui custo de hardware local.

## Known limitations — sem tuning

- Opt-out explícito continua edge case V1; não repetido aqui e nenhum explicit_opt_out criado.
- Migração probabilística 1/5 histórica já existente na v2 permanece limitação conhecida. 0/3 atual não prova eliminação da variância.
- Verbosidade e literalidade ocasionais. A voz apareceu em “cosplay de urgência”, “sabotagem arquitetural” e “coroa provisória”; isso é observação do agente, não nova avaliação humana.
- #11: núcleo técnico correto (WhenAll não cria threads), mas a frase mistura o retorno de WhenAll com a expressão await. #2 também simplifica “disparar ... com WhenAll”. Imprecisões técnicas leves registradas, não blockers.
- #1/#11: necessita_cuidado=true em contexto técnico/organização é classificação ampla; sem erro material final observado.
- #4 preserva uma hora total em duas ocorrências. “Pare de recriar” é orientação firme; não contamos automaticamente advice como atribuição pessoal factual material.
- Boundaries live vazias limitam coverage; nenhuma memória foi inventada para preencher isso.

## Verificações e encerramento

- 74 testes locais LLM/roteamento/preflight/harness PASS, 0 failed/skipped.
- 39 testes locais Core/manual-shadow/outbound/handoff PASS, 0 failed/skipped.
- Auditoria da evidência: 12 IDs inbound/execution únicos, project hash preservado, direct sem Presença, seis resultados shadowed com transição só hipotética, preflight verde, run_closed e exit 0.
- `messages_send = 0`; zero transport adapter disponível; enabled impossível neste host Phase 0; zero mensagens WhatsApp; nenhuma execução legada. A evidência do zero-send inclui composição sem dependency de transport e testes, não apenas o contador da CLI.
- /quit encerrou a run; DisposeAsync desarma o host para Disabled. Nada continua capturando/ouvindo.
- Nenhuma alteração de brain/transport/config pessoal/DREAMS; nenhum push/commit nesta rodada.

## Decisão

BRAIN MILESTONE PASS — voltar ao transport

Brain congelado na v3 com as limitações acima. Próximo milestone recomendado: retomar observation/provisioning de transport em rodada própria. **Nenhum trabalho de transport foi iniciado aqui.**
