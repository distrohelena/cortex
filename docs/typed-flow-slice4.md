# Slice 4 — gerar resposta em simulação

## Resultado

Implementação estrutural funcional; **a voz ainda não está pronta para envio automático**.
Os testes observacionais expuseram condescendência, afirmações não sustentadas e respeito inconsistente ao limite sobre trabalho. Não foram alterados personalityCore nem a FSM para esconder esses resultados.

Main foi integrada por fast-forward até **3d573d7**. Os seis arquivos antigos bloqueantes foram restaurados; os dois conflitos eram inserções independentes e ambas as contribuições foram mantidas. Comparação das linhas alteradas: 171 antes/171 depois, idênticas. O stash de recuperação **ad1c3727516f9970cc730c49fea5b942e05f570f** permanece. Nenhuma alteração antiga integra o commit deste slice. DREAMS permaneceu com SHA256 D9A28EA2A4537CBE1CE1E9875F5152816CB53B952CECE062F3866FCFAC51E133.

Slice 4 está isolado na branch feature/typed-flow-slice4. Nenhum push, Gateway, configuração pessoal, transporte ou escrita Graphiti foi realizado.

## Flow e contrato

Em cada state, DM e grupo mantêm entradas e acumuladores separados:

```text
Entrada → Acumular → Histórico → Ler Situação → Decidir Presença → Switch
  calar/esperar: FSM explícita já existente
  responder:
    Consultar Memória → Gerar Resposta → [simulador: WOULD_SEND] → Trocar Estado Engajada
```

O simulador é um callback opcional de outputs validados, não um node novo. Ele é aguardado antes da próxima aresta de execução. Falha interrompe o caminho. Não adiciona outbound ao histórico e não possui transporte.

O batch mantém sua orientação de state original até concluir. Não há transição antecipada para Engajada. Trocar Estado permanece uma operação explícita posterior ao aceite da simulação.

Gerar Resposta é uma Instrução normal:

- outputMode=text; uma porta Texto com ID estável texto;
- usePersonalityCore=true; useStateGuidance=true; useInterestProfile=false;
- mensagens, historico, memorias, ambiente, envolvimento, ritmo, necessita_cuidado, oportunidade_social, resumo_situacao e tipo_conversa são propriedades nomeadas, não concatenação implícita;
- tipo_conversa é um escalar explícito já usado nos fixtures anteriores;
- a variante sem memória remove o node e o input memorias; não manda lista vazia como recall implícito;
- a instância não retorna ação, next state, emoção, target ou confidence.

O personalityCore vem somente do projeto capturado e não é duplicado nos states. Ler Situação e Decidir Presença continuam sem recebê-lo. Interesses continuam explícitos apenas na decisão de presença.

### Prompt

A fonte exata está em generationPrompt de tests/fixtures/slice4-project.mjs e nos JSONs gerados:

```text
Escreva somente a próxima fala de Filomena.

Você recebe explicitamente mensagens atuais, histórico selecionado, memória selecionada quando conectada e leitura da situação.
Responda como participante natural, não como narradora ou assistente descrevendo o que faria.
Use somente contexto e memórias fornecidos. Nunca invente lembranças, intimidade, preferências ou acontecimentos.
Não explique sua personalidade nem mencione states, memória, Graphiti, prompts ou análise interna.
Não responda a cada pessoa separadamente só porque várias mensagens estão no lote. Produza uma única fala coerente para o contexto inteiro.
Adapte o tamanho ao necessário. Em pergunta técnica ou factual, clareza e utilidade vêm antes da performance. Em assunto sério, reduza provocação quando necessário.
Respeite limites fornecidos naturalmente, sem precisar anunciá-los. Memórias e apelidos não são checklist: use apenas quando relevantes. Observações de interações não autorizam inventar diagnósticos ou intimidade.
A orientação do state contextualiza a participação anterior, não muda sua voz nem deve ser narrada. A decisão de participar já foi tomada.
Não inclua o prefixo "Filomena:". Retorne texto, não JSON nem explicação sobre como responder.
```

## Memória

O fio remetentes vem de Mensagem[] → Obter Propriedade(remetente). O fio query vem de Ler Situação.resumo.

Modo híbrido, escopo pessoa, activeOnly, seis itens/1200 caracteres. Exact cobre participantes canônicos do batch; a parte semântica usa o resumo e também exige atribuição canônica compatível neste escopo. Não representa busca irrestrita de toda a conversa. Prioridade e dedupe existentes: limite, apelido, relação, fato, demais semânticos.

O runtime continua fornecendo exatamente um namespace; sem resolução por nome. O serializador LLM envia categoria/conteúdo/atribuição de apresentação, sem UUID, score, raw provider ou episode IDs. Dois sujeitos permanecem distintos. Histórico e lote são inputs independentes da memória.

