# Cortex Blueprint — plano de implementação

> **Escopo atualizado pela Helena em 2026-10-15: sem suporte a legacy.** Atualizar os grafos distribuídos e seus consumidores para v4; recusar formatos antigos com erro claro. Não implementar compatibilidade histórica, retomada v3, conversor genérico de arquivos antigos ou infraestrutura exclusiva de migração de dados legados. Preservar arquivos existentes em disco. Esta decisão substitui requisitos anteriores de conversão/consulta de dados antigos neste documento; eventual suporte fica para uma entrega futura explicitamente solicitada.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Cada tarefa é implementada por GPT-6 Luna, high; sem subdelegação pelo worker.

**Goal:** tornar os grafos do Cortex próximos de Blueprints e migrar seus consumidores confirmados.
**Architecture:** forma de execução explícita, funções puras sob demanda, valores e chamadas tipados, descrições compartilhadas com o editor. Migrar para grafo v4 após estabilizar o núcleo e converter artefatos/consumidores; não manter runtime v3 no resultado final.
**Tech Stack:** C#/.NET, System.Text.Json, TypeScript/DOM, WPF/WebView2 nos consumidores pertinentes.
**Spec:** C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-runtime-design.md; C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-editor-design.md; C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-consumers-design.md.

## Global Constraints

- Modelo implementador GPT-6 Luna, esforço high. Uma tarefa de implementação por vez; revisão independente antes da próxima.
- Sem commits, push, deploy, serviços externos ou execução de grafos/contas reais. Preservar todo trabalho anterior nos checkouts.
- Regra posterior da Helena: trabalhar apenas no checkout real na main. Nunca usar worktrees, git archive ou exports para implementação/build/testes. Ao encontrar outro repositório em outra branch ou com alterações não commitadas de outra sessão, parar e perguntar antes de editar. Cortex e Cortex Agents foram passados para main preservando o trabalho existente com autorização explícita em 2026-10-15.
- Pedir feedback da Helena sobre nova decisão arquitetural; corrigir falhas comuns autonomamente e relatá-las.
- Forma de execução não se infere apenas de FlowEffect.Pure.
- Cache puro é por ativação consumidora, fora do checkpoint autoritativo.
- Sem suporte a legacy nesta entrega: não converter dados antigos, não retomar snapshots v3 e não criar API histórica especial; preservar arquivos existentes em disco.
- API de descrição não inicia sessões, credenciais, providers ou handlers de execução.
- Não executar builds/testes .NET concorrentes que compartilhem outputs. Um processo que retornou session_id continua em execução.
- Arquivos alterados antes de cada tarefa são baseline, não mudanças a reverter. Guardar cópias dos arquivos tocados antes de editar para revisão incremental.

## Review Focus

- Dois outputs da mesma função pura: uma avaliação na ativação; duas operações: duas avaliações.
- Falha/cancelamento de dependência pura: identificar origem, não executar consumidor nem ramo não escolhido.
- Emenda/retomada: valor derivado antigo não reaparece e efeitos já confirmados não repetem.
- Plugin/configuração desconhecidos: erro explícito sem criar sessões ou trocar por default.
- Formato antigo recusado claramente; nenhum dado existente apagado ou substituído por default silenciosamente.

## Execução e checkpoints

Cada tarefa deixa evidência em C:\temp\cortex-blueprint-redesign\task-N-report.md: arquivos alterados, mudanças, testes red/green, comandos/resultados, limitações. O coordenador guarda a base e gera o diff incremental para revisão. Não commitar para produzir esses diffs.

Primeira entrega visível: um grafo pequeno no Cortex demonstra cálculo puro sem exec. A troca de versão ocorre na tarefa 3; durante tarefas 1 e 2 o runtime existente continua aceitando v3 apenas como estado intermediário de desenvolvimento, sem publicação.

### Task 1: execução pura no núcleo

