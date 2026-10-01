# Cortex Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development only after explicit orchestration approval. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renomear seletivamente a fundação compartilhada para Cortex, mantendo a aplicação Filomena e todos os seus contratos persistidos e comportamentais.

**Architecture:** Um repositório e uma solução agregadora. Assemblies Cortex contêm mecanismos compartilhados; Filomena.App e o restante específico de Filomena.Llm consomem esses mecanismos. Identidade e leitura de memória legadas permanecem explicitamente compatíveis com Filomena; a renomeação não implementa ownership multiaplicação.

**Tech Stack:** C#/.NET 10, MSBuild, xUnit v3, testes Node existentes; sem novas dependências.

**Spec:** [Cortex foundation design](../specs/2026-09-30-cortex-foundation-design.md), aprovada para elaboração deste plano.

**Execution update, 2026-10-01:** implementation authorized and performed inline.
Tasks 1–5 are implemented; Task 6 ran the final verification and self-review.
Global acceptance is not green because pre-existing flow-validation and missing
frontend/settings test dependencies remain. See [results](../../cortex-migration-status.md)
for exact outcomes and the narrow typed-summary extraction adjustment.
The unchecked planning checklist below is retained as the original execution brief,
not a claim that every acceptance gate passed.

## Global Constraints

- Manter um repositório e a pasta física `C:\dev\filomena` nesta migração.
- `Filomena.App` permanece `Filomena.App`; não mudar brain, prompts, modelos ou operação.
- Nenhuma mudança de UUIDs, chaves de dedupe, namespaces Graphiti/SpiceDB, session IDs, referências, nomes de journals, cursors, receipts ou conteúdo serializado só por causa de namespaces C#.
- Manter bytes, versões e checksums de migrations; conservar os `LogicalName` legados.
- Manter variáveis de ambiente, nomes de seção de configuração, endpoints e contratos wire atuais.
- Não substituir DLLs em uso, mover a instalação, reiniciar a Filomena ou alterar scripts/configs privados ativos.
- Nenhuma credencial ou permissão passa a vir de project JSON.
- Sem repositório novo, pacotes publicados, novos serviços, banco novo, alteração Graphiti, implementação Damma/Dota, tuning, chamadas pagas ou deploy.
- Trabalhar inline no checkout solicitado; preservar `workspace/DREAMS.md` e trabalho alheio. Sem push. Commits delimitados somente com autorização aplicável; aprovação deste plano não presume autorização de deploy.

## Review Focus

1. Identidade incorporando o nome Filomena: UUIDs e namespaces devem ser idênticos antes/depois; Task 1 fixa vetores, Task 2 conserva compatibilidade explícita.
2. Recursos SQL não encontrados após troca de assembly: catálogo deve carregar nove migrations com checksums anteriores; Task 5.
3. Binários antigos mascarando referência quebrada: build/teste em diretório de artefatos novo e inspeção do grafo de assemblies; Tasks 2–6.
4. Testes que abrem modelos/bancos por ambiente herdado: seleção explícita de testes offline e inspeção de opt-ins, nunca suíte live inteira; Tasks 1 e 6.
5. Ferramentas fora da solução e caminhos de scripts: compilar `ImportLiveContext`/`ReconcileNotSent`, verificar referências de CLI e preservar recursos da App; Tasks 2, 5 e 6.

## Estado inspecionado e limites

Base `17ad29a`. A especificação e a atualização de acompanhamento são mudanças
documentais locais preexistentes. Nenhum build/teste executado para escrever este
plano. O índice consultado tem 50 projetos e nenhum Filomena/Cortex; inspeção por
arquivos, sem alegação de grafo completo.

`MemoryResultPolicy.ValidateQuery`, `SessionFlowRuntime.Memory.cs`,
`GraphitiMemoryReader` e `ShadowGraphitiProbe` ainda pressupõem identidade Filomena.
Este plano **não** converte essas políticas em políticas Damma. A inferência
host-bound é a fronteira reutilizável já disponível; memória para outro produto
continua condicionada ao [plano de ownership](2026-09-28-host-bound-memory-read.md).
Não fornecer um novo entrypoint genérico que use silenciosamente a memória Filomena.

## Estrutura final e decisões de movimento

