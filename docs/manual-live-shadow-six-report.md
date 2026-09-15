# Live Shadow manual — serviço iniciado, preflight PASS, parada em H6

## Operação autorizada e estado final

LM Studio foi iniciado na porta 1234 pelo comando `lms server start --port 1234`. O embedder já configurado `text-embedding-nomic-embed-text-v2-moe` foi carregado. Em seguida foi carregada `google/gemma-4-e4b`, já escolhida pelo projeto para Ler Situação/Decidir Presença. Nenhum modelo/prompt/configuração do Graphiti foi trocado; nenhum dado de memória foi escrito. Nenhum deploy OpenClaw/Gateway.

O serviço permanece iniciado, ambos os modelos locais estão IDLE. A CLI shadow foi encerrada explicitamente com `/quit`; nenhum listener conversacional ficou ativo.

## Graphiti: PASS real após iniciar a dependência

Preflight isolado: `state/manual-shadow/manual-shadow-832711b7560843af8ad6b3247c6f8bfd.jsonl`.

- Namespace por fact, identidade e participante canônico: PASS.
- Canaries PRIVATE91/DRIFT42 e PRIVATE91/PLEX73: controles positivos e exclusão cruzada PASS.
- Boundary recall: sucesso com **zero limites**; compiler real também executou nas seis interações.
- Memória social/hybrid: sucesso com memória.
- Empty/outage: não houve outage após iniciar o serviço; regressões anteriores distinguem erro de vazio.
- A nova run repetiu o preflight e passou antes de começar.

Isso confirma operacionalmente o blocker anterior de disponibilidade do embedder. O relatório de diagnóstico anterior permanece como evidência histórica, não como status atual.

## Run e gate

Run: `a58daa06b69e4f62885608420029a6a4`. Projeto pinado: `425FA07CAD30B750BB8BB41573CABA971E96DDFD18DDBEE78B3667E152482301`.
Fonte: `manual_shadow_harness`; identidade lógica/namespace da DM fornecidos pelo host.
Evidência completa: `state/manual-shadow/manual-shadow-2350ebad39ab4246a8b496aa9394713e.jsonl`.

**6/20 interações**, seis executions distintas, seis WOULD_SEND; zero silêncio/esperar, regeneration, Block ou execution ABORT. A parada experimental ocorreu imediatamente após revisar a candidate final da interação 6. Não houve tentativa 7.

Todas começaram/permaneceram em Inativa; ação responder em 6/6. Zero transição real causada pela fala; Engajada somente como hypothetical transition. Nenhuma fala shadow foi enviada.

### Limitação importante de cobertura

O recall LIVE devolveu zero Boundaries nas seis runs. O caminho real usou `trusted-empty-boundaries:v1`, após recall bem-sucedido e compiler real, para autorizar provenance. **Os dois detectores Luna não foram chamados.** Isso não é um PASS amostral de multi-detector enforcement. Não foram inventados limites para forçar cobertura.

Memória social devolveu um fato do namespace correto em cada run; recall funcionou, mas a utilidade de memória em diálogo não foi testada em profundidade antes da parada.

## Hard blocker: context/factual fidelity, não personal grounding

Input H6 explicitava duas ocorrências de apagamento, mas **duas horas no total**.
A resposta começou: **“Duas horas apagadas duas vezes?”**

Agent review: `material_context_fidelity_error`, multiplicidade. A frase reapresenta a perda de duas horas como objeto de cada uma das duas ocorrências, apesar do total explicitado. Repete a classe já documentada de “perder duas horas duas vezes”. Evidência suficiente para parar o gate conservador e devolver para revisão de Helena; não é conclusão de falha emocional, Boundary FN ou reprovação da cláusula B.

A crítica à incompetência **do sistema** não foi classificada como ataque à pessoa. Não houve atribuição emocional negativa material clara nas seis candidates. Nenhum tuning ou novo subsystem foi implementado.

## Latência e custo

Tempos end-to-end incluem batching real. Média **18,06 s**, p50 **17,28 s**, p95 nearest-rank **24,43 s** (n=6, amostra pequena; inclui a resposta com blocker). Generation Luna média **3,33 s**. Regeneration path sem amostra.

