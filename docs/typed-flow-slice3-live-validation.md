# Slice 3 — Graphiti real, 2026-09-08

## Resultado: parcial, não aprovado integralmente

O node executa contra Graphiti real. O fixture original passou com reader real e
completion fake. Falta o fact `PLEX73`: ele existe apenas no episódio bruto.
Nenhuma escrita, reingestão, migração, chamada de geração Gemma ou operação de
Gateway foi realizada. A busca semântica usa o embedding local normalmente.

## Conexões e diagnóstico

| Camada | Endpoint/processo | Evidência atual |
|---|---|---|
| C# → Graphiti | MCP Streamable HTTP, `http://127.0.0.1:8000/mcp`, container `filomena-graphiti-mcp` | `search_memory_facts` e `get_entity_neighborhood` responderam |
| Graphiti → FalkorDB | compose `redis://falkordb:6379`, container `filomena-falkordb`; host `127.0.0.1:6380` | `PING=PONG`, consultas read-only e exact recall funcionam |
| Graphiti → embeddings | `http://host.docker.internal:1234/v1`, `text-embedding-nomic-embed-text-v2-moe`, 768 dimensões | semantic recall real encontra Drift e DM |
| Authorization/SpiceDB | não participa deste read adapter | o cliente chama somente as duas tools Graphiti |

Nos logs históricos de 2026-09-07, às 15:50–15:52 e 17:33 (horário do log),
`openai._base_client` registra retries imediatamente antes de
`graphiti_mcp_server: Error searching facts: Connection error.`
O retorno MCP anterior era um envelope válido contendo esse erro: portanto não
era falha de conexão C# → MCP. Na busca, o código vendorizado chama
`embedder.create` para o vetor da consulta. Com o reranker desabilitado, os sinais
apontam para Graphiti → API OpenAI-compatible local de embeddings.

O erro interno preservado é somente **`Connection error.`**. Não há traceback
de socket que permita afirmar retrospectivamente se foi connection refused,
bind incorreto ou outra falha de rede. Não afirmamos ter corrigido essa causa:
ela não se reproduziu com os serviços atualmente ligados.

`GET /health` retorna 200 com JSON constante (`graphiti_mcp_server.py`): comprova
processo HTTP alcançável, não banco/embeddings operacionais. Nesta rodada usamos
operações reais de leitura para comprovar backend utilizável, sem criar outro
sistema de health monitoring ou mudar Graphiti Core.

## Isolamento e dados reais

| Contexto | Namespace | Positivo | Negativos |
|---|---|---|---|
| Drift | `whatsapp-group-5511981042361-1589823314-g-us` | DRIFT42 encontrado | sem PLEX73/PRIVATE91 |
| Plex | `whatsapp-group-120363318973251043-g-us` | PLEX73 **ausente nos facts** | sem DRIFT42/PRIVATE91; respostas vazias |
| DM Helena | `whatsapp-dm-5511944558218-s-whatsapp-net` | PRIVATE91 encontrado | sem DRIFT42/PLEX73 |

Cada namespace foi consultado separadamente para cada um dos três códigos.
Não houve consulta multi-namespace. Os negativos do Plex, embora passem, não
substituem o positivo ausente.

Auditoria `GRAPH.RO_QUERY`, sem alterações:

- Drift: fact `95fdb20c-dc66-4ce7-8e4c-3bd4a29bed41` tem DRIFT42, ativo e ligado
  à Person canônica `b4964ff3-b8e0-5a95-8e82-517ab0dc88ee`.
- DM: fact `3a32ad64-6a58-40ca-893a-48233d9bdcc7` tem PRIVATE91, ativo e ligado
  à Person canônica `87943b77-4bb0-555c-84a7-7443b84a674e`.
- Plex: episódio `7beb242f-34fb-584e-814b-87010e2e69e2` contém PLEX73,
  com `valid_at=2026-09-04T18:52:04+00:00`. A Person
  `6824b555-ae10-53ca-958f-99f4cc832d23` existe, mas tem **zero RELATES_TO**.
  Não existe RELATES_TO com PLEX73 nesse namespace. Não é um descarte do reader:
  a própria API devolve zero facts.

## Exact, híbrido e runtime

- Exact: Drift/DM retornam fatos ativos atribuídos aos UUIDs canônicos acima.
- Híbrido Drift: chama exact + semantic no mesmo namespace, confirma sobreposição
  real e dedupe por fact ID, atribuição Helena e budgets de 8 itens/1800 caracteres.
  Resultado observado: 2 facts finais, 1 fact sobreposto deduplicado, 0 limites.
- Sem limite explícito adequado nessa amostra: precedência de limites continua
  coberta pelos testes fake, não declarada como demonstrada em dados reais.
- Vazio: exact do participante Plex retorna `[]`. Um endpoint local reservado
  sem listener gera exception no mesmo reader, não `[]`; nenhum serviço real
  foi derrubado para simular indisponibilidade.
- `tests/fixtures/typed-flow-slice3.json` foi usado **sem alterar o JSON**:
  duas mensagens simuladas → Acumular → Histórico → propriedade remetente →
  memória real → completion fake. A consulta textual original `Persona` permanece;
  a parcela exact recupera o canário Drift.
- O payload conserva três inputs distintos (`mensagens`, `historico`, `memorias`).
  UUID do participante e ID do fact não entram no payload. Nenhuma mensagem é enviada.
  O fixture recebeu 2 memórias reais.

## Comandos/testes

```powershell
$env:FILOMENA_TEST_GRAPHITI = '1'
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore --filter FullyQualifiedName~GraphitiLiveRead --logger 'console;verbosity=normal'
```

**7 pass / 1 fail**: somente o positivo PLEX73. O canário antigo também continua
falhando pelo mesmo dado ausente; não removemos nem afrouxamos essa exigência.
As novas verificações são opt-in, nunca criam os próprios dados reais.

Offline (variável opt-in ausente):

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore --verbosity quiet
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
```

Core **198 pass**; backend **43 pass / 9 live skipped**; JS/editor **72 pass**,
incluindo import/export roundtrip dos três fixtures. O teste preexistente
OpenClaw `InvalidActorIdentityFailsClosedInsteadOfThrowing` permanece fora do escopo.
Build `Filomena.slnx`: 0 warnings / 0 errors. Formatting do novo teste e
`git diff --check`: passaram. Somente teste e relatório foram adicionados;
nenhum código de produção ou configuração foi alterado.

## Próximo passo autorizado separadamente

Investigar/reprocessar somente o episódio Plex acima para produzir os fatos
faltantes e repetir o positivo PLEX73. Isso exige escrita/extração; **não foi feito**.
Não é necessário reimportar chats inteiros. Manter o limite exact de 50 candidatos;
sem paginação nova, merge de Persons legadas ou alterações de identidade.