| Origem | Destino / decisão |
|---|---|
| `src/Filomena.Core` | `src/Cortex.Core`, namespace geral `Cortex.Core.*` |
| `src/Filomena.Core/Identity/FilomenaIdentity.cs` | `src/Cortex.Core/Compatibility/Filomena/FilomenaIdentity.cs`; manter nome de tipo e namespace `Filomena.Core.Identity` como exceção de compatibilidade explícita, sem copiar implementação |
| `src/Filomena.Llm/ProviderFlowCompletion.cs`, `FlowInstructionPrompt.cs`, `OpenAi/`, `LmStudio/` | Mesmos caminhos relativos em `src/Cortex.Inference`, namespaces `Cortex.Inference.*` |
| `src/Filomena.Llm/SystemOne/SystemOneClient.cs`, `SystemOneFlowCompletion.cs` | `src/Cortex.Inference/SystemOne/`; mesmos contratos/protocolos |
| `src/Filomena.Llm/Agent/`, `Arbitration/`, `Context/`, `SystemOne/SystemOneParticipationCompletion.cs` | Permanecem `Filomena.Llm`; participação e prompts não são adapters genéricos |
| `src/Filomena.Memory`, `src/Filomena.OpenClaw` | `src/Cortex.Memory`, `src/Cortex.OpenClaw` |
| `src/Filomena.Evaluation`, `src/Filomena.Evaluation.Cli` | `src/Cortex.Evaluation`, `src/Cortex.Evaluation.Cli` |
| `tests/Filomena.Core.Tests`, `tests/Filomena.OpenClaw.Tests`, `tests/Filomena.Evaluation.Tests` | Projetos equivalentes `Cortex.*.Tests`; testes legados de personagem permanecem identificáveis pelos nomes das classes |
| `tests/Filomena.Llm.Tests` | Mantido para integração da aplicação; adicionar `tests/Cortex.Inference.Tests` para adapters extraídos |
| `Filomena.slnx` | Mantida e atualizada como solução agregadora; não renomear pasta nem solução nesta fatia |

Core permanece sem referência a adapters/App. Cortex.Inference/Memory/OpenClaw
referenciam Cortex.Core. Cortex.Evaluation referencia Core e Inference, **não**
Filomena.Llm. Filomena.Llm referencia Core e Inference. Filomena.App compõe ambos.

A exceção `Filomena.Core.Identity` permite preservar callers e semântica sem
criar dependência Core → App ou outra biblioteca apenas para uma classe. Não é
uma API de identidade Cortex nem declaração de desacoplamento completo da memória.
A remoção dessa exceção depende da fatia de ownership, não de um replace global.

## Task 1: Baseline reproduzível e guardas de compatibilidade

**Files:** criar `tests/architecture/cortex-foundation.test.mjs`,
`tests/architecture/cortex-compatibility-baseline.json`; ampliar
`tests/Filomena.Core.Tests/IdentityCompatibilityTests.cs` e
`tests/Filomena.Evaluation.Tests/MigrationCatalogTests.cs` somente onde faltarem
assertions; registrar evidência em `docs/cortex-migration-status.md`.

**Interfaces:** baseline contém hashes SHA-256 de arquivos aprovados existentes e
checksums SQL, com caminhos; testes não regeneram automaticamente a baseline.
O teste arquitetural consome arquivos `.csproj` e assemblies publicados nas tasks
seguintes; não precisa de banco, modelo ou transport real.

- [ ] Inspecionar Git, `.csproj`, targets/imports MSBuild, testes selecionados e opt-ins. Registrar referências atuais de projetos, `InternalsVisibleTo`, recursos, reflexão, serializers e caminhos de scripts. Resolver ocorrências, não presumir que toda string `Filomena` é namespace.
- [ ] Capturar hashes do checkpoint dos `docs/manual-*.json`, `evaluation/conversation-quality-v1/*.project.json`, cláusulas existentes, `workspace/SOUL.md` se presente e migrations 001–009. Hash do arquivo ausente não pode virar hash vazio. Não ler/copiar dados privados live.
- [ ] Fixar vetores literais já presentes em `IdentityCompatibilityTests`: `AgentUuid` da fixture é `7da4d33e-c475-5446-bd7a-0c0f0bd25178`; inbound e outbound permanecem `827e98fb-1456-524d-ab68-19a8b61a8049` e `8770f1d3-045e-520a-b43e-23f9792edf61`. Preservar também o teste de compatibilidade do account; ele não comprova isolamento entre contas.
- [ ] Criar guardas de futuro estado: Core sem referência a Filomena.App/Llm/Memory/OpenClaw; Inference sem App/Llm; recursos SQL antigos disponíveis. Antes das renomeações, esperar falha específica por projeto Cortex ausente, não erro de sintaxe no teste.
- [ ] Rodar baseline offline com filtros explícitos para `IdentityCompatibilityTests`, `HostInferenceAuthorityTests`, `HostBoundFlowCompletionTests`, `HostBoundSessionCompletionTests`, `HostBoundBrainCompatibilityTests`, `MigrationCatalogTests`. Registrar falhas anteriores separadamente; não declará-las regressões da renomeação.

