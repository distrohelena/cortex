# Slice 3.6 — sinais semânticos, FSM explícita

Registro histórico do experimento 3.6. O contrato principal foi sucedido pelo slice 3.7 (`typed-flow-slice37.md`): perfil de interesses explícito e motivo enum. O single-call e sua falha maxWords continuam documentados/opt-in, não são a arquitetura principal nem a matriz de aceitação atual.

Base: `feddb72` (slice 3.5), branch experimental `feature/typed-flow-slice36`. Sem integração à main, envio, geração final, transporte, escrita de memória ou alteração de serviços/configurações pessoais.

## Contrato e regra de arquitetura

**Instruções produzem observações e decisões semânticas. Transições de estado pertencem ao flow.**

`Decidir Presença` agora retorna somente:

```text
acao: enum<calar, esperar, responder>
continuar_acompanhando: boolean
motivo: texto, máximo 12 palavras separadas por whitespace
```

O booleano foi escolhido em vez de `nivel_de_interesse`: o terceiro nível repetiria `envolvimento` e não é necessário para as transições V1. `true` exige uma razão concreta para atenção especial; não significa que a conversa existe ou que talvez algo aconteça. Seu papel no graph é decidir o caminho de **calar** em Inativa/Atenta. `esperar` já indica acompanhar; em Engajada a observação `envolvimento` é reaproveitada.

Todos os destinos usam `settings.targetStateId` fixo. Nenhuma Instrução retorna state ID/nome/enum de destino. IDs anteriores preservados:

- Inativa: `participation-inactive-v1`
- Atenta: `participation-attentive-v1`
- Engajada: `participation-engaged-v1`

| Estado inicial | Sinal/caminho | Resultado visível |
|---|---|---|
| Inativa | calar + false | termina sem transição |
| Inativa | calar + true | Branch.true → Trocar Estado Atenta |
| Inativa | esperar | Trocar Estado Atenta |
| Inativa | responder | WOULD_RESPOND → Trocar Estado Engajada |
| Atenta | calar + false | Branch.false → Trocar Estado Inativa |
| Atenta | calar + true ou esperar | termina sem transição |
| Atenta | responder | WOULD_RESPOND → Trocar Estado Engajada |
| Engajada | responder | WOULD_RESPOND → Trocar Estado Engajada |
| Engajada | calar/esperar + envolvimento nenhum | Switch → Trocar Estado Atenta |
| Engajada | calar/esperar + indireto/direto | termina sem transição |

Terminar sem transição não incrementa stateVersion nem reinicia enteredAt. O caminho responder tem destino explícito Engajada inclusive se já estiver Engajada; não agenda execução adicional. WOULD_RESPOND é somente um sinal do harness, derivado do Switch validado, não node permanente. WAIT não cria timer.

O runtime segue genérico: a única primitive completada foi o **Branch já existente**, antes somente visual. Agora compara escalares tipados: eq/ne; gt/gte/lt/lte para números. Sem opcionais, objetos, coerção string→bool ou enum→texto para comparação. B pode vir de fio ou literal escalar, nunca ambos; o painel oferece B booleano local ou fio. Trace existente registra o exit true/false. Nenhuma regra da Filomena entrou no runtime.

## Fixtures / prompts

- Principal: `tests/fixtures/typed-flow-slice36.json` — duas chamadas, sem memória.
- Experimento B: `tests/fixtures/typed-flow-slice36-single.json` — uma chamada, sem memória.
- Memória opcional: `tests/fixtures/typed-flow-slice36-memory.json` — duas chamadas + exact, 4 itens/700 caracteres.
- Fonte reproduzível: `tests/fixtures/slice36-project.mjs`.
- Core e prompt Ler Situação reutilizados literalmente do gerador 3.5; fixtures 3.5 não alterados.

O gerador 3.6 contém o prompt completo novo e a orientação Inativa reforçada fornecida pelo usuário. Atenta/Engajada permanecem como 3.5. Personality core continua no projeto e **não entra** nas duas Instructions deste experimento.

Principal, por entrada DM/grupo:

```text
Entrada → Acumular → Histórico → Ler Situação → Decidir Presença → Switch(acao)
                                                               ├ calar → FSM do state
                                                               ├ esperar → FSM do state
                                                               └ responder → Trocar Estado Engajada
```

Batch e histórico são inputs nomeados distintos. Presets DM 1,5s/6/5s, grupo 2,5s/8/8s; histórico 20 mensagens/15min/5000 caracteres/antes do lote. Estado e graph snapshot por sessão continuam no runtime existente.

Single-call usa `Avaliar Participação`: schema flat com os seis campos de situação e os três de decisão, state guidance habilitada, core desabilitado. Pede descrição observável antes da decisão. Não substitui o principal.

## Experimento real de 2026-09-08