**Files:** C:\dev\cortex\src\Cortex.Core\Flows\NodeCatalog.cs; FlowNodeHandler.cs; TypedFlowCompiler.cs; TypedFlowExecution.cs; Execution\FlowExecutionKernel.cs. Criar testes em C:\dev\cortex\tests\Cortex.Core.Tests\Flows\PureHostNodeTests.cs e ampliar SteppedExecutionTests.cs quando necessário.

**Interfaces:** adicionar enum `FlowNodeExecutionMode { Entry, Action, Pure }` e propriedade explícita `NodeDefinition.ExecutionMode` (Action por padrão). `FlowNodeHandler(..., isEntry:true)` continua representando entrada explicitamente; normalizar Definition.ExecutionMode para Entry, recusando configuração Pure conflitante. Novos handlers puros declaram Pure e nenhuma porta de controle; devem ter efeito Pure. Expor `FlowNodeHandler.IsPure`. Built-ins property/count participam da mesma semântica de resolução pura; entradas DM/group permanecem entradas.

- [x] Escrever testes red: handler puro sem controle; rejeição Pure com efeito Model/WriteState ou controle; fan-out de dois outputs conta uma avaliação; duas operações contam duas; ramo não escolhido conta zero; erro/cancelamento impedem consumidor; ciclo e disponibilidade parcial falham na compilação.
- [x] Rodar o filtro PureHostNodeTests e registrar falha relevante.
- [x] Generalizar alcance, resolução de portas e disponibilidade para pure hosts; verificar definições resolvidas também, não só estáticas. Preservar validação das operações existentes.
- [x] No kernel, resolver dependências puras antes de ler inputs do consumidor, com cache efêmero por ativação. Validar outputs e propagar cancelamento. Não persistir derivados em snapshot.Values/Outputs. Não criar efeitos/checkpoints adicionais. Evitar duplicar a execução/validação de handlers em caminhos divergentes.
- [x] Testar emenda de output no modo passo a passo e restart/retomada com derivados recomputados e operações anteriores não repetidas. Usar fixtures duráveis existentes, sem storage externo.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~PureHostNodeTests|FullyQualifiedName~HostFlowNodeTests|FullyQualifiedName~SteppedExecutionTests|FullyQualifiedName~TypedFlowSafetyTests|FullyQualifiedName~TypedBranchTests" -v quiet -m:1 -nr:false` e o filtro durável específico criado. Reportar resultados e diff.

### Task 2: funções, estruturas e descrições compartilhadas

Execução subdividida em 2a (Core: chamadas, estruturas e constantes) e 2b (descrições, descoberta de plugins e catálogo Editor), cada uma com revisão. Constantes de entrada usam settings.inputValues: mapa de nomes declarados para valores JSON; o compilador valida e preenche TypedPort.Config.value, sem mudar tipos/portas.

**Files:** criar contratos/helpers focados em C:\dev\cortex\src\Cortex.Core\Flows; integrar NodeCatalog.cs e plugin metadata em C:\dev\cortex\src\Cortex.Core\Plugins; C:\dev\cortex\src\Cortex.Editor\EditorCatalog.cs. Testes em Core/Flows e Cortex.Editor.Tests.

**Interfaces:** `FlowFunctionDefinition` descreve Id, DisplayName, inputs/outputs tipados, efeito e referência opcional navegável. `FlowFunctionRegistry` recebe definições e delegates de invocação do host, resolve alvo estático autorizado e cria handler de chamada com portas resolvidas por configuração. Descrição de funções pode ser obtida só das definições, sem delegates. Chamada com efeitos é Action. Não adicionar recursão.

`FlowNodeDescription` representa a instância resolvida: nome, execution mode, dados com schemas completos e portas de controle. API de descrição recebe NodeDefinition + configuração; não FlowNodeHandler vinculado a sessão. Compartilhar com o editor e disponibilizar aos hosts.

