# Slice 3.7 — participação explícita, interesses e reason codes

## Contrato V1

```text
Decidir Presença
  acao: enum<calar, esperar, responder>
  continuar_acompanhando: boolean
  motivo: enum<reason code>
```

O motivo não tem texto livre obrigatório nem maxWords. JSON continua estrito: campos extras, tipo incorreto ou enum desconhecido falham; no máximo um retry corretivo; nenhum output parcial ou transição em falha. `resumo` de Ler Situação continua Texto com maxWords=20, sem relaxamento.

Reason codes e descrições:

| Código | Significado |
|---|---|
| chamada_direta | Chamada ou pedido dirigido à Filomena |
| pergunta_direta | Pergunta explicitamente dirigida à Filomena |
| continuidade | Continuação concreta da participação recente dela |
| interesse_no_assunto | Ideia ou discussão relevante ao perfil |
| oportunidade_social | Abertura concreta para contribuição social |
| pensamento_incompleto | Interlocutor ainda completando o pensamento |
| conversa_em_andamento | Troca fluindo; melhor observar antes de falar |
| conversa_entre_outros | Troca pertence aos outros interlocutores |
| conversa_migrou | Troca deixou de envolver Filomena |
| conflito_sem_convite | Conflito alheio sem solicitação de participação |
| acabou_de_falar | Evitar monopolizar após participação muito recente |
| sem_contribuicao | Sem contribuição natural ou razão específica de atenção |

Os códigos descrevem a causa principal, não são política de execução. Nenhum fio lê `motivo`; testes variam o código mantendo os demais sinais e provam o mesmo estado final. A distinção chamada/pergunta foi mantida para observação, sem prioridade oculta no runtime.

## Envolvimento objetivo

- **direto:** a troca dirige-se claramente à Filomena, estrutural ou semanticamente: mention, reply, pergunta/pedido a ela, nome seguido de solicitação. Uma chamada seguida de pausa para explicar continua direta; a incompletude afeta ritmo.
- **indireto:** continuidade clara com participação recente, resposta à ideia/argumento dela, comentário sobre o que fez/disse, ou falam sobre ela sem chamá-la diretamente.
- **nenhum:** apenas observa a troca atual, mesmo que o assunto lhe interesse.

Estar no grupo, conhecer interlocutores, existir histórico ou gostar do tema não constitui envolvimento. **Interesse e envolvimento são dimensões diferentes.** Essas definições estão no prompt e na descrição do campo; não são correção heurística dos outputs. A classificação do modelo ainda pode errar.

## Configuração administrativa e runtime

`project.interestProfile` é texto estático limitado a 32.000 caracteres, capturado no snapshot administrativo do batch junto de personalityCore e participationGuidance. Não é memória, estado operacional, node novo nem aprendizado.

| Configuração | Responsabilidade |
|---|---|
| personalityCore | Identidade expressiva e estilo |
| interestProfile | Assuntos/situações que podem capturar atenção |
| participationGuidance | Disposição de participação naquele State |

Perfil inicial: tecnologia/programação/sistemas; jogos/gráficos/audiovisual/criação técnica; disputas intelectuais; ideias incomuns/ambiciosas; competência; confiança excessiva divertida; humor/provocação elegante. Afinidade não obriga responder nem torna uma atualização rotineira interessante.

No editor: textarea **Perfil de interesses** junto da configuração do personagem; checkbox **Usar perfil de interesses** e preview explícita no painel de Instrução. Sem node Interesses.

| Instrução principal | Core | Interesses | Guidance |
|---|---|---|---|
| Ler Situação | não | não | não |
| Decidir Presença | não | sim | sim |

Referências desativadas não entram em system nem no payload nomeado. Projetos antigos não recebem referência implicitamente. Mudança administrativa não altera o contexto de um batch já aberto. Valores/configurações inválidos falham na validação; o trace padrão não grava os textos administrativos.

Projeto schema **1**, graph schema **3**, evolução aditiva. Salvar/carregar/importar/exportar preserva core, interesses, guidance, flags, portas e fios. Nenhum UUID, namespace ou esquema de identidade foi alterado.

## Flow e FSM

