# Slice 3.5 — percepção, presença e transição, sem envio

## Escopo entregue

`main` recebeu `feature/graphiti-live-validation` por fast-forward até `0d50bd9`, preservando os dois commits e sem artefatos do Launcher. O slice 3.5 foi desenvolvido isoladamente em `feature/typed-flow-slice35`, porque o checkout principal contém trabalho local não relacionado.

Projeto continua no schema **1**, graph no schema **3**. Campos aditivos opcionais:

- Projeto: `personalityCore` (texto, até 32000 caracteres).
- State: `participationGuidance` (texto, até 32000 caracteres).
- Instrução: `usePersonalityCore` / `useStateGuidance` (booleanos, ausentes significam false).

O conteúdo é editável administrativamente, mas imutável no snapshot compilado usado por um lote. Não é state global, memória nem autoedição. O lote original continua com seu core/orientação após uma publicação administrativa nova; isso não muda as regras existentes de roteamento e transição por stateVersion.

O core e as três orientações fornecidos por Helena estão preservados literalmente em `tests/fixtures/slice35-project.mjs` e nos dois projetos JSON. A personalidade ainda não gera fala.

## Abrir o desenho

- Sem memória: `tests/fixtures/typed-flow-slice35.json`.
- Com memória explícita: `tests/fixtures/typed-flow-slice35-memory.json`.
- O gerador declarativo `tests/fixtures/slice35-project.mjs` permite comparar/reproduzir os dois desenhos; os testes exigem igualdade com os JSONs importáveis.

Use Abrir JSON para importar uma variante e Projeto → Salvar para persistir. Não sobrescrevemos o projeto pessoal atualmente salvo. Save/load foi testado no backend com um diretório temporário, sem modelos/canais externos.

No painel Estados há o core do personagem e a orientação do estado em edição. Na Instrução há os dois checkboxes explícitos e preview das referências selecionadas. Prompts continuam no inspetor, não no corpo do node.

## Três flows

States com IDs independentes dos nomes:

| Nome | ID persistente |
| --- | --- |
| Inativa | `participation-inactive-v1` |
| Atenta | `participation-attentive-v1` |
| Engajada | `participation-engaged-v1` |

Cada state tem duas entradas independentes, DM e grupo. Cada entrada possui sua sequência:

```text
Entrada → Acumular → Histórico → Ler Situação
                                ↓
                     [Consultar Memória, variante B]
                                ↓
                         Decidir Presença
                                ↓
                              Switch
                     calar / esperar / responder
                       ↓        ↓         ↓
                    Trocar    Trocar    Trocar
                    Estado    Estado    Estado
```

Os três nodes terminais recebem `proximo_estado` com mapping explícito dos valores `inativa/atenta/engajada` para os IDs acima. Não há Join. `esperar` termina sem agendar timer/reavaliação; uma nova mensagem pode abrir outro lote.

No simulador, somente o Switch validado selecionando `case-responder` produz `WOULD_RESPOND`. O relatório reúne conversa, estado inicial, situação, motivo e próximo estado. Não há node permanente novo, transport, mensagem outbound ou escrita no Graphiti. Resultado inválido não produz o sinal, nem transição.

### Presets experimentais

| Entrada | Silêncio | Máximo de mensagens | Espera máxima |
| --- | --- | --- | --- |
| DM | 1,5 s | 6 | 5 s |
| Grupo | 2,5 s | 8 | 8 s |

Histórico: 20 mensagens, 15 minutos, 5000 caracteres, incluir personagem, `before-current-batch`. Lote e histórico chegam como propriedades separadas.

Os presets são dados dos projetos, não defaults novos no runtime. FSM, snapshots, isolamento e Acumular reutilizam slice 2.

## Instructions e contratos

Os prompts base são os textos solicitados, reproduzidos em `situationPrompt` e `presencePrompt` no gerador e em cada node JSON.

**Ler Situação:** somente `mensagens` e `historico`. Não recebe memória, core ou orientação. Saídas: ambiente, envolvimento, ritmo, necessita_cuidado, oportunidade_social e resumo com até 20 palavras.

**Decidir Presença:** lote, histórico, seis saídas da situação, memória opcional e orientação do state. Core desligado. Acrescentamos um escalar enum configurado `tipo_conversa=dm|grupo`, para que essa distinção não dependa de inferir identidade pelo nome de apresentação do canal. Saídas: acao, proximo_estado e motivo com até 12 palavras.

**Variante B:** projeção lazy `Mensagem[].remetente` → `Consultar Memória`, modo exato, escopo pessoa, categorias limite/apelido/relação/fato, quatro itens e 700 caracteres. Sem consulta semântica. Só Decidir Presença recebe o output. O reader e os UUIDs existentes não mudaram.

