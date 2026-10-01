# Cortex / Filo Repository Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Native execution in the current session is recommended; no subagents or independent reviewers without the user's separate approval.

**Goal:** Separar Cortex e Filo em dois Gits independentes, usando referências locais e preservando comportamento, histórico e arquivos privados.

**Architecture:** O Git atual conserva a fundação e passa a ocupar `C:\dev\cortex`. Uma cópia Git independente origina `C:\dev\filo`, contendo a aplicação; `CortexRoot` resolve a fundação vizinha. Preparar e validar fontes antes do rename físico, sem alterar serviços.

**Tech Stack:** .NET 10, MSBuild ProjectReference, Git, Node.js, PowerShell; nenhuma infraestrutura nova.

**Spec:** `docs/superpowers/specs/2026-10-01-cortex-filo-repository-split-design.md`.

## Global Constraints

- Checkout atual em `main`; não criar worktree, reescrever histórico, fazer push ou criar remoto.
- Filo sem origin herdado; Cortex preserva `git@github.com:distrohelena/cortex.git`.
- Preservar brain, prompts, modelos, identidade, memória, Graphiti e outbound; nenhuma chamada live.
- Não mover ou apagar ignorados indiscriminadamente; DREAMS permanece privado e sem estágio.
- Sem junction, submodule, NuGet privado ou fonte Cortex duplicada em Filo.
- Manter a exceção de identidade legada, hashes aprovados e checksums SQL; não rebaselinar.
- Testes offline somente, opt-ins live desabilitados no processo filho. PostgreSQL continua pendente.
- Commits locais de extração somente após aceite do plano; não incluir alterações alheias.

## Review Focus

1. Root alternativo com espaços ou cwd diferente: referências e assets devem resolver corretamente (tarefa 3).
2. Histórico compartilhado não pode deixar Filo apontando para o remoto Cortex ou usando alternates (tarefa 2).
3. Testes/fixtures mistos não podem sumir para fazer builds passarem (tarefas 1 e 4).
4. Caminho ativo fora da linha de comando pode depender da pasta antiga; não romper launchers/config (tarefas 1 e 6).
5. Manifesto de compatibilidade não pode mudar hashes ao mudar a localização dos arquivos (tarefas 4 e 5).

## Arquivos e ownership

| Destino | Arquivos / responsabilidade |
| --- | --- |
| Cortex | `src/Cortex.*`, `packages/flow-graph`, `runtime/evaluation`, testes puros de infraestrutura |
| Filo | `src/Filomena.App`, `src/Filomena.Llm`, `tests/Filomena.Llm.Tests`, `config`, `workspace` rastreado, plugins e ferramentas operacionais |
| Filo | `evaluation/conversation-quality-v1`, runtime Graphiti fora de `runtime/evaluation`, scripts atuais de operação/relatórios |
| Filo | Cinco testes App hoje em Cortex.OpenClaw.Tests: InboundBridgeHttpServer, PhaseBComposition, PhaseCPipeline, ProbeOptions, TimedLmStudioClient |
| Classificação individual | `docs`, `tests/fixtures`, `tests/Shared`, `vendor`, fixtures de suites Core, testes de arquitetura |
| Compartilhado deliberado | `.gitignore`, `.gitattributes`, `global.json`; arquivos de metadados podem existir nos dois, sem duplicar bibliotecas |

Criar `docs/repository-split-manifest.json` como registro do checkpoint: `schemaVersion`,
`sourceCommit`, `files[{sourcePath,owner,destinationPath,sha256,reason}]`. `owner` aceita
`cortex`, `filo` e `both-metadata`; o último exige justificativa e não permite fonte de produção.
Documentos históricos podem ser preservados com indicação de arquivo histórico; documentos
canônicos novos têm um único owner. Nenhum dado privado entra nesse manifesto além de paths rastreados.

### Tarefa 1: inventário verificável e gate operacional

**Files:** criar manifesto acima, `tests/architecture/repository-split.test.mjs`; registrar evidência privada em `.tmp-repository-split/` ignorado.
**Interfaces:** manifesto associa cada arquivo de `git ls-files` no checkpoint a destino explícito; inventário privado não é artefato Git.