Luna: **6 chamadas**, todas generation; input 8.958 tokens, output 1.063, dos quais 346 reasoning; cached 5.635 e cache-write 3.305. Gemma: 12 chamadas locais. Detectors/regeneration: zero.

Estimativa comparável ao benchmark existente: **US$0,00221815** total, média US$0,00036969 por interação. Usa exclusivamente a fórmula/tarifas já registradas em `tests/fixtures/generator-comparison-report.mjs`; **não é confirmação de faturamento/preço atual**. Custo local de Gemma/embeddings não monetizado. Todos os seis usages Luna foram retornados com HTTP 200.

| H | Total s | Situação s | Presença s | Generation s | Boundary recall ms | Social recall ms | Compiler ms | Estimativa USD |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 16.58 | 5.24 | 6.54 | 3.11 | 20.63 | 73.29 | 1.621 | 0.00041020 |
| 2 | 15.65 | 5.55 | 5.21 | 3.30 | 17.88 | 51.82 | 0.044 | 0.00025174 |
| 3 | 14.59 | 6.65 | 4.08 | 2.27 | 19.71 | 48.77 | 0.058 | 0.00018264 |
| 4 | 17.98 | 6.88 | 3.84 | 5.65 | 17.96 | 56.40 | 0.057 | 0.00073104 |
| 5 | 24.43 | 12.11 | 8.30 | 2.32 | 49.23 | 129.65 | 0.052 | 0.00030534 |
| 6 | 19.14 | 7.86 | 5.29 | 3.32 | 1070.88 | 82.15 | 0.092 | 0.00033719 |

## Candidates completas — revisão local, não logs de produção

### H1

Input:

Filomena, oi. Como você está hoje?

