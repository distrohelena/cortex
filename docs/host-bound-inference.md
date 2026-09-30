# Inferência vinculada ao host — primeira fatia local

Data: 2026-09-27. Implementação local, não ativada em nenhum composition root operacional.

Relacionados: [especificação](superpowers/specs/2026-09-27-intelligence-platform-evolution-design.md), [plano](superpowers/plans/2026-09-27-host-bound-inference.md). O [plano da oficina](superpowers/plans/2026-09-26-evaluation-workbench.md) e sua aceitação PostgreSQL continuam pendentes e independentes.

## O que foi implementado

- `InferenceScope` separa ownership operacional de ID do projeto de flow.
- `HostInferenceAuthority` emite grants opacos locais com principal, scope e perfis permitidos; `Bind` não congela autoridade. Cada tentativa revalida a concessão.
- `HostInferenceProfile` fixa node, provider/model solicitado, esforço, limite de saída e schemas aprovados, com cópias defensivas. Inputs/prompts não selecionam endpoint/credencial.
- `HostBoundFlowCompletion` chama somente o adapter host-owned registrado. Não há retry, fallback, tools ou sender implícitos.
- `SessionCompletionContext` leva a identidade/revisão do batch real à fábrica de completion; constructors anteriores continuam funcionando.
- `HostBoundSessionCompletionFactory` exige binding enumerado de SessionKey completo + ProjectId/Version/Revision. Não existe fallback para revisão mais recente ou grant global.
- Execução normal, revisão de participação e preview usam o mesmo ponto de criação por contexto capturado.

Arquivos de produção novos: `src/Filomena.Core/Inference/*.cs` e `Flows/Sessions/SessionCompletionContext.cs`. Alterações existentes limitadas à fábrica em `SessionFlowRuntime.cs` e aos seus três callers. Sem novos assemblies/pacotes/stores.

## Semântica de autoridade

Grant, scope e configuração de perfil são criados pelo host, não por HTTP/modelo/projeto. Grant de outra authority, scope incorreto, perfil ausente, revisão de sessão diferente ou node futuro são negados. Compartilhar principal ou modelo não compartilha concessão.

Admissão da tentativa e revogação são ordenadas por lock curto. Revogação anterior à admissão bloqueia; uma tentativa já admitida pode terminar. Não há lock durante I/O, nem promessa de desfazer requisição externa. Um batch aguardando execução não adquire permissão permanente por estar pinado.

Isso **não é autenticação externa, isolamento contra código arbitrário em processo ou garantia multi-produto de todos os stores**. Os roots antigos e outros seams de inferência não foram migrados automaticamente. Memória, interpretação contextual por interfaces próprias, credenciais live e dados continuam sujeitos aos contratos atuais e às fases seguintes.

## Validação e observação

Inferência avulsa exige validador host-owned. A fixture de aceitação usa objeto fechado e enum; `FlowDomain.Check` sozinho não rejeita propriedades extras, portanto a fixture também verifica o conjunto de campos. Não foi criado validador universal.

No flow, `ForRuntime` preserva a validação/correção existente: o decorator retorna `AdapterReturnedPendingValidation`, nunca `Completed`. O runner/Boundary determina a validade final. Isso evita suprimir o retry de JSON que já ocorre depois de `CompleteAsync`.

Os dois detectores existentes compartilham NodeId, mas usam schemas diferentes. O perfil permite uma lista **explícita e copiada** de schemas adicionais aprovada pelo host. Schema futuro continua negado; não há wildcard ou mudança de Boundary.

Observações incluem scope/principal, run/step/attempt, revisões, duração monotônica e código/estágio, sem prompt, input, candidate, segredo ou mensagem privada de exception. Modelo solicitado não é modelo observado. Tokens, custo e modelo observado ficam desconhecidos neste adapter `Task<string>`; não são registrados como zero.

Erro HTTP/timeout conserva sua categoria para os consumidores existentes, mas remove texto/inner exception privados. Falha/cancelamento após invocar o adapter não comprova ausência de consumo. Saída null ou inválida não é sucesso. Falha do sink não reenvia a chamada e fica visível em `ObservationFailed`, tanto no completion quanto na authority.

O sink é process-local e não é ledger financeiro/audit durável. Não há budget hierárquico nem reserva monetária nesta fatia.

## Evidência executada

Tudo abaixo usou adapters em memória, fixtures controladas e arquivos temporários locais. Não houve modelo real, banco ou serviço iniciado.

| Verificação | Resultado |
|---|---|
| Baseline SessionFlowRuntime/BoundaryRuntime/ShadowRuntime | 23 passes antes das alterações |
| Novos testes + mesmas regressões focadas | 74 passes; 51 casos novos e 23 regressões |
| Build Core e Llm, `--no-restore` | Passaram com zero warnings/errors |
| Formatação whitespace dos arquivos próprios | Verificada sem mudanças |
| Diff whitespace dos quatro arquivos existentes modificados | Sem erros; Git avisa conversão LF/CRLF conforme configuração existente |
| Suite Core offline ampliada | Não está totalmente verde: falhas existentes listadas abaixo; relatório TRX no ledger local |