- [ ] Reconsultar Git/HEAD, instruções locais, paths absolutos, existência dos destinos e status de processos/launchers relevantes. Não despejar command lines ou credenciais.
- [ ] Classificar arquivos rastreados com leitura dos consumidores; resolver fixtures pelo código que as carrega, não apenas pelo nome do diretório.
- [ ] Criar teste Node com entradas sintéticas: rejeitar arquivo sem owner, destino duplicado, path absoluto/`..`, produção `both-metadata`; observar falha antes do helper mínimo do teste/manifesto.
- [ ] Validar o manifesto contra a lista completa do checkpoint e hashes de origem; nenhum arquivo omitido.
- [ ] Inventariar ignorados/untracked por categoria, hashes privados quando necessários e ACLs antes do corte. DREAMS não entra no clone nem no commit.
- [ ] Identificar consumidores do caminho antigo em scripts/configs/launchers conhecidos. Dependência operacional concreta bloqueia SOMENTE o corte físico; preparar código continua permitido.

### Tarefa 2: criar o Git independente e aplicar ownership

**Files:** novo `C:\dev\filo\.git`, arquivos destinados à Filo; `Cortex.slnx` no original e `Filo.slnx` na Filo.
**Interfaces:** dois Git dirs completos, mesmo checkpoint ancestral, sem alternates/worktrees; manifesto governa remoções rastreadas.

- [ ] Confirmar `C:\dev\filo` ausente e checkpoint preservado. Não sobrescrever diretório existente.
- [ ] Clonar localmente com `git clone --no-local --no-hardlinks C:/dev/filomena C:/dev/filo`; remover imediatamente o origin de Filo. Não acessar GitHub.
- [ ] Verificar HEAD ancestral, `.git` independente, ausência de `.git/objects/info/alternates` e `git remote -v` vazio em Filo.
- [ ] Aplicar ownership às cópias rastreadas: remover de cada working tree somente paths explicitamente classificados para o outro repo, após conferir hash/cópia recuperável. Usar paths literais, nenhuma limpeza recursiva genérica.
- [ ] Criar as duas soluções com os projetos próprios. Manter assemblies Filomena com nomes atuais; nenhuma renomeação cosmética adicional.
- [ ] Preservar docs históricos e source checkpoint. Não copiar bin/obj, .env, estado, node_modules, logs ou publish ativo para o Git novo.

### Tarefa 3: referências locais e assets

**Files:** Filo `Directory.Build.props`, `Directory.Build.targets`, `cortex-reference.json`, `scripts/check-cortex-reference.mjs`, `scripts/build-flow-graph.mjs`, `.csproj` de App/Llm/ferramentas/testes; Cortex `packages/flow-graph` sem mudança funcional.
**Interfaces:** `CortexRoot` é absoluto normalizado, default relativo a `MSBuildThisFileDirectory/../cortex`; `cortex-reference.json` contém `schemaVersion:1` e `testedCommit` (SHA completo real, não hash inventado).

- [ ] Adicionar testes de resolução antes da implementação: default sibling, override com espaços, cwd diferente, root ausente, projeto requerido ausente. Esperar erro `CORTEX_ROOT_INVALID`, sem download ou busca alternativa.
- [ ] Implementar props/target de validação e trocar ProjectReferences por `$(CortexRoot)/src/...`. Enquanto o root antigo existir, validar com `-p:CortexRoot=C:/dev/filomena`.
- [ ] Passar root resolvido ao script de assets; compilar o pacote em Cortex e copiar apenas dist para o App. Não depender do node_modules do plugin em Filo; usar TypeScript local já disponível do pacote e dar erro claro se ausente, sem instalar automaticamente.
- [ ] Implementar checker CLI `node scripts/check-cortex-reference.mjs --root <path> [--strict]`: informar revisão testada/current/dirty sem mostrar diff; `--strict` falha em mismatch/dirty, modo normal informa desenvolvimento. Nunca checkout/pull/rebase automático.
- [ ] Testar checker com Gits temporários: SHA correspondente limpo, SHA diferente, dirty, root ausente; nenhuma chamada remota.
- [ ] Verificar build Filo com override e assets produzidos; não iniciar App. A revisão final testada será preenchida após commit Cortex na tarefa 5.

### Tarefa 4: separar testes e preservar evidências

**Files:** Cortex `.csproj` de Core.Tests/OpenClaw.Tests; Filo `tests/Filomena.App.Tests/`; fixtures e helpers conforme manifesto; testes `tests/architecture/cortex-foundation.test.mjs` em cada destino.
**Interfaces:** testes Cortex não dependem de Filo; testes de aplicação referenciam App e CortexRoot. SHA esperado do manifesto original continua idêntico.