## Task 2: Renomear Core e atualizar os consumidores como um conjunto

**Files:** mover `src/Filomena.Core/Filomena.Core.csproj` para
`src/Cortex.Core/Cortex.Core.csproj` e árvore correspondente; mover projeto de
testes para `tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj`; atualizar
`Filomena.slnx`, referências nos `.csproj` consumidores e `using`/nomes totalmente
qualificados nos fontes/testes/tools. Ajustar friends em `Boundaries/BoundaryContracts.cs`,
`Flows/Sessions/SessionReviewedResponse.cs` e `LiveContextMigration.ToolAccess.cs`.

**Interfaces:** mesmos tipos/métodos de runtime, com prefixo `Cortex.Core`, exceto
`Filomena.Core.Identity.FilomenaIdentity` compatível no mesmo assembly. Nenhuma
assinatura de flow/wire ou valor de identidade muda. `ImportLiveContext` e
`Filomena.Llm.Tests` mantêm nomes reais para os friends que os usam.

- [ ] Rodar o teste arquitetural e observar falha por ausência de `Cortex.Core`.
- [ ] Mover arquivos e atualizar referências C# por símbolo/prefixo, excluindo explicitamente a classe de identidade compatível. Não substituir literais em JSON, SQL, prompts ou hashes. Preservar `.slnx` e executable Filomena.App.
- [ ] Atualizar referências de todos os sete projetos atuais e dos cinco projetos de testes afetados, mais `tools/ImportLiveContext/ImportLiveContext.csproj` e `tools/ReconcileNotSent/ReconcileNotSent.csproj`; conferir o inventário, não depender da contagem.
- [ ] Compilar Core e consumidores em diretório de artefatos novo. Rodar identidade/host-bound e testes de `LiveContextMigrationTests`, `ObservedShadowRunTests`, `ObservedLiveRunTests`, `OutboundJournalRecoveryTests`, depois a seleção offline adicional definida na Task 1.
- [ ] Assertar que a assembly resultante é `Cortex.Core`, não depende de App/adapters e que os hashes congelados e vetores de identidade não mudaram. Registrar checkpoint; não publicar no host.

## Task 3: Extrair adapters sem extrair personalidade

**Files:** criar `src/Cortex.Inference/Cortex.Inference.csproj`, mover os arquivos
listados na tabela; modificar `src/Filomena.Llm/Filomena.Llm.csproj`,
`src/Filomena.App/Filomena.App.csproj`, `src/Filomena.Evaluation/Filomena.Evaluation.csproj`
e consumers dos tipos movidos. Criar `tests/Cortex.Inference.Tests/Cortex.Inference.Tests.csproj`.

**Interfaces:** `Cortex.Inference.ProviderFlowCompletion` implementa o mesmo
`Cortex.Core.Flows.IFlowCompletion`; `CompleteAsync(FlowCompletionRequest,
CancellationToken)` continua retornando `Task<string>`. Classes OpenAi/LmStudio/
SystemOne preservam construtores, payloads, validação, cancelamento e erro.
Nenhum fallback, provider ou default de modelo novo.

- [ ] Separar os testes offline de adapters: `OpenAiFlowCompletionTests`, `LmStudioClientTests`, `SystemOneClientTests`, `SystemOneFlowCompletionTests`; mover helpers locais necessários sem trazer App. A referência do novo projeto de teste é Core/Inference, não App. Casos mistos permanecem em Filomena.Llm.Tests.
- [ ] Observar falha de compilação por símbolos Cortex.Inference ausentes; implementar apenas a extração e referências. `SystemOneParticipationCompletion`, `FilomenaAgentOptions`, `ArbiterPrompt` e interpretação contextual permanecem com Filomena.
- [ ] Trocar `EvaluationProviderFactory` para adapters Cortex.Inference e remover sua referência a Filomena.Llm. Preservar o roteamento host-owned existente.
- [ ] Rodar novos testes de adapter com HTTP controlado; verificar payloads equivalentes, saída inválida rejeitada e provider indisponível sem fallback. Rodar os testes offline existentes de composição/participação da Filomena após a extração.
- [ ] Verificar por `Assembly.GetReferencedAssemblies()` no teste de arquitetura que Inference e Evaluation não dependem da aplicação ou Filomena.Llm. Conferir hashes de prompts e configurações; registrar checkpoint.

