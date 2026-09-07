# Typed-flow slice 3 — leitura explícita de memória

Projeto versão **1**, graph versão **3**. Nenhuma migração de UUID/namespace.

## Contrato

`Consultar Memória` (`memory`) executa somente pelo fio `exec`; publica `memories: Memória[]` e segue por `then` após sucesso. Dados não disparam operações. Namespace vem exclusivamente da sessão, não do node. Sem o node executado e conectado, a Instrução não recebe memória.

- `persons`: Pessoa, Pessoa?, Pessoa[] ou Pessoa?[]; nulls removidos, UUIDs canônicos deduplicados.
- `query`: Texto, ou `settings.queryText` local; nunca ambos.
- Exato + pessoa: exige pessoa válida.
- Semântico + conversa: exige consulta não vazia.
- Híbrido + pessoa: exige pessoa; consulta é opcional.
- Híbrido + conversa: exige consulta; pessoas são opcionais para complementar o exact recall.
- `activeOnly=true` nesta versão. Histórico inválido não está disponível.
- Categorias estruturais: limite, apelido, relação, fato, não classificada. Sem classificação por LLM.

`Memória` expõe apenas `conteudo: Texto` e `categoria: Enum`. `Obter Propriedade` projeta ambos, inclusive listas. Metadados técnicos permanecem fora do property picker. Na fronteira da Instrução, `sobre` preserva sujeitos como nomes de apresentação e referências efêmeras distintas. Nome nunca resolve identidade; nome ausente mantém uma referência, não um UUID.

Pessoa usa o `stableId` estrutural validado e o UUIDv5 já existente, derivado do namespace atual. Representações WhatsApp PN/device/c.us usam o normalizador existente; LID permanece LID sem inferência de PN. Telegram aceita IDs de usuário estruturais. Não há identidade ou recall social global. Metadata explícita de namespace/participant UUID inconsistente é rejeitada.

## Política e erros

O runtime e o adapter verificam isolamento e validade temporal. Resultados de outro namespace falham fechado. Endpoint Person legado/ambíguo ou UUID inconsistente é omitido pelo adapter, sem merge por nome.

Prioridade determinística: limite → apelido estabelecido → relação → fato → não classificada; exact antes de semantic na mesma categoria; desempate por fact ID. Dedupe por ID. Budgets padrão: 8 itens/1800 caracteres de conteúdo (unidades UTF-16, como `string.Length`). Não é um orçamento de tokens nem do envelope JSON inteiro. Itens são inteiros: se o próximo item prioritário exceder o budget restante, encerra-se a seleção, sem substituí-lo por um fato menor de prioridade inferior.

Apelido exige `USED_ADDRESS_TERM`, endpoints canônicos Agent↔Person, termo não genérico e pelo menos dois `evidence_run_ids` distintos no fato. Não promove uso isolado. Confirmação explícita de nome não tem ainda um contrato de evidência estruturado nessa API e não foi inventada aqui.

Sucesso sem fatos retorna `[]`. Backend indisponível, timeout, erro MCP, formato inválido ou identidade inválida produzem node failed e interrompem o caminho. Não há fallback para texto, histórico ou resultados parciais.

## Adapter

Assembly `Filomena.Memory`, SDK oficial MCP **2.2.0**, conexão HTTP lazy e reutilizada. Interface Core `IMemoryReader`; runtime não importa MCP/FalkorDB/EntityEdge.

Somente:

```text
get_entity_neighborhood(group_id=namespace, entity_uuid=PersonUUID, active_only=true, limit=50)
search_memory_facts(group_ids=[namespace], query=texto, active_only=true, max_facts=50)
```

O MCP instalado pode envolver o resultado em `{result: ...}`. `error` é falha mesmo sem `isError`. O exact atual limita a 50 candidatos, ordenados pelo backend por recência; prioridade/budget são aplicados sobre esse conjunto. **Não há garantia de encontrar um limite mais antigo fora desses 50 candidatos.** Entidade inexistente retorna erro na API atual; não fingimos que isso é uma busca vazia bem-sucedida.