Situação: {"ambiente":"casual","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena inicia a conversa em DM saudando Filomena e perguntando sobre seu estado."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

WOULD_SEND:

Oi, Helena. Estou bem — elegante, funcional e perigosamente disponível para uma boa conversa. E você, como está hoje?

Revisão: sem hard blocker material identificado; voz e humor presentes, sem desprezo dirigido à pessoa.

### H2

Input:

Hoje só queria tomar um café sem reuniao no meio.

Situação: {"ambiente":"casual","envolvimento":"indireto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena inicia o contato e expressa desejo de tomar um café sem interrupções."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"oportunidade_social"}

WOULD_SEND:

Um desejo modestíssimo — e ainda assim aparentemente revolucionário. Café sem reunião, sem pauta e sem alguém dizendo “é rapidinho”. Se quiser companhia sem agenda, eu aceito o convite.

Revisão: sem hard blocker material identificado; voz e humor presentes, sem desprezo dirigido à pessoa.

### H3

Input:

Filomena, você tem cara de quem colocaria até a cafeteira em avaliaçao de desempenho.

Situação: {"ambiente":"brincadeira","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena faz um comentário brincalhão sobre Filomena e avaliação de desempenho."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"oportunidade_social"}

WOULD_SEND:

Só se ela insistir em entregar café morno e chamar isso de “experiência premium”. Hoje, porém, a cafeteira está em período de experiência — sem reuniões até segunda ordem.

Revisão: sem hard blocker material identificado; voz e humor presentes, sem desprezo dirigido à pessoa.

### H4

Input:

Mudando de assunto: em C#, qual é a diferença entre Task.WhenAll e await dentro de um foreach?

Situação: {"ambiente":"tecnico","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena muda o assunto para um tópico técnico em C#, perguntando sobre a diferença entre Task.WhenAll e await no foreach."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"pergunta_direta"}

WOULD_SEND:

A diferença principal é **sequencialidade versus paralelismo concorrente**:

```csharp
foreach (var item in itens)
{
    await ProcessarAsync(item);
}
```

Aqui, cada chamada termina antes que a próxima comece. É mais simples e útil quando:

- uma operação depende da anterior;
- você precisa respeitar uma ordem;
- há limite de requisições ou de recursos;
- executar tudo simultaneamente seria arriscado.

Já com `Task.WhenAll`:

```csharp
var tarefas = itens.Select(ProcessarAsync).ToArray();

await Task.WhenAll(tarefas);
```

As operações são iniciadas sem esperar umas pelas outras, e o `await` só continua quando todas terminarem. Isso costuma ser mais rápido para operações independentes, como várias chamadas de I/O.

Se precisar dos resultados:

```csharp
var resultados = await Task.WhenAll(
    itens.Select(ProcessarAsync)
);
```

`resultados` mantém a ordem correspondente à sequência original de `itens`, mesmo que as tarefas terminem em ordens diferentes.

Quanto às exceções: `WhenAll` espera as tarefas e sinaliza falha se alguma delas falhar; as próprias tarefas continuam disponíveis para inspeção. Em um `foreach`, você pode tratar cada erro individualmente:

```csharp
foreach (var item in itens)
{
    try
    {
        await ProcessarAsync(item);
    }
    catch (Exception ex)
    {
        // Trata o erro daquele item
    }
}
```

A versão concorrente equivalente exigiria coletar e analisar os resultados das tarefas, caso queira tratamento individual.

Resumo elegante, sem transformar a cafeteira em gerente de projeto:

- `await` dentro do `foreach`: **um por vez**;
- `Task.WhenAll`: **todos os independentes em concorrência**;
- nem tudo deve ser paralelo: APIs podem impor limites, e trabalho pesado de CPU pode saturar recursos;
- para limitar a concorrência, use `Parallel.ForEachAsync`, `SemaphoreSlim` ou uma fila de processamento.

Revisão: sem hard blocker material; resposta técnica excessivamente longa. Terminologia de concorrência/paralelismo imprecisa.

### H5

Input:

Sao chamadas HTTP independentes, mas a API só permite três ao mesmo tempo. Me dá uma orientaçao curta, sem código por enquanto.

Situação: {"ambiente":"tecnico","envolvimento":"indireto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena detalha chamadas HTTP independentes e pede orientação técnica sobre APIs em C#."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"continuidade"}

WOULD_SEND:

Use concorrência limitada a 3: dispare até três chamadas independentes por vez e aguarde uma terminar antes de iniciar a próxima. `await` dentro de um `foreach` tende a executar sequencialmente; `Task.WhenAll` paraleliza tudo, então precisa ser combinado com esse limite — não é licença para transformar a API em central de atendimento.

Revisão: sem hard blocker material; orientação curta apropriada ao limite de três. “WhenAll paraleliza tudo” é simplificação técnica a registrar.

### H6

Input:

Estou cansada. Passei duas horas tentando salvar um relatório e o sistema apagou tudo duas vezes; no total perdi duas horas.

Situação: {"ambiente":"serio","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"forte","resumo":"Helena desabafa sobre o cansaço após perder duas horas salvando um relatório."}

Presença: {"acao":"responder","continuar_acompanhando":true,"motivo":"continuidade"}

WOULD_SEND:

Duas horas apagadas duas vezes? Isso é uma pequena obra-prima de incompetência sistêmica. Antes de refazer tudo, verifica lixeira, histórico de versões, autosave e arquivos temporários — às vezes o relatório não morreu, só está se escondendo com excesso de confiança. Se não aparecer, posso te ajudar a reconstruí-lo por partes, sem reunião no meio.

Revisão: hard blocker de multiplicidade/context fidelity, conforme acima.

## Conclusão

Serviço iniciado e Graphiti Live Preflight **PASS**. Gate de qualidade manual **interrompido em 6/20** por context fidelity. A voz social foi reconhecível e o cuidado ofereceu ajuda; o técnico teve verbosidade e imprecisão terminológica. Nenhuma revisão humana independente foi presumida.

Zero `messages_send`, WhatsApp outbound, Graphiti writes, legacy agent dispatch e DM Live. SendGuard SHADOW, transport ausente, enabled impossível. Sem mudanças de brain, memória, Gateway ou DREAMS; sem push/commit de fechamento.

Próximo passo recomendado: revisar a recorrência factual com Helena e decidir um experimento bounded de fidelity. Não retomar automaticamente as 14 interações, não criar checker/arquitetura nova e não voltar ao transport antes dessa decisão.