## Task 4: Renomear adapters de memória e transporte preservando o legado

**Files:** mover árvores/projetos para `src/Cortex.Memory/Cortex.Memory.csproj`,
`src/Cortex.OpenClaw/Cortex.OpenClaw.csproj`,
`tests/Cortex.OpenClaw.Tests/Cortex.OpenClaw.Tests.csproj`; atualizar consumidores,
solução e ferramentas. Não mover testes Graphiti para um suposto projeto existente
Memory.Tests: atualmente vários estão em Filomena.Llm.Tests.

**Interfaces:** `GraphitiMemoryReader`, `GraphitiParticipantReader`,
`GraphitiResponseValidation`, `ShadowGraphitiProbe` e contratos OpenClaw mantêm
assinaturas/semântica sob os novos namespaces. O discriminador legado
`agent:filomena` continua idêntico e documentado; não vira `agent:cortex`.

- [ ] Fixar/reexecutar testes controlados `GraphitiMemoryTests`, `GraphitiParticipantTests`, `GraphitiCanaryTests`, `NativeObservationReaderTests`, `OpenClawMcpSenderTests` e recuperação de journals. Inspecionar filtros para excluir os testes explicitamente live.
- [ ] Aplicar renomeação somente a projetos/namespaces/referências. Não editar plugin, backend Graphiti, config privada, protocolo MCP, reader/cursor ou política SendGuard.
- [ ] Testar que nomes novos ainda leem fixtures antigas, rejeitam namespace/identidade incorretos e distinguem empty de outage. Manter testes de reply, self, stale revision e entrega incerta.
- [ ] Reexecutar `node --test tools/member-dm-enrollment.test.mjs tools/native-conversation-runner.test.mjs tools/conversation-memory.test.mjs`. Nenhuma ativação live é parte deste comando.
- [ ] Verificar que Core não ganhou referência a Memory/OpenClaw, App compila com ambos e nenhum journal/fixture aprovada foi reescrito. Registrar checkpoint.

## Task 5: Renomear oficina e CLI sem alterar armazenamento

**Files:** mover `src/Filomena.Evaluation` → `src/Cortex.Evaluation`, CLI e testes
para nomes equivalentes Cortex; atualizar `.csproj`, `Filomena.slnx`, referências
de scripts operacionais da oficina no repositório e documentação atual de CLI.
Preservar `Storage/MigrationCatalog.cs` e recursos SQL semanticamente.

**Interfaces:** `MigrationCatalog.Pending(IReadOnlyDictionary<int,string>)` mantém
o mesmo resultado; `EvaluationApplication`/`CommandDispatcher` mantêm comandos,
payloads, authority e códigos. Novo nome de DLL da CLI é uma mudança de invocação
documentada, não alteração de API wire. Não criar CLI paralela com outra lógica.

- [ ] Fixar checksums literais anteriores de 001–009; complementar `MigrationCatalogTests` para comparar com essa baseline independente. Não calcular expected a partir do catálogo que está sob teste.
- [ ] Renomear projetos e namespaces, preservando `LogicalName="Filomena.Evaluation.Storage.Migrations.*"` e o lookup em `MigrationCatalog.Read`. Preservar `StorageDefinitionTests` que abre recurso legado.
- [ ] Rodar `MigrationCatalogTests`, `StorageDefinitionTests`, testes CLI e comparação offline; verificar que applied checksums anteriores retornam lista vazia e checksum adulterado continua rejeitado.
- [ ] Atualizar comandos atuais em `docs/evaluation-cli.md`; não substituir nomes em relatórios históricos, prefixos de tabelas, schemas ou config de bancos. Atualizar caminhos reais encontrados no inventário, sem iniciar scripts de banco.
- [ ] Compilar CLI e executar somente sua superfície de ajuda/validação sem binding operacional, após inspeção do entrypoint; confirmar que uso sem authority não cria acesso a banco/provedor. Aceitação PostgreSQL continua pendente e fora deste refactor.