Fonte dos prompts e desenho: `tests/fixtures/slice37-project.mjs`. Fixture para importação: `tests/fixtures/typed-flow-slice37.json`.

```text
Entrada DM ou Grupo → Acumular → Histórico → Ler Situação → Decidir Presença
                                                                  ↓
                                                            Switch(acao)
```

Memória não está no path principal. Os fios de dados/execução são byte-for-byte equivalentes em conteúdo ao fixture 3.6; somente configuração de prompts/referências/schema mudou.

| State | calar | esperar | responder |
|---|---|---|---|
| Inativa | acompanhar=true → Atenta; false → termina | Atenta | Engajada |
| Atenta | acompanhar=false → Inativa; true → termina | termina Atenta | Engajada |
| Engajada | envolvimento=nenhum → Atenta; demais → termina | mesma regra de envolvimento | Engajada |

RESPONDER gera apenas o sinal **WOULD_RESPOND** no harness, a partir do Switch validado, depois segue o destino explícito. Não existe geração final, transporte nem envio. WAIT não cria timer adicional. Estados continuam isolados por sessão.

Contagens, incluindo DM e Grupo: Inativa 20 nodes / 26 fios de dados / 18 de execução; Atenta 18/26/16; Engajada 22/28/20. Não aumentou a complexidade no canvas. Permanece duplicação dos pipelines e seis fios situação→presença por entrada; não foi criado Join/subgraph para escondê-la.

## Método da avaliação

Duas Instructions, Gemma `google/gemma-4-e4b` já carregada, endpoint local `/v1/chat/completions`, temperatura 0,1, orçamento 4096 por chamada. Nenhum serviço/modelo iniciado/reconfigurado. Sem timeout artificial de teste e sem busy polling; cancellation continua suportado.

Dez cenários sintéticos x três repetições. Mesmos presets e modelo da rodada anterior. Timing usa Stopwatch real, Acumular usa fake time. Não são testes de transporte ou medidas de tempo desde a chegada real de uma mensagem.

Critérios estruturais rígidos; escolhas comportamentais observacionais. Cada relatório inclui situação, ação, acompanhamento, motivo, estado final, path validado, duração e retries. Tokens vêm do `usage` real da resposta HTTP através de handler apenas de teste, sem mudar o client de produção e sem guardar prompts/raw response/raciocínio.

### Primeira passagem, preservada

`typed-flow-slice37-initial-results.json`: 30/30 estruturalmente válidas; 60 chamadas; **zero retries**. C++ e engine passaram a Atenta, mas a atualização técnica banal também levou a Atenta nas três repetições. Não foi considerada calibração suficiente. Pensamento incompleto foi classificado como indireto em 2/3 apesar da chamada pelo nome. Persistiu um resumo com gesto físico inventado ("aceno").

Latência inicial: situação 5,54s; presença 3,98s; total 9,52s. Tokens médios por chamada de presença: 481,3 completion, dos quais 434,2 reasoning (47,1 não-reasoning). Não há contagem de tokens 3.6 comparável registrada.

Uma única revisão de prompt foi feita e a matriz inteira repetida, sem selecionar apenas resultados favoráveis:

- atenção por afinidade exige ideia, argumento, criação, problema ou demonstração concreta; status/manutenção rotineira sem isso não basta;
- nome seguido de pausa continua envolvimento direto;
- situação não deve inventar gestos ou assumir assunto técnico de uma ideia não descrita;
- motivo pede a causa observável mais específica, não um tema incidental.

Não há keyword matching, menção a Node/C++ como exceção no runtime nem regra por cenário. Ambas as matrizes permanecem disponíveis. Para reproduzir a primeira versão, remover essas quatro adições de prompt do gerador; o perfil e graph são os mesmos.

## Reprodução

```powershell
dotnet test tests/Filomena.Core.Tests --no-restore
dotnet test tests/Filomena.Llm.Tests --no-restore
node --test tests/frontend/*.test.mjs packages/flow-graph/tests/*.test.mjs
dotnet build Filomena.slnx --no-restore

$env:FILOMENA_TEST_SEMANTICS='1'
dotnet test tests/Filomena.Llm.Tests --no-build --filter FullyQualifiedName~ParticipationSemanticsLiveTests --logger 'trx;LogFileName=semantics37.trx'
```