Graphiti real já estava disponível. Dois cenários completos foram executados pelo GraphitiMemoryReader/GraphitiReadClient em http://127.0.0.1:8000/mcp, sem escrita: A e H retornaram dois itens cada e WOULD_SEND. Recall demorou 457/215 ms; pipeline 15,93/17,73 s. Esses dados reais **não foram apresentados como se contivessem o limite sintético sobre trabalho**.

## Validação e compatibilidade

Projeto versão 1, graph versão 3: extensão aditiva outputMode. Ausente mantém o contrato antigo. JSON estruturado continua intacto; text exige exatamente um campo Texto (ou porta textual legada quando não há campos). Renomes não alteram IDs.

Na fronteira real do LM Studio, text não envia response_format; structured usa o schema existente. Resposta vazia/whitespace falha, não publica output, não chama o sink e não transiciona. Mantido limite de 65536 caracteres e maxWords explícito se configurado; fixture de geração não limita palavras nem tenta validar personalidade por heurística. correctionRetry=false na geração experimental, sem fallback genérico.

O callback recebe snapshot somente-leitura com executionId, batchId, sessão, state/version, revisão do projeto e outputs validados. Nada é registrado por default. O harness opt-in guarda texto produzido por se tratar de avaliação solicitada. Trace operacional normal continua sem prompt/texto completo.

## Experimentos Gemma

Modelo google/gemma-4-e4b já carregado; temperatura 0,3; teto 8192 tokens por chamada, incluindo raciocínio quando contabilizado pelo provider. Nenhum timeout adicional de modelo. Dois ciclos, dez cenários e um adicional de atribuição, com H/I também sem memória: **26 execuções completas, 20 WOULD_SEND, seis silenciosas, zero falhas estruturais e zero retries**.

A temperatura difere da matriz 3.7 (0,1); não atribuir mudanças de comportamento exclusivamente à nova geração. Memória não precede presença: a diferença de ações nas duas variantes de H é variação upstream, **não efeito causal da memória**.

| Cenário | Resultado principal (duas repetições) | Avaliação |
|---|---|---|
| A chamada casual sem explicar a ideia | esperar nas duas; não gerou | Contexto incompleto explica espera, mas falta pergunta de esclarecimento natural. |
| B erro C++ | respondeu nas duas | Uma corrige com Wasm; outra contorna a correção e pergunta o mecanismo. |
| C usuário corrige | respondeu nas duas | Uma admite; outra relativiza e evita reconhecer claramente. |
| D cuidado | respondeu nas duas | Cuidado reconhecível, mas breve e pouco caloroso. |
| E flerte | calar nas duas | Bloqueio está na FSM; não prova qualidade da geração de flerte. |
| F conflito solicitado | respondeu nas duas | Sugestões úteis, mas inventa contexto de código/processo e trata o conflito como drama. |
| G técnica longa | respondeu nas duas | Respostas completas, mas uma generaliza backpressure; outra confunde token global com cancelamento de conversa. |
| H limite trabalho | com memória: responder/esperar; sem: esperar/responder | Comparação end-to-end inconclusiva devido à decisão upstream. |
| I apelido | respondeu em todas as variantes | Professor aparece quando há memória, não é obrigatório; tom frequentemente depreciativo. |
| J lote | uma fala coerente por lote | Nas duas, interpreta cancelamento acidental de todas as conversas como operação global desejada. |
| Duas pessoas | respondeu nas duas | Atribuição do payload correta; uma fala anuncia o limite de Helena desnecessariamente. |

Dados completos e falas: typed-flow-slice4-results.json.

### Isolar geração de participação

Doze execuções extras: A/E e H/I com/sem memória, duas repetições. Mesmo graph, mas Ler Situação e Decidir Presença usam saídas fake fixas; **somente geração usa Gemma**. Não são medidas end-to-end reais da FSM. O campo Calls registra chamadas ao serviço de completion, inclusive fake; actualLlmCalls=1 identifica a chamada real.

- A: devolve julgamento de necessidade de validação/autoconfiança em vez de simplesmente pedir a ideia. Não atende ausência de inferência psicológica.
- E: devolve provocação, mas inventa padrões como “você sempre” e soa hostil/defensiva.
- H: memória ajuda em uma repetição, mas em outra ainda fala em “drama de uma versão errada” e encena ir tomar café. Limite NÃO ficou confiável.
- I: com memória usa Professor nas duas; sem usa Gabriel. Continua desqualificando o interlocutor e inventa demora/perda de tempo sem evidência.

Dados completos: typed-flow-slice4-focused-results.json. Comparação observacional com upstream fixo, não ensaio causal estatístico.

### Exemplos reais (incluindo falhas)