Comandos focados:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~HostInference|FullyQualifiedName~HostBound|FullyQualifiedName~SessionFlowRuntimeTests|FullyQualifiedName~BoundaryRuntimeTests|FullyQualifiedName~ShadowRuntimeTests' --verbosity quiet
dotnet build src/Filomena.Core/Filomena.Core.csproj --no-restore
dotnet build src/Filomena.Llm/Filomena.Llm.csproj --no-restore
```

A suite ampliada excluiu `*Live*`, `BoundarySemanticCorpusTests` e `DetectionStrategies`, após leitura dos pontos que criam HttpClient. Não equivale a rodar a solução inteira. A suite Llm referencia App; não foi executada. A caracterização do brain foi feita diretamente em Core com o grafo real.

### Preservação do brain

`docs/manual-shadow-project-v3.json` manteve SHA-256 `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

`HostBoundBrainCompatibilityTests` compara o mesmo artifact antes/depois da composição, alterando apenas o estado inicial na cópia em memória para setup:

- direct nos três estados sem Decidir Presença;
- indireto/nenhum continuam no ramo de presença;
- MemoryItem sintético passa por compilação real de Boundary;
- dois detectores reais com respostas controladas;
- Block inicial regenera e reavalia; Block remanescente não chega a WOULD_SEND;
- Shadow não confirma fala/transição; não há transport fornecido;
- review/preview recebem novos run IDs e não readmitem inbound nem criam outcomes ordinários adicionais.

São testes estruturais de composição, não avaliação de voz, factualidade de modelos ou memória live.

### Falhas não corrigidas fora do escopo

As mesmas duas classes já constavam como problema no plano da oficina anterior; nem suas implementações nem seus testes foram alterados nesta fatia. A baseline focada inicial não incluía essas classes; a execução ampliada atual confirmou 19 falhas, sem alegar que foi executada antes do patch:

- `FlowBindingValidatorTests.ValidatesUnusedGrantCharacterSpaceAndMediatedProtectorReferences`;
- `FlowBindingValidatorTests.RejectsIngressGraphsCarryingCharacterIds`;
- `FlowBindingValidatorTests.RequiresCharacterGraphReferenceToBeACharacterGraphOwnedByThatCharacter`;
- `FlowBindingValidatorTests.RequiresPrivateSpaceOwnerCharacterToExist`;
- `FlowBindingValidatorTests.RejectsDuplicateDestinationIdsWithinAnEnvironment`;
- `FlowCompilerTests.MalformedDirectObjectsReturnSafeIssues`: 13 casos (`node`, `dataEdge`, `character`, `controlEdge`, `fieldPath`, `space`, `source`, `field`, `graph`, `conversation`, `environment`, `profile`, `destination`);
- `FlowCompilerTests.OversizedImportIsRejectedBeforeAllocatingItsUtf8Payload`.

A última execução ampliada terminou com **589 passes e 20 falhas (609 testes)**: as 19 acima e `ConversationTurnManagerTests.ProcessesConversationsIndependentlyWithoutMixingBuffers`. Esta última passou na reexecução individual (1/1). O stack aponta para `RecordingArbiter.DecideAsync`, linha 617: o helper compartilha `Queue<TurnDecision>` e faz `Count` seguido de `Dequeue` sem sincronização, compatível com a falha intermitente `Queue empty` sob concorrência. O teste e o manager já tinham alterações anteriores, preservadas; não foram corrigidos nesta fatia. A reexecução individual não apaga a falha da suíte ampliada.

Não anunciar build/testes completos da plataforma verdes. A aceitação PostgreSQL/Docker da oficina também não foi executada.

## Decisões e desvios registrados

1. Livro de progresso via apply_patch/PowerShell em vez dos scripts bash que pressupõem commits e headings diferentes. Custo: bookkeeping manual; evidências e rulings preservados em `.superpowers/sdd/2026-09-27-host-bound-inference/`.
2. Manter main, sem worktree, commits ou revisão independente automática, conforme autorização explícita. Custo: revisão final é do próprio autor, não uma segunda revisão independente.
3. Validação do flow permanece no runner; observação intermediária não finge sucesso final. Custo: consumidor deve correlacionar com resultado/trace do runtime.
4. Binding de sessão enumerado substitui resolver livre. Custo: registrar explicitamente cada revisão autorizada; não existe hot fallback.
5. Incluir os callers review/preview, descobertos além da execução normal. Custo: duas linhas adicionais em caminhos existentes; ambos exercitados localmente.
6. Variantes explícitas de schema preservam a composição dos detectores. Custo: novos contratos exigem alteração host-owned da allowlist, não passam automaticamente.
7. Não corrigir as 19 falhas de outro slice nem iniciar App para executar sua suite. Custo: checkout global continua sem aceitação completa; não há deploy autorizado por estes testes.

Autorrevisão final: escopo/revogação, cópias defensivas, contexto concorrente, schema variants, unknown cost, correction/regeneration, categorias de falha e preview. Corrigidos com RED→GREEN: saída null, visibilidade do sink e preservação sanitizada de HTTP/timeout. Nenhum reviewer independente foi acionado.

## Estado final e próximo passo

Primeira fatia implementada e validada localmente nos caminhos descritos, **não ativada**. Sem alterações de banco, Graphiti, dados, prompts ou entrega; sem commit/push.

Próxima decisão é qual fatia aprovar: retomar a aceitação offline/isolada pendente da oficina quando a infraestrutura estiver disponível ou planejar ownership dos stores. Não ligar novos produtos a dados legados antes dessas provas. Não prometer suporte completo a Atores, Dota e Damma a partir desta implementação.