O single-call 3.6 permanece como experimento histórico, separado pelo opt-in `FILOMENA_TEST_CALIBRATION`; sua falha maxWords não foi silenciada ou truncada. A matriz principal atual usa somente o contrato 3.7. A falha OpenClaw preexistente `InvalidActorIdentityFailsClosedInsteadOfThrowing` continua fora do escopo.

## Matriz final — três repetições

Cada célula: `ação / acompanhar / motivo → state aplicado`. `+` = true; `−` = false. Todos os 30 outputs foram validados; nenhuma fala foi gerada/enviada.

| Cenário | R1 | R2 | R3 |
|---|---|---|---|
| conversa irrelevante | calar − / conversa_entre_outros → Inativa | calar − / conversa_entre_outros → Inativa | calar − / conversa_entre_outros → Inativa |
| chamada direta | responder + / pergunta_direta → Engajada | responder + / pergunta_direta → Engajada | responder + / pergunta_direta → Engajada |
| interesse sem chamada | esperar + / interesse_no_assunto → Atenta | esperar + / interesse_no_assunto → Atenta | esperar + / interesse_no_assunto → Atenta |
| pensamento incompleto | esperar + / pensamento_incompleto → Atenta | esperar + / conversa_em_andamento → Atenta | responder + / chamada_direta → Engajada |
| continuidade engajada | responder + / continuidade → Engajada | responder + / interesse_no_assunto → Engajada | responder + / continuidade → Engajada |
| conversa migrou | calar − / conversa_entre_outros → Atenta | calar − / conversa_entre_outros → Atenta | calar − / conversa_entre_outros → Atenta |
| conflito sem convite | calar − / conflito_sem_convite → Inativa | calar − / conversa_entre_outros → Inativa | calar − / conflito_sem_convite → Inativa |
| interesse legítimo | responder + / interesse_no_assunto → Engajada | responder + / interesse_no_assunto → Engajada | responder + / interesse_no_assunto → Engajada |
| conversa banal | calar − / conversa_entre_outros → Inativa | calar − / conversa_entre_outros → Inativa | calar − / conversa_entre_outros → Inativa |
| atualização técnica banal | calar − / conversa_entre_outros → Inativa | calar − / conversa_entre_outros → Inativa | calar − / conversa_em_andamento → Inativa |


Resultados completos: `typed-flow-slice37-results.json`; rodada inicial preservada em `typed-flow-slice37-initial-results.json`. TRX locais em `%TEMP%\filomena-semantics37-results\semantics37.trx` e `semantics37-refined.trx`.

### Consistência e limitações comportamentais

- Ação + acompanhar + estado foram idênticos nas três repetições de **9/10 cenários**.
- Irrelevante, banal, conflito sem convite e atualização técnica banal ficaram Inativa em 3/3.
- Chamada direta e continuidade Engajada responderam → Engajada em 3/3.
- C++: esperar + acompanhar → Atenta em 3/3. Difere do exemplo calar+acompanhar, mas cumpre o resultado de atenção sem interrupção imediata; WAIT nesta V1 não agenda nada.
- Engine: responder → Engajada em 3/3, resultado permitido, embora mais participativo que a primeira passagem. Não tratamos afinidade como garantia de resposta: C++ e atualização banal demonstram caminhos diferentes.
- Conversa migrou: envolvimento nenhum, calar → Atenta em 3/3.
- **Pensamento incompleto: esperar → Atenta em 2/3; responder → Engajada em 1/3.** É uma falha qualitativa real ainda aberta. Na terceira repetição a situação registrou corretamente o pedido para esperar, mas a presença priorizou a chamada direta. O runtime não reescreveu essa decisão.
- Envolvimento direto no pensamento incompleto passou de 1/3 para 3/3. C++/engine permanecem nenhum: interesse não é confundido com estar sendo chamada.
- Reason codes são estruturalmente estáveis, mas a escolha causal ainda varia: continuidade recebeu interesse_no_assunto em uma repetição; pensamento incompleto recebeu chamada_direta na resposta prematura. Isso reforça manter motivo fora da FSM.
- A situação ainda pode superinterpretar: qualificou negação como “veementemente”, supôs “evento passado” no conflito e marca cuidado em discordância técnica. Não é uma leitura objetiva infalível.
- Não houve amostra independente/holdout, seed controlado ou prova estatística. A revisão foi calibrada nestes exemplos conhecidos; generalização precisa ser verificada com conversas novas.