- [x] Testar chamada com parâmetros/retornos estruturados, alvo desconhecido, tipo incompatível e exceção/cancelamento; contagem de invocação exatamente uma no caminho escolhido.
- [x] Implementar helpers Make/Break Struct puros com contrato configurado e validação completa; testar optional/nullable/array, campo desconhecido e tipo incorreto.
- [x] Testar e implementar descrição de portas dinâmicas, incluindo portas desconectadas e erros de configuração. Teste de catálogo usa factories que lançam caso provider, credencial ou sessão sejam acessados.
- [x] Migrar o catálogo Editor para descrever configurações reais. Registrar no relatório APIs finais e exemplos mínimos que os consumidores usarão.
- [x] Rodar testes focados Core e Editor, incluindo notas e plugins afetados; sem alterar consumidores ainda.

### Task 3: formato v4 e grafos internos

Escopo corrigido pela Helena: sem legacy. Atualizar guardas do compilador, geradores, grafos internos ativos e fixtures para v4. Formato portátil, plugin/bloco e snapshot permanecem contratos independentes. Recusar formatos antigos com erro claro. Não implementar API histórica especial, retomada v3, conversor genérico offline ou infraestrutura de migração de dados antigos; retirar essas adições Task3 sem reverter outras alterações.

- [x] Testar aceitação v4 e rejeição explícita de formatos antigos.
- [x] Atualizar grafos distribuídos e fixtures ativos; preservar testes intencionais de versão incompatível.
- [x] Executar testes/builds internos afetados, com filtros offline e número de testes registrado.
- [x] Preservar dados reais existentes e trabalho anterior. Relatório e revisão incremental obrigatórios.

### Task 4: editor Blueprint compartilhado

**Files:** C:\dev\cortex\packages\flow-graph\src\index.ts e estilos/testes; C:\dev\cortex\src\Cortex.Editor\wwwroot\editor.js, use.js, editor-model.mjs, editor.css; testes Editor.

**Interfaces:** renderer recebe NodeView com tipos reais, modo de execução e referência de chamada; host fornece validação/navegação. Preservar API de câmera/dispose ou migrar todos os chamadores no mesmo passe.

- [x] Usar descrição comum; diferenciar operação/evento/puro/nota, mostrar portas desconectadas e tipos estruturados, defaults simples e chamada navegável.
- [x] Testar conexão incompatível sem mutação, config dinâmica, nó indisponível, read-only, volta com câmera/seleção preservadas e disposal. Não inferir tipos a partir dos fios.
- [x] Rodar testes de renderer e Editor; fazer um lote visual amplo/reduzido, no máximo uma correção e confirmação. Documentar APIs de integração para os próximos workers.

### Task 5: Cortex Agents

Segundo pré-requisito de 5a, escolhido explicitamente pela Helena: suporte compartilhado a dicionários tipados no Core/editor, preservando o mapa de orçamento por modelo. Implementar com Luna, validar codecs/execução/metadados/constantes e revisar antes de retomar Agents. Brief: C:\temp\cortex-blueprint-redesign\task-5a-dictionary-brief.md.

Pré-requisito descoberto em 5a e aprovado pela Helena: permitir saídas nomeadas opcionais ausentes, encaminhando ausência a entradas opcionais e falhando em entradas obrigatórias. Implementação isolada no Core pelo Luna, com testes de funções puras e retomada e revisão independente, antes de retomar a integração Agents. Preservar null explícito e a regra de erro para campo solicitado ausente em Separar estrutura. Brief: C:\temp\cortex-blueprint-redesign\task-5a-optional-core-brief.md.

Execução em dois lotes sequenciais, sem mudança de arquitetura ou aceite: 5a contratos/grafos/catálogo das camadas e brains, com testes core; 5b inspector/Desktop e abertura isolada para teste. Revisão independente após cada lote; concluir ambos antes da Filomena. Briefs em C:\temp\cortex-blueprint-redesign\task-5a-brief.md e task-5b-brief.md.

**Files:** C:\dev\cortex-agents\Layers; Brain; layers; Desktop Hosting/CortexLayers, LayerViews e Web/cortex-graph; testes afetados.