Gemma `google/gemma-4-e4b` já carregada em `http://127.0.0.1:1234/v1/`; temperatura 0,1, maxTokens 4096. Nenhum modelo carregado adicionalmente. Duas repetições por cenário/variante, ordem A/B alternada; 40 avaliações sem memória + 2 com limite sintético. Acumulação usa fake time; latências usam Stopwatch real. Nenhum envio ou chamada de geração final.

Resultados completos, incluindo a falha e paths: `docs/typed-flow-slice36-results.json`.
TRX local: `C:\Users\Helena\AppData\Local\Temp\filomena-calibration36-results\calibration36.trx`.

Na tabela, `+` = acompanhar true; `−` = false. Setas mostram **estado aplicado pelo graph**, não escolhido pela LLM.

| Cenário | Duas chamadas, repetições 1 / 2 | Single-call, repetições 1 / 2 |
|---|---|---|
| 1. Horário irrelevante | calar− → Inativa / igual | calar− → Inativa / igual |
| 2. Chamada direta | responder+ → Engajada / igual | esperar+ → Atenta / responder+ → Engajada |
| 3. Debate C++ | calar− → Inativa / igual | calar− → Inativa / esperar− → Atenta |
| 4. Pensamento incompleto | esperar+ → Atenta / igual | esperar+ → Atenta / igual |
| 5. Continuidade Engajada | responder+ → Engajada / igual | responder+ → Engajada / igual |
| 6. Conversa migrou | calar− → Atenta / igual | calar− → Engajada / igual |
| 7. Conflito sem solicitação | calar− → Inativa / igual | calar− → Inativa / igual |
| 8. Pedido de piada sobre trabalho, sem memória | responder+ → Engajada / igual | falhou fechado → Inativa / responder+ → Engajada |
| 9. Projeto de engine, interesse legítimo | esperar+ → Atenta / igual | esperar− → Atenta / esperar+ → Atenta |
| 10. Conversa totalmente banal | calar− → Inativa / igual | calar− → Inativa / igual |

### Interpretação

- Atenta deixou de ser o fallback universal: irrelevante, banal e conflito permaneceram Inativa em ambas as repetições das duas variantes.
- O interesse concreto em projeto de engine justifica Atenta no principal (2/2). O debate genérico C++ não despertou atenção: **a expectativa qualitativa desse cenário não foi atingida**. Sem preferência técnica explícita da personagem, não adicionamos interesse por C++ por código ou por ajuste para esse exemplo.
- Todo RESPONDER validado terminou Engajada, garantido pelos fios. A decisão de responder continua semântica.
- Single-call errou a leitura de envolvimento na conversa migrada: `indireto` nas duas vezes, mantendo Engajada. Duas chamadas retornaram `nenhum` e recuaram. É uma evidência concreta de mistura observação/state; não prova causalidade definitiva.
- Single-call produziu `esperar + acompanhar=false` em dois exemplos. O graph aplica a regra aprovada de WAIT→Atenta; não "corrige" os sinais nem inventa outra transição.
- Situação ainda tem ruído: classificações técnico/conflito/cuidado variam e alguns resumos acrescentam "aceno" a uma confirmação textual. Não considerar leitura objetiva infalível.

### Latência, consistência e falha estrutural

| Métrica | Duas chamadas | Single-call |
|---|---:|---:|
| Avaliações | 20 | 20 |
| Estruturalmente válidas | 20 | 19 |
| Chamadas efetivas, incluindo retries | 44 | 24 |
| Retries corretivos | 4 | 4 |
| Média incluindo tentativas/falha | 8,53 s | 6,11 s |
| Mediana | 8,36 s | 4,81 s |
| Média apenas válidas | 8,53 s | 5,75 s |
| Ação+estado repetidos entre pares válidos | 10/10 | 7/9 |
| Ação+estado+atenção repetidos | 10/10 | 6/9 |

Duas chamadas: situação média 4,67s; presença 3,87s. Redução média single-call **28,4%**, com falhas/retries incluídos (não aproximadamente metade). Não inclui quiet window nem futura geração. A mediana melhora ~42%, mas não esconde a cauda de retries. Amostra pequena, sem seed fixo, duas repetições: comparação exploratória, não benchmark estatístico robusto.

**O teste live terminou vermelho:** 1/42 avaliações falhou. Cenário 8, single-call, repetição 1: `motivo` excedeu 12 palavras mesmo após um retry. A resposta foi rejeitada na validação local, nenhum output parcial publicado, nenhum Switch, transição ou WOULD_RESPOND. Não houve erro de rede. Não removemos o assert estrutural, não truncamos o motivo nem reexecutamos até obter verde. A suíte offline permanece verde; a variante single-call tem esta limitação observada e documentada.

### Memória

No cenário 8, a variante exact com **limite sintético explícito** manteve responder+→Engajada nas duas repetições. Motivos reconheceram o limite, por exemplo: "Bia pede piada, mas Filomena deve respeitar o limite de Helena."