## Fixture testável

Importe `tests/fixtures/typed-flow-slice3.json`. O desenho tem DM e grupo → Acumular(2) → Histórico → Memória → Instrução fake. Projetar remetente do lote alimenta memória. A Instrução tem três inputs separados: `mensagens`, `historico`, `memorias`.

```text
[DM] ───────┐
           ├→ [Acumular] → [Histórico] → [Consultar Memória] → [Instrução fake]
[Grupo] ───┘       │                             ↑                  ↑
                   └→ [Propriedade: remetente] ──┘                  │
                   lote e histórico ───────────────────────────────┘
```

O botão Run standalone continua sendo do slice 1. O runtime por sessão é exercitado pela API fake existente, não por transporte real. Exemplo PowerShell com o playground que **você** iniciar:

```powershell
$baseUri = 'http://127.0.0.1:18793'
$headers = @{ 'X-Filomena-Playground' = '1' }
$project = Get-Content tests/fixtures/typed-flow-slice3.json -Raw | ConvertFrom-Json
$ns = 'whatsapp-dm-5511944558218-s-whatsapp-net'
$actor = 'whatsapp:5511944558218@s.whatsapp.net'
$body = @{
  project = $project
  responses = @{ ai = @('{"ok":true}') }
  memories = @(
    @{ id='b'; conversationNamespace=$ns; content='não brincar com X'; category='limite'; actors=@($actor) }
    @{ id='f'; conversationNamespace=$ns; content='gosta de Persona 4 Golden'; category='fato'; actors=@($actor) }
  )
} | ConvertTo-Json -Depth 64
$run = Invoke-RestMethod "$baseUri/api/flow/typed/sessions" -Method Post -Headers $headers -ContentType application/json -Body $body
$route = @{ channel='whatsapp'; accountId='test'; nativeId='5511944558218@s.whatsapp.net'; kind='dm' }
foreach ($id in @('m1','m2')) {
  $event = @{ session=$route; message=@{ messageId=$id; timestamp=[DateTimeOffset]::UtcNow; direction='inbound'; value=@{
    texto='oi'; remetente=@{nome='Helena';stableId=$actor}; canal=@{nome='DM';nativeId=$route.nativeId}; reply=$null
  } } } | ConvertTo-Json -Depth 16
  Invoke-RestMethod "$baseUri/api/flow/typed/sessions/$($run.runtimeId)/receive" -Method Post -Headers $headers -ContentType application/json -Body $event
}
Invoke-RestMethod "$baseUri/api/flow/typed/sessions/$($run.runtimeId)/drain" -Method Post -Headers $headers -ContentType application/json -Body (@{session=$route}|ConvertTo-Json)
Invoke-RestMethod "$baseUri/api/flow/typed/sessions/$($run.runtimeId)"
```

Trace retorna `MemoryQueryStarted`/`MemoryQueryCompleted`, modo/escopo/counts/categorias/duração — sem conteúdo, query ou prompt por default. Preview de contrato da Instrução mostra os inputs separados; payload efetivo seguro é verificado pelo fake completion nos testes Core, não exposto indiscriminadamente pelo snapshot HTTP.

Para leitura real no playground standalone: configure `FILOMENA_GRAPHITI_MCP_ENDPOINT=http://127.0.0.1:8000/mcp` no processo e crie simulação com `usePersistentMemory:true`, sem `memories`. Isso continua usando completion fake; não ativa transporte, escrita ou envio. Aplicação com composição própria pode injetar `IMemoryReader` em `LocalPromptPlayground.Build`.

## Fora deste slice

Escrita/ingestão, migração de Persons legadas, memória global, Graphiti Core, transporte, envio, personalidade final e persistência de estado operacional. Histórico/Acumular/state snapshots continuam exatamente no runtime do slice 2. Captura passiva continua com seu responsável atual.

Slice 4 ainda precisa de escopo aprovado: geração/envio e personality assets não foram adiantados.