- [x] Migrar contratos de sete camadas e brains para funções tipadas, removendo adaptação JSON redundante. Entrada termina com cinco operações conforme spec; preservar falhas, aprovação de setup e revisão/correção.
- [x] Migrar tipos de ranking/trabalho/revisão ponta a ponta, não apenas labels visuais. Leituras de snapshot puros usam Task 1. Remover adapters sem uso depois da conversão.
- [x] Atualizar defaults, grafos gerados e fixtures distribuídos para v4. Overrides antigos são recusados com erro claro; não convertê-los nem substituí-los silenciosamente.
- [x] Inspector devolve descrições separadas do JSON original, navega chamadas e usa renderer comum sem executar providers.
- [x] Rodar três grupos core indicados no spec e testes Desktop focados; build e abrir app para teste sem análise automática. Registrar esse checkpoint antes do próximo consumidor.

### Task 6: Filomena

**Files:** C:\dev\filo\src\Filomena.App; Filomena.Llm; playground; ferramentas ReconcileNotSent/ImportLiveContext; grafos e testes afetados.

- [x] Migrar geradores/leitores/fixtures/artefatos e consumo do catálogo/renderer. Preservar comportamento de sessão, boundaries, feedback e envio confirmado.
- [x] Recusar grafos antigos sem retomada/fallback. Preservar comportamento de cancelamento e reconciliação da execução v4; não criar infraestrutura de migração de dados antigos.
- [x] Build de App/Llm/ferramentas, testes offline afetados Core/Llm/Flows e frontend, incluindo FlowGraphAssetTests. Provar retomada v4 e diagnóstico de v3 sem repetir efeitos.

### Task 7: Depois do Slogan / FluxStudio

**Files:** C:\dev\depois-do-slogan\src\DepoisDoSlogan.Web; DepoisDoSlogan.CortexPlugin; FluxStudio.Device no mesmo checkout; grafos e testes pertinentes.

- [x] Migrar handlers/plugins, uso de EditorUseService, contratos/grafos e integração do editor. Preservar spending e fluxos de vídeo/editorial.
- [x] Build dos consumidores Editor e testes offline Web/plugin/frontend afetados. Sem renderização paga, postagem ou execução externa.

### Task 8: NewWine e Damma; fechamento do inventário

**Files:** C:\dev\new-wine\src\NewWine.TrustLab.Cortex e testes NewWine.TrustLab.Tests; C:\dev\damma\personas\tests\Damma.Personas.Tests.

- [x] Migrar grafo embutido, plugin e replay de NewWine; testar runtime offline e replay pertinente.
- [x] Adaptar Damma somente se contratos de inferência usados mudaram; rodar OfflinePersonaSmokeTests e verificar número de testes. Compatibilidade sem diff é resultado válido.
- [x] Repetir inventário limitado a referências reais, registrar novos consumidores antes de modificar; testar consumidores adicionais afetados.
- [x] Revisão final incremental de todos os checkouts, matriz de builds/testes e pendências. Nenhum commit/push/deploy automático.


## Sem legacy (decisão posterior)

A Helena retirou suporte a dados antigos do escopo. Preparações anteriores de cutover/histórico são evidência arquivada, não requisitos desta entrega. O foco é grafos e contratos do código distribuído em v4. Não criar conversor, API histórica especial, adapters de cutover ou retomada v3. Não apagar dados reais.

Pré-requisito adicional de Task5a, aprovado em 2026-10-15: tipo JSON bruto compartilhado para evidência de histórico de execuções novas, inclusive entradas inválidas. Concluir correções independentes da revisão5a (Features, Delegação, ações de cartões), implementar/revisar JsonType com Luna em uma etapa sequencial, depois integrar e revisar R3 do histórico. Brief: C:\temp\cortex-blueprint-redesign\task-5a-json-brief.md. Sem legacy, sem fallback de string, sem alterar evidência em disco.

## Resultado da entrega

Registro durável de resultado, decisões, verificações, limites e revisão final: C:\dev\cortex\docs\superpowers\plans\2026-10-15-cortex-blueprint-redesign-result.md.