## Task 6: Fechar compatibilidade local e entregar artefatos sem deploy

**Files:** finalizar `tests/architecture/cortex-foundation.test.mjs`,
`docs/cortex-migration-status.md`, `docs/platform-migration-progress.md` e
referências atuais em README/documentação de build encontradas no inventário.

**Interfaces:** manifest de evidência enumera comandos reais, resultados, assembly
names, exceções legadas intencionais e hashes; não contém credenciais/dados live.

- [ ] Rodar guardas de arquitetura e comparar a baseline inteira. Falhar por hash alterado, referência a caminho removido ou recurso ausente; não regenerar expected para fazer passar.
- [ ] Restaurar dependências pelos caches/fontes já aprovados, sem mudar versões. Usar uma raiz inédita de artifacts local para evitar DLLs antigas: por exemplo `dotnet build Filomena.slnx --artifacts-path <raiz-local-nova>` e `dotnet build <tool.csproj> --artifacts-path <mesma-raiz>` para as duas tools. Inspecionar targets antes. Não escrever na release ativa.
- [ ] Rodar testes offline selecionados com o mesmo artifacts path e `--no-restore`; registrar comandos completos. Não assumir que filtro `!Live` sozinho impede efeitos: revisar opt-ins, fixtures de PostgreSQL, listeners e env herdado. Se restore/build tiver falha anterior, registrar causa e não declarar migração verde.
- [ ] Usar teste de metadados de assemblies para dependências permitidas e presença de tipos/resources reais. Testes não devem passar apenas porque um arquivo contém o texto `Cortex`.
- [ ] Revisar diff procurando mudanças indevidas em `agent:filomena`, UUIDs, schemas, cláusulas, modelo, revisões, ownership e comportamento. Preservar a exceção compatível de identidade e documentar que memória genérica para Damma não foi entregue.
- [ ] Atualizar status e entregar conjunto coerente de commits locais somente se autorizado. Rollback local é reverter esses commits próprios; sem reset destrutivo, restauração de banco ou DLLs misturadas. Nenhum deploy/restart/push.

## Comandos de teste e convenções

Os nomes de classes acima são os existentes inspecionados, não testes já executados.
Para seleção use, após verificação dos corpos:

```powershell
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~IdentityCompatibilityTests|FullyQualifiedName~HostInferenceAuthorityTests|FullyQualifiedName~HostBoundFlowCompletionTests|FullyQualifiedName~HostBoundSessionCompletionTests|FullyQualifiedName~HostBoundBrainCompatibilityTests"
dotnet test tests/Cortex.Inference.Tests/Cortex.Inference.Tests.csproj --no-restore
dotnet test tests/Cortex.Evaluation.Tests/Cortex.Evaluation.Tests.csproj --no-restore --filter "FullyQualifiedName~MigrationCatalogTests|FullyQualifiedName~StorageDefinitionTests"
node --test tests/architecture/cortex-foundation.test.mjs
```

Acrescentar o artifacts path validado na Task 6 quando aplicável. Antes da Task 2,
os comandos de baseline usam os nomes Filomena existentes. Para cada tarefa:
testes novos observam falha pertinente primeiro; testes de caracterização podem
passar antes e depois, pois seu papel é preservar comportamento.

## Gates e modo de execução recomendado

- Plano escrito, aguardando revisão; nenhuma implementação nesta entrega.
- Recomendo execução **inline**, pelo mesmo agente, pois os movimentos compartilham
  referências e precisam de sequência. Sem subagentes ou revisão independente
  automática; uma revisão independente requer consulta separada à Helena.
- Primeira entrega implementável: Tasks 1–2, com Core renomeado e todos os consumidores
  compilando. Não exige concluir a oficina nem habilitar memória de outro produto.
- Seguir Tasks 3–6 somente dentro do escopo aprovado para execução. Não voltar à
  Helena por falha mecânica local corrigível; pedir decisão apenas se compatibilidade
  externa real exigir quebra contratual não contemplada.
- Aceite final desta migração: nomes/fronteiras Cortex verificados localmente,
  Filomena preservada e instalação ativa intacta. Não equivale a deploy, isolamento
  multiaplicação completo ou Damma implementada.