### Latência e tokens

| Métrica | 3.6 duas chamadas (20 avaliações) | 3.7 inicial (30) | 3.7 final (30) |
|---|---:|---:|---:|
| Situação média, incluindo retries | 4,67 s | 5,54 s | 5,38 s |
| Presença média, incluindo retries | 3,87 s | 3,98 s | 4,26 s |
| Total LLM médio | 8,53 s | 9,52 s | 9,65 s |
| Retries corretivos | 4 | 0 | 0 |
| Total de chamadas | 44 | 60 | 60 |
| Avaliações estruturalmente válidas | 20/20 | 30/30 | 30/30 |

O baseline citado pelo usuário (4,93 + 3,72 = 8,65s) é mantido como referência histórica; o relatório 3.6 medido registrou 4,67 + 3,87 = 8,53s. Não substituímos números para favorecer comparação.

**Não ficou mais rápido.** Contra 3.6 medido, a presença aumentou cerca de 10,2% e o total 13,0%. O contrato ficou mais confiável, mas os prompts explicativos ficaram maiores; não há ablação que prove qual fator causou a diferença. Nenhuma otimização arquitetural/single-call foi feita.

Tokens médios **por chamada** na matriz final:

| Etapa | Prompt | Completion (inclui reasoning) | Reasoning | Completion sem reasoning |
|---|---:|---:|---:|---:|
| Ler Situação | 763,9 | 661,8 | 569,6 | 92,2 |
| Decidir Presença | 1490,8 | 517,4 | 470,0 | 47,4 |

Uso retornado pelo provider, não estimativa de caracteres. O JSON final de presença é pequeno, mas o reasoning domina os tokens. Sem usage registrado no 3.6, **não é possível afirmar redução de output tokens contra esse baseline**. Mesmo entre as duas passagens 3.7, completion de presença subiu de 481,3 para 517,4.

### Verificação final do escopo

- Core: **224 passando**, incluindo isolamento, batch snapshots e referências administrativas.
- Backend/LLM offline: **178 passando / 13 opt-in separados**.
- JS/editor+biblioteca: **82 passando**.
- Matriz Gemma: **30/30 válidas** em cada passagem, total 60 avaliações/120 chamadas/zero retries.
- 54 combinações determinísticas da FSM 3.7; 108 caminhos anteriores 3.6 preservados.
- Projeto 3.7 salvo/carregado/validado via backend HTTP temporário com chamadas externas proibidas; import/export JS idêntico.
- Build da solução: zero warnings/erros; format somente C# alterado e verify-no-changes; sintaxe JS e diff check.
- Nenhuma execução de OpenClaw, Graphiti ou envio. A falha antiga de OpenClaw permanece fora do escopo.
- Artefatos Launcher e alterações pessoais/antigas na main não pertencem a este slice.

## Recomendação

**Pronto para implementar Gerar Resposta em simulação, ainda sem envio.** O contrato, isolamento e FSM V1 são executáveis e explicáveis; afinidade agora diferencia debate/criação de manutenção banal nesta amostra. Duas Instructions permanecem aprovadas, não reabrimos single-call.

Não chamar isso de comportamento perfeito nem de prontidão para chats reais: o caso de pensamento incompleto ainda respondeu prematuramente em 1/3. O próximo experimento de geração deve manter esse caso explícito, verificar que não inventa a explicação ausente e separar avaliação comportamental de garantias estruturais. Antes de envio, testar material novo/holdout e timing real. Não foi adicionada uma regra escondida para corrigir o modelo.

O personalityCore continua sem uso nestas duas Instructions. Memória de estilo/limites continua fora da decisão padrão; sua conexão futura deve ser explícita após RESPONDER.