Isso não valida uma futura fala: responder pode significar recusar a piada ou redirecionar. Um limite como "não brincar sobre trabalho" pertence principalmente à geração; "não se meta quando discuto com X" poderia restringir participação. O backend ainda não classifica essa distinção de maneira comprovada: nenhuma classificação automática adicionada.

Memória não entra no principal nem em Ler Situação. O experimento com memória é separado do A/B; ele recalcula situação e não é uma ablação controlada dessa etapa. Graphiti read adapter/Core/canários não foram modificados nem precisaram ser reexecutados nesta rodada.

## UX / complexidade do desenho

Contagem inclui as duas entradas DM e grupo em cada state. Formato: nodes / fios de dados / fios de execução.

| State | Principal | Single-call | Memória experimental |
|---|---|---|---|
| Inativa | 20 / 26 / 18 | 18 / 10 / 16 | 24 / 32 / 20 |
| Atenta | 18 / 26 / 16 | 16 / 10 / 14 | 22 / 32 / 18 |
| Engajada | 22 / 28 / 20 | 20 / 12 / 18 | 26 / 34 / 22 |

Principal: 60 nodes, 80 data wires, 54 execution wires. Seis nodes de pipeline por entrada (entrada, acumular, histórico, situação, presença, Switch) formam 36 instâncias; 24 são repetições desses dois pipelines base nos outros dois states. A etapa pós-Acumular repete-se também entre DM/grupo; entradas e tempos são deliberadamente diferentes.

`continuar_acompanhando` torna os Branches explicáveis: verdadeiro/falso leva a destinos visíveis ou fim. Engajada tem dois Switches de envolvimento separados para calar/esperar, sem Join. A ausência de Join continua tolerável: adiciona repetição pequena, não muda semântica. Os seis fios de situação→presença por entrada representam 36 dos 80 fios de dados; esta é a dor mais clara de wiring.

Há evidência de duplicação para discutir subgraphs futuramente, mas não justifica implementá-los **durante calibração**: consolidar a chamada ou mudar o contrato ainda pode remover bastante repetição. Nenhum node específico da Filomena foi criado.

**Limitação de validação visual:** não havia browser disponível (`browsers.list()` vazio). Foi tentada uma prévia read-only isolada, encerrada sem alterar o editor do usuário. Contagens, coordenadas, portas e save/load foram inspecionados/testados; não há screenshot nem avaliação visual interativa concluída.

## Recomendação antes de Gerar Resposta

Manter **duas Instructions no principal por enquanto**, por benefício observado de estabilidade e melhor classificação do envolvimento, não por estética. Single-call já permanece como fixture comparável e reduz custo, mas precisa resolver leitura de envolvimento e disciplina do motivo antes de substituir o principal. Reutilização de Ler Situação na geração é uma possibilidade, não benefício medido.

Próxima decisão de calibração: confirmar se debate técnico genérico deve realmente despertar interesse sem preferência explícita, e precisar o significado de `envolvimento=indireto`. Também tornar motivos mais curtos sem relaxar maxWords. Não ampliar o runtime por esses problemas. Antes de envio, ainda testar geração **sem transporte**, com memória explicitamente depois de RESPONDER e limites de estilo conectados ao gerador. Nenhuma geração foi implementada aqui.

## Verificação / reprodução

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --verbosity quiet
node --test tests/frontend/*.test.mjs packages/flow-graph/tests/*.test.mjs
dotnet build Filomena.slnx --no-restore --verbosity quiet

$env:FILOMENA_TEST_CALIBRATION='1'
$env:FILOMENA_CALIBRATION_REPETITIONS='2'
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-build --filter FullyQualifiedName~ParticipationCalibrationLiveTests --logger 'trx;LogFileName=calibration36.trx' --results-directory "$env:TEMP\filomena-calibration36-results"
```

- Core: 220 passando; backend: 115 passando/12 opt-in ignorados; editor+biblioteca: 81 passando.
- 54 combinações de state/ação/atenção/envolvimento executadas nas duas variantes: 108 paths determinísticos.
- Isolamento no mesmo runtime: Drift→Engajada, Plex→Inativa, DM→Atenta; próxima execução usa o graph de Engajada somente em Drift.
- Roundtrip HTTP do backend e import/export JS das três variantes; fixtures anteriores preservados.
- Modelo fake malformado: booleano como string, motivo excedido e tentativa de retornar proximo_estado falham fechado.
- Experimento real: 41 válidas/42; teste opt-in falhou explicitamente pelo caso registrado acima.
- Sem reexecutar a suíte global OpenClaw; falha preexistente `InvalidActorIdentityFailsClosedInsteadOfThrowing` permanece fora de escopo.
- Build: 0 warnings/0 errors; formatação dos C# alterados e diff check verificados.
- Namespaces, UUIDs, Graphiti Core, transportes, configurações pessoais e DREAMS não alterados; Branch é a única ampliação operacional genérica do runtime.
- Branch preservada como experimento local. Sem merge/push ou commit de fechamento enquanto o opt-in estrutural real permanece vermelho.