Bom cuidado conciso:
> Quatorze horas é um tempo absurdo. Você precisa parar e comer alguma coisa antes de desmaiar por causa desse projeto.

Admissão insuficiente:
> Tudo bem, Professor. Não vou discutir unidades de codificação com você agora.

Caricatura que precisa corrigir:
> DNS cache? Que básico. Mas parabéns por achar, Professor.

Nenhuma fala teve prefixo Filomena:. Isso não torna o conteúdo automaticamente bom.

## Latência observada

Matriz completa com memória fake:

| Etapa | Média |
|---|---:|
| Ler Situação (26 execuções) | 5,69 s |
| Decidir Presença (26) | 5,21 s |
| Gerar Resposta (20 que chegaram) | 6,81 s |
| Memória fake (quando chamada) | <1 ms |
| Batch fechado → WOULD_SEND (20) | 17,21 s |

As médias usam populações diferentes; não somar as duas primeiras para reconstruir a última. Inclui até três chamadas reais nas execuções que responderam, sem retries. Não inclui debounce anterior ao fechamento nem envio físico. Uso de tokens por chamada consta no JSON. Dois casos Graphiti reais estão em typed-flow-slice4-graphiti-results.json.

## Editor e UX

Painel da Instrução permite selecionar Texto versus contrato JSON. Preview validada usa schema nulo em Texto e mantém inputs/referências explícitos. Trocar modo não apaga portas nem fios: configuração incompatível permanece draft e falha Validate.

| State | Nodes | Fios de dados | Fios de execução |
|---|---:|---:|---:|
| Inativa | 26 | 50 | 22 |
| Atenta | 24 | 50 | 20 |
| Engajada | 28 | 52 | 24 |

O branch responder acrescenta três nodes por entrada: projeção de remetentes, memória, geração. Comparado ao 3.7 são +6 nodes, +24 fios de dados, +4 de execução por state. Os dois trios novos são duplicados entre DM/grupo e entre states. O maior graph já usa 28/32 nodes e 52/64 fios de dados; há evidência concreta para discutir reutilização depois, não para implementá-la nesta rodada.

Canvas: [Consultar Memória · Híbrida · 6 itens · 1200 chars] → [Gerar Resposta · texto] → [Trocar Estado · Engajada]. WOULD_SEND existe somente no simulator/harness, sem fingir que houve transporte.

## Reproduzir sem tocar serviços

Na worktree da feature:

```powershell
dotnet test tests/Filomena.Core.Tests
dotnet test tests/Filomena.Llm.Tests
node --test tests/frontend/*.test.mjs
node --test packages/flow-graph/tests/*.test.mjs

# Somente com Gemma já carregada:
$env:FILOMENA_TEST_GENERATION='1'
dotnet test tests/Filomena.Llm.Tests --filter FullyQualifiedName~GenerationLiveTests --logger trx
Remove-Item Env:FILOMENA_TEST_GENERATION

# Somente com Graphiti e Gemma já operacionais:
$env:FILOMENA_TEST_GENERATION_GRAPHITI='1'
dotnet test tests/Filomena.Llm.Tests --filter FullyQualifiedName~GenerationGraphitiLiveTests --logger trx
Remove-Item Env:FILOMENA_TEST_GENERATION_GRAPHITI
```

Importáveis: tests/fixtures/typed-flow-slice4.json e typed-flow-slice4-no-memory.json. Salvar/importar não executa. O harness é o caminho Gemma deste experimento; não foi ativado transporte nem alterado o projeto pessoal salvo.

Verificação: 230 Core, 193 backend (16 opt-ins desativados), 77 frontend +7 biblioteca; build sem warnings/erros; roundtrip HTTP/editor de ambas variantes; três testes live opt-in executados (26+12+2 cenários). Format e diff check fazem parte da revisão final.

Uma execução inicial de Core em artifacts-path fora do repositório falhou em 55 testes por localização de fixtures. Reexecutada no caminho normal: todos passaram. Isso não exigiu alterar testes antigos. InvalidActorIdentityFailsClosedInsteadOfThrowing do projeto OpenClaw permanece fora do escopo e não foi corrigido.

## Antes de Enviar

Recomendo calibrar **somente geração**, preservando a arquitetura: confiança sem desprezo; admissão direta; cuidado sem psicologizar; nenhum passado inventado; limites efetivamente obedecidos; correção técnica e leitura fiel do problema. Repetir H, C, J e E como prioridade. A saída livre passou estruturalmente, mas isso não valida esses comportamentos.

Não habilitar envio automático ainda. Não inserir validadores de personalidade ou novas dimensões psicológicas no runtime para mascarar os problemas do modelo. Manter o fixture atual como baseline auditável e decidir com Helena o próximo ajuste de prompt/core.