- [ ] Executar build Core.Tests sem ProjectReference Filomena.Llm; se houver consumidor real, mover SOMENTE seu teste de composição para Filo. Não adicionar referência inversa de volta.
- [ ] Mover os cinco arquivos de testes App identificados para Filomena.App.Tests; conferir também referências qualificadas sem using antes de remover App de Cortex.OpenClaw.Tests.
- [ ] Conferir dependências transitivas de fixtures, FakeTimeProvider e SystemOneTestSupport. Para helpers genéricos, link explícito via CortexRoot é permitido; não puxar App para Cortex.
- [ ] Adaptar resolução dos fixtures/documentos por root do owner. Não mudar payloads ou expectativas para contornar paths quebrados.
- [ ] Distribuir checks de compatibilidade por owner preservando cada um dos 53 hashes originais. Adicionar teste de união sem perdas/duplicatas; não regenerar hash esperado.
- [ ] Executar testes em ambos; reconciliar inventário de casos com baseline. Contagens distintas exigem explicação por movimento/novos testes, não redução silenciosa.

### Tarefa 5: aceite local e checkpoints

**Files:** READMEs dos dois repos, documento de split/status, `cortex-reference.json` final.
**Interfaces:** revisão Cortex validada e commit Filo que a registra; ambos recuperáveis localmente.

- [ ] Restaurar/build usando cache NuGet já disponível; nenhum serviço, banco ou modelo. Usar artifacts paths separados e reproduzíveis em cada checkout.
- [ ] Executar `dotnet test Cortex.slnx` com filtro `Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests`, opt-ins FILOMENA removidos somente do processo de testes; executar `dotnet test Filo.slnx` nas mesmas condições.
- [ ] Rodar testes Node de arquitetura, pacote flow-graph e ferramentas operacionais nos respectivos owners. Manter fixtures sintéticas; scripts de operação não são executados como smoke.
- [ ] Provar ausência de dependência reversa por avaliação das referências MSBuild e build Cortex numa cópia rastreada controlada sem Filo, sem apagar/mover Filo para testar.
- [ ] Rodar `git diff --check`, auditoria de novos segredos e conferência de Git dirs/remotos; preservar ignorados e DREAMS.
- [ ] Fazer self-review dos manifests e dependências. Não lançar independent review sem autorização específica.
- [ ] Commit local Cortex do split; registrar seu SHA real em Filo, validar checker strict e build Filo; commit local Filo. Sem push. Registrar ambos os hashes e resultados, inclusive integrações não executadas.

### Tarefa 6: rename físico protegido e verificação final

**Files:** diretório original `C:\dev\filomena` torna-se `C:\dev\cortex`; status de split.
**Interfaces:** referências default funcionam; private state preservado; runtime ativo não reconfigurado.

- [ ] Revalidar gate operacional da tarefa 1 imediatamente antes do rename. Se houver dependência ativa do caminho antigo, parar só essa ação e reportar a menor mudança live necessária, mantendo código/testes prontos.
- [ ] Verificar origem absoluta `C:\dev\filomena`, destino `C:\dev\cortex` ausente e identidade do Git esperado. Executar rename nativo PowerShell fora da pasta origem; sem combinação de shells ou exclusão recursiva.
- [ ] Conferir HEAD, origin, ACLs/arquivos privados preservados e status nos dois repos. DREAMS permanece no diretório original agora chamado Cortex; não é redistribuído automaticamente.
- [ ] Reexecutar build/testes Filo com CortexRoot default e checker strict; build/testes Cortex sem Filo. Nenhuma alteração de launchers/serviços para fazer os testes passarem.
- [ ] Se o movimento físico falhar, não parar processos arbitrariamente. Se necessário reverter rename, validar os dois caminhos e ausência de colisão; não usar reset hard ou limpeza de arquivos privados.
- [ ] Entregar paths, commits, comandos de desenvolvimento, testes e qualquer pendência operacional real. Não declarar deploy ou mudança na Filomena ativa.

## Execução proposta e limite desta entrega

Executar nativamente nesta sessão, um responsável pelas mudanças correlacionadas;
nenhum subagent, worktree adicional ou review independente. As tarefas são fortemente
dependentes entre si e o paralelismo não compensa o risco de ownership sobreposto.

Este documento é o plano para revisão. Após aprovação do plano e da execução nativa,
seguir todas as tarefas até conclusão ou blocker operacional real. Não pedir confirmação
para cada bug local, ajuste de referência ou teste necessário dentro deste escopo.