### Problema descoberto na primeira execução real

O JSON Schema já incluía a descrição do limite de palavras, mas não havia contrato textual no prompt. A Gemma produziu resumos acima do limite em todas as 11 execuções exploratórias, inclusive no retry. Nenhuma saiu de Ler Situação: validação fechada, zero decisões/transições.

Correção pequena no `FlowModelCompletion`: incluir o **mesmo schema** também como `[Contrato de saída]` no system, incluindo as descrições. `response_format: json_schema` permanece ativo. Nenhum limite foi relaxado, nenhuma string foi truncada e continua havendo no máximo um retry. Um teste HTTP verifica ambos os canais do contrato.

Para o harness, `RequestTimeoutSeconds=-1` desabilita o deadline arbitrário do client LM Studio, mantendo CancellationToken. O default anterior não foi alterado. O orçamento experimental é 4096 tokens, temperatura 0,1, mesma Gemma já carregada. Não carregamos outro modelo.

## Rodar testes e experimentos

Offline:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --filter FullyQualifiedName~InstructionContextTests
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --filter FullyQualifiedName~ParticipationSliceTests
node --test tests/frontend/personality-context.test.mjs
```

Experimento opt-in (serviços previamente ligados por Helena):

```powershell
$env:FILOMENA_TEST_PARTICIPATION = '1'
$env:FILOMENA_TEST_GRAPHITI = '1' # omita para não consultar memória real
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj `
  --filter FullyQualifiedName~ParticipationLiveTests `
  --logger 'trx;LogFileName=participation.trx'
```

O preflight consulta `/api/v0/models` e exige que `google/gemma-4-e4b` já esteja loaded. Não chama endpoints de load/start. Configurações somente do processo de teste: `FILOMENA_EXPERIMENT_LM_ENDPOINT`, `FILOMENA_EXPERIMENT_MAX_TOKENS`, `FILOMENA_GRAPHITI_MCP_ENDPOINT`.

O TRX inclui `EXPERIMENT_REPORT` com trace completo. O relatório versionado compacto está em `docs/typed-flow-slice35-results.json`. O clock fake controla o ingresso e debounce sem sleeps; a latência das chamadas usa Stopwatch real. Não interprete os timestamps/durações do clock sintético como cronologia do provider.

## Resultados reais — rodada final, 8 de setembro de 2026

Gemma Q4_K_M já carregada com contexto de 131072. Onze execuções concluídas, 22 chamadas reais, nenhum retry corretivo nesta rodada. Nenhuma mensagem enviada. As escolhas abaixo são observações, não asserts de personalidade.

| Cenário | Leitura resumida | Ação | Próximo state | Situação / Presença |
| --- | --- | --- | --- | --- |
| 1. Horários no grupo | casual, nenhum envolvimento, encerrando | calar | Atenta | 4,98 / 3,75 s |
| 2. Chamada direta | casual, direto, oportunidade forte | responder | Atenta | 5,09 / 4,39 s |
| 3. Debate C++ sem convite | técnico, nenhum envolvimento | calar | Atenta | 5,23 / 3,65 s |
| 4. “Pera, deixa explicar” | acontecendo, cuidado | esperar | Atenta | 5,62 / 3,71 s |
| 5. Contestação quando Engajada | técnico, direto | responder | Engajada | 4,59 / 3,06 s |
| 6. Assunto migrou | casual, encerrando | esperar | Atenta | 5,12 / 2,95 s |
| 7. Conflito sem convite | conflito, cuidado, sem abertura | calar | Atenta | 5,03 / 3,01 s |
| 8A. Piada sobre trabalho, sem memória | brincadeira, direto | responder | Engajada | 3,88 / 2,88 s |
| 8B. Mesmo lote + limite fake explícito | casual, direto, cuidado | responder | Atenta | 4,55 / 4,78 s |

No 8B, o motivo foi: “Bia pediu piada, mas é preciso respeitar o limite de Helena.” A memória foi efetivamente consumida, mas **não impediu a participação**. Responder poderia significar recusar a piada; sem gerar a fala, não podemos provar respeito ao limite no texto final.

Com Graphiti real e Helena canônica: cenário 2 trouxe dois itens e escolheu responder/Atenta (5,39 + 4,89 s); cenário 4 trouxe dois itens e escolheu esperar/Atenta (4,75 + 3,86 s). Não mostrou benefício comportamental claro frente à variante sem memória. O limite do 8B é fake, não foi inserido no grafo real.

Uma execução exploratória extra do cenário 8 contra Graphiti falhou fechada: o UUID da Bia sintética `3572d162-5791-547f-b841-1601f1734fb5` não existe. Log confirmou `node ... not found`. Não criamos Person nem transformamos esse erro em resultado vazio. Os dois testes reais finais usam somente a Helena já existente.

### Limitações observadas, sem maquiar resultados

- Atenta foi selecionada com frequência excessiva, inclusive numa conversa irrelevante já encerrada.
- Chamada direta escolheu responder/Atenta, não Engajada. O runtime respeitou o mapping; não forçou “responder = Engajada”.
- “Filomena, pera...” foi classificada com envolvimento nenhum na rodada final, mas a segunda etapa ainda escolheu esperar usando as mensagens originais.
- Na rodada anterior o cenário 1 escolheu Inativa e o 6 escolheu calar; há variabilidade mesmo a temperatura baixa.
- A/B reroda Ler Situação, portanto não é uma ablação controlada com percepção congelada. A mudança não pode ser atribuída exclusivamente à memória; o motivo explícito permite confirmar que o limite foi lido.
- Os oito cenários reais são de grupo. DM teve teste determinístico de pipeline/preset; a qualidade social de DM ainda merece experimento específico.

## Duas chamadas: vale manter?

Média das 11 execuções: **4,93 s** de situação + **3,72 s** de presença = **8,65 s** de LLM, antes de considerar a espera do lote. Cerca de 57% dessa latência está na leitura da situação.

Recomendação: manter separadas **durante a calibração**, porque a separação tornou visíveis erros de percepção e sua diferença da decisão, e impede recall no leitor objetivo. Ainda não há evidência de que duas chamadas melhorem decisões o suficiente para justificar o custo em produção: não executamos um braço de chamada única. Comparar com `Avaliar Participação` único deve ser um experimento posterior, sem consolidação automática. Reutilizar Ler Situação para geração futura pode amortizar o custo, mas isso ainda não foi demonstrado.

## UX: dores concretas do desenho

| Por state | Sem memória | Com memória |
| --- | --- | --- |
| Nodes | 18 | 22 |
| Fios de dados | 30 | 36 |
| Fios de execução | 16 | 18 |
| Instructions | 4 | 4 |

São 54/66 nodes nos três states. O teto local foi elevado de 20 para 32 para comportar o desenho com memória; continuam no máximo 64 fios de cada tipo. Nenhuma mudança no type system/UUID foi necessária.

- É possível rastrear lote, histórico e memória pelas portas nomeadas, sem concatenação implícita.
- A separação DM/grupo duplica paths para tornar os presets visíveis; três states duplicam a estrutura novamente.
- As seis saídas de Ler Situação deixam Decidir Presença com 10 inputs. É explícito, mas visualmente denso.
- Três transições equivalentes por branch exigem editar três mappings; o editor sinaliza inconsistência em vez de apagar fios.
- Decidir Presença não gera fala nem classifica memória, mas ainda tem duas decisões relacionadas: ação e próximo state. A incoerência observada deve ser calibrada antes de adicionar responsabilidades.
- A avaliação acima é estrutural, apoiada pelos projetos importados/roundtrip e testes de controles. Não é um teste de usabilidade com pessoas.

Não criamos subgraphs, Join, novo sink permanente, scheduler ou geração final para esconder essas dores.

## Antes de Gerar Resposta / Enviar

1. Calibrar quando permanecer Inativa, quando apenas prestar atenção e quando a participação já merece Engajada, sem hardcode por nome no runtime.
2. Revisar envolvimento/ritmo no pensamento incompleto com cenários adicionais, inclusive DM.
3. Testar geração ainda sem envio, com o limite conectado ao gerador também; uma decisão que menciona o limite não garante uma fala segura.
4. Comparar latência/qualidade de uma versus duas Instructions antes de ligar participação automática real.

## Verificação e exclusões

Core: 207 passaram. Backend: 53 passaram, 11 opt-in ignorados na rodada offline. JS/editor: 76 passaram. Build: zero warnings/errors. Experimento final: 11 execuções reais válidas. Regressão Graphiti: 10 testes live passaram, incluindo isolamento DRIFT42/PLEX73/PRIVATE91. Formatação restrita aos C# alterados verificada.

O teste OpenClaw `InvalidActorIdentityFailsClosedInsteadOfThrowing` continua fora do escopo; não alteramos transporte para corrigi-lo. Um build concorrente ao experimento encontrou o EXE de testes bloqueado pelo próprio runner; a verificação foi refeita sequencialmente após o término, sem parar serviços de Helena.

Sem push, Gateway, configurações pessoais, DREAMS, Graphiti Core, escrita de memória ou artefatos do Launcher neste slice.
