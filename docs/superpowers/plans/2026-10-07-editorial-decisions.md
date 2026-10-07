# Plano de decisões de edição Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task after the human reviews this plan. Steps use checkbox (`- [x]`) syntax for tracking. Proposed execution is inline in this session. Subagents and a fresh independent review require separate user authorization.

**Goal:** Gerar um plano editorial por cena antes da revisão humana, preservar seus ajustes e executar entrada e zoom em imagens na prévia do Depois do Slogan.

**Architecture:** Cortex.Video possui documentos tipados, validação, merge de decisões, resolução de foco, compiler e renderer compartilhado do movimento. O plugin Depois do Slogan gera roteiro e decisões iniciais na mesma chamada, e permite replanejar cenas com o roteiro completo como contexto. O host persiste versões e a interface revisa o plano; materiais e prévias continuam usando os fluxos existentes.

**Tech Stack:** .NET 10, System.Text.Json, JSON Schema 2020-12, Cortex FlowNodeHandler/IFlowCompletion, SQLite existente, JavaScript ES modules, FFmpeg e renderer VFX existente, xUnit v3 e node:test. Sem novo provedor ou pacote de geração/visão.

**Spec:** [desenho aprovado](../specs/2026-10-07-editorial-decisions-design.md), aprovado em conversa em 2026-10-07; [exemplo editorial](../../examples/editorial-decisions/depois-do-slogan.md).

## Global Constraints

- Novo schema `cortex.edit.decisions.v1`; conservar os quatro documentos editoriais atuais e os contratos de cortes legados.
- IDs, campos externos, enums e diagnósticos técnicos em inglês; interface, justificativas e textos autorais em português.
- Exatamente uma decisão por cena, na ordem do roteiro; modelos não inventam fontes, pessoas, textos autorais, coordenadas de imagem ou timecodes de gravação.
- Catálogo inicial: entrada `cut`/`fade`, movimento `none`/`zoom`, curvas `linear`/`ease_in_out`, alvos `center`/`point`/`text_region`; saídas entre cenas por corte.
- Escalas entre 1 e 2,5, tempos locais finitos e dentro da cena; ponto normalizado na fonte em `[0,1]`; região textual usa pixels da mídia selecionada.
- Revisão `pending`/`accepted`/`rejected`; origens `cortex`/`human`/`inherited`; referências de plano, roteiro e mídia têm revisões e fingerprints reais.
- Foco textual permanece `pending_media`/`needs_review` até uma marcação humana válida ou um retângulo já validado para a mesma mídia. OCR/visão automático fica fora desta entrega.
- Overlays e legendas são compostos depois da imagem; não herdam seu zoom. A primeira entrega mantém a prévia de planejamento silenciosa.
- Ajustes humanos são preservados; aprovação baseada em revisão antiga falha como conflito. Alterações de movimento não regeneram PNGs.
- Reutilizar a confirmação de custo em andamento no host. Uma geração inicial faz uma chamada que inclui roteiro e decisões; nova tentativa/replanejamento pago exige a confirmação já prevista pelo produto. Não acrescentar preços ou criar outro diálogo de custo.
- Há alterações concorrentes nos dois repositórios, incluindo legenda, timestamps e custo. Executar em checkouts isolados que incluam o estado atual; integrar apenas o diff próprio, sem reverter essas alterações. Instalar/publish com `-p:CortexRoot=C:/dev/cortex` ajustado ao checkout do Cortex em uso.

## Review Focus

1. Duas abas salvam o mesmo plano: a gravação baseada em revisão antiga não pode apagar a edição recente (tarefa 5).
2. O mesmo post aparece em duas cenas com movimentos diferentes: cada instância precisa de tempo local, sem compartilhar uma animação indevida (tarefas 3 e 7).
3. A imagem é trocada por outra de dimensões diferentes durante a resolução: o foco antigo fica pendente e uma marcação atrasada é rejeitada (tarefas 3 e 5).
4. A IA mantém o ID mas muda o papel da cena: alterações humanas ficam preservadas como conflito, sem aplicação automática ao novo conteúdo (tarefas 2 e 4).
5. O plano é recalculado enquanto a pessoa edita ou a cotação expira: resultado antigo não substitui o atual; não há nova chamada paga sem confirmação válida (tarefas 4 e 5).

## Estrutura e dependências

Os caminhos `src/Cortex.Video` e `tests/Cortex.Video.Tests` pertencem a `C:/dev/cortex`. Os caminhos `src/DepoisDoSlogan*`, `tests/DepoisDoSlogan*` e `tests/frontend` pertencem a `C:/dev/depois-do-slogan`.

| Unidade | Responsabilidade | Dependências |
|---|---|---|
| Documento e validator | Forma tipada, limites e relações com roteiro | Contratos editoriais existentes |
| Contexto e merge | Identidade das cenas, proveniência e alterações humanas | Documento |
| Resolução e compiler | Foco real e sequência de frames local | Documento e merge |
| Planejador do plugin | Proposta inicial e por cenas | Documento e contexto |
| Store e runtime | Versões, autorização, custo, filas e resultados obsoletos | Planejador e merge |
| Interface | Revisão dos valores e marcação de foco | API e resolução |
| Renderer compartilhado | Frames de imagem com fade e zoom | Compiler |
| Adaptação da prévia | Compor frames, textos e cenas | Store, resolução e renderer |

As tarefas 1–8 formam uma sequência de integração. Cada commit fica compilável e tem seus próprios testes; ativação no site vem depois da validação integrada.

## Task 1: Documento, schema e validação das decisões

**Files:** Create `src/Cortex.Video/Editing/EditorialDecisionModels.cs`, `EditorialDecisionValidation.cs`, `EditorialDecisionFlowTypes.cs`, `Schemas/cortex.edit.decisions.v1.schema.json`; modify `EditingDocumentKinds.cs`, `EditingJson.cs`, `EditingSchemas.cs`, `EditingValidationContext.cs`, `EditingContractValidator.cs`. Test `tests/Cortex.Video.Tests/EditorialDecisionContractTests.cs`; create `EditorialDecisionFixtures.cs` nesse projeto para os fixtures compartilhados.

**Interfaces:** `EditorialDecisionPlanDocument : EditingDocument` possui `ProjectId`, `PlanId`, `Revision`, `ScriptRef`, `ContextFingerprint`, `ReviewStatus`, `Scenes`. `EditorialSceneDecision` possui `BeatId`, `IdentityFingerprint`, `Reason`, `Proposal`, `Overrides`, `ReviewStatus`; `Proposal` é `EditorialSceneRecipe` com material, apresentação, tempo, referências aos textos autorais e movimento. `EditorialFieldOverride` guarda caminho permitido, valor validado e origem humana. Declarar records tipados para esses grupos e enums do catálogo. `EditingDocumentKinds.Decisions` é `cortex.edit.decisions.v1`; `EditorialDecisionFlowTypes.Plan` expõe os campos executáveis. `EditorialDecisionValidation.Validate(plan, script, currentScriptRef)` retorna `IReadOnlyList<FlowIssue>` e não lê mídia.

- [x] Escrever round-trip, schema e validação: sete cenas na ordem correta; texto português preservado; schema desconhecido, propriedades duplicadas, fonte inexistente, overlays inventados e cenas faltantes falham. Criar testes com escala `0.99`/`2.51`, `NaN`, tempo fora da cena, ponto fora de `[0,1]` e comando livre no lugar do catálogo.

  Testes `RoundTripsAllSevenScenes` e `RejectsScaleOutsideCatalog`, com assertions independentes do builder:
  ```csharp
  Assert.Equal(7, parsed.Scenes.Count);
  Assert.Equal("hook", parsed.Scenes[0].BeatId);
  Assert.Contains(issues, issue => issue.Code == "edit.decisions.scale");
  ```
- [x] Rodar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter FullyQualifiedName~EditorialDecisionContractTests`; confirmar falha pelas APIs ausentes.
- [x] Implementar os records/schema/interfaces, nova opção do dispatcher e validação, usando os patterns editoriais existentes. Ampliar `EditingValidationContext` com referência opcional ao documento de decisões. Preservar as quatro formas v1 atuais; não acrescentar campos desconhecidos aos seus JSONs.
- [x] Rodar os testes novos e os testes editoriais existentes. Validar os fixtures contra o schema com o `jsonschema` já disponível no ambiente; rejeições devem concordar nos limites portáveis.
- [x] Registrar somente os arquivos desta tarefa no Cortex, com mensagem `feat(video): add editorial decision contract`.

## Task 2: Contexto, revisão e merge que preserva decisões humanas

**Files:** Create `EditorialDecisionContext.cs`, `EditorialDecisionMerge.cs`, `EditorialDecisionExport.cs` em `src/Cortex.Video/Editing`; test `EditorialDecisionMergeTests.cs`, `EditorialDecisionExportTests.cs`.

**Interfaces:** `EditorialDecisionContext.Create(EditScriptDocument script) -> EditorialPlanningContext` retorna JSON de contexto, fingerprint e mapa de identidade por cena. A identidade inclui tipo visual, fonte/pessoa e propósito; o contexto completo inclui ordem, falas, textos, duração, formato e perfil de legenda. `EditorialDecisionMerge.Effective(plan) -> IReadOnlyList<EffectiveEditorialScene>` aplica overrides com origem por campo; `ApplyPatch(plan, beatId, IReadOnlyList<EditorialFieldPatch> patches, int expectedRevision)` retorna novo documento; `MergeReplanned(previous, proposal, currentScript) -> EditorialMergeResult` retorna plano e conflitos. `EditorialDecisionExport.Export(script, plan) -> JsonElement` e `Import(JsonElement bundle) -> EditorialProjectBundle` conservam dois documentos e validam suas relações.

- [x] Escrever testes: zoom humano `1.6` sobrevive a nova proposta `1.2`; aceitar sem editar mantém origem Cortex e muda revisão; retirar um override restaura só o campo escolhido; revisão base antiga falha; ID reutilizado com outro propósito preserva override como conflito. Export/import preserva os valores e rejeita pares de cenas incompatíveis.

  Teste `ReplanKeepsHumanZoomScale`:
  ```csharp
  Assert.Equal(1.6, effectiveZoomScale);
  Assert.Equal("human", origins["motion.zoom.to_scale"]);
  Assert.Empty(compatibleReplan.Conflicts);
  ```
- [x] Rodar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter "FullyQualifiedName~EditorialDecisionMergeTests|FullyQualifiedName~EditorialDecisionExportTests"`; observar as falhas esperadas.
- [x] Implementar os métodos. Whitelist de caminhos cobre material/apresentação, entrada, zoom e foco; referência aos textos e ao perfil vigente fica tipada e não permite reescrever fala pelo patch do plano. Valorar um grupo inteiro substitui sua proposta efetiva, mas mantém a proposta original para comparação. Não aplicar overrides órfãos sem revisão explícita.
- [x] Rodar os testes da tarefa e o contrato. Verificar que mudanças apenas em movimento deixam a composição/tamanho do pedido de referência intactos.
- [x] Commit `feat(video): preserve human editorial decisions` com os arquivos próprios.

## Task 3: Resolução de foco e compilação de movimento por cena

**Files:** Create `EditorialMediaResolution.cs`, `EditorialDecisionCompiler.cs`, `EditorialImageTransform.cs` em `src/Cortex.Video/Editing`; test `EditorialFocusResolutionTests.cs`, `EditorialMotionCompilerTests.cs`.

**Interfaces:** `EditorialImageSnapshot` inclui ID, SHA-256, largura e altura; `EditorialFocusResolution` inclui beat, mídia/hash, estado, região em pixels e origem. `EditorialFocusResolver.Resolve(target, image, previousResolution, humanSelection) -> EditorialFocusResolution` resolve centro/ponto imediatamente e mantém texto sem área em revisão. `EditorialDecisionCompiler.Compile(EffectiveEditorialScene scene, EditorialFocusResolution focus, double sceneDurationSec, int fps) -> CompiledImageMotion` valida os parâmetros e gera a curva local. `EditorialImageTransform.AtFrame(compiled, int frame, int outputWidth, int outputHeight) -> ImageFrameTransform` retorna retângulo de amostragem e opacidade finitos, sem IO/modelo.

- [x] Escrever testes para a abertura de 3 s: entrada termina em `0.25`, zoom vai de `0.8` a `2.2`, escala vai de `1` a `1.35`, enquadramento estabiliza até `3`. Aos `24` fps, testar início/meio/fim e último frame anterior ao fim exclusivo. Legendas não são parâmetros dessa transformação.

  Teste `ZoomChangesSamplingAndHoldsItsFinalFrame`, com fonte quadrada de `1000×1000` e saída `540×540`. `ImageFrameTransform` expõe `SourceRect` e `Opacity`:
  ```csharp
  Assert.Equal(0, first.Opacity);
  Assert.InRange(last.SourceRect.Width, 740, 742);
  Assert.Equal(atEndOfZoom.SourceRect, last.SourceRect);
  ```
- [x] Adicionar testes para centro, ponto na borda, retângulo textual fora da fonte, texto pendente, imagem substituída e seleção com hash antigo; repetir a mesma imagem em duas cenas com escalas diferentes e provar que as curvas são independentes.
- [x] Rodar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter "FullyQualifiedName~EditorialFocusResolutionTests|FullyQualifiedName~EditorialMotionCompilerTests"`; confirmar falha.
- [x] Implementar as interfaces. Curva `ease_in_out` usa `3u²−2u³` com progresso local limitado a `[0,1]`; `linear` usa `u`. O retângulo de amostragem preserva a proporção e é limitado à fonte. Foco textual sem área não produz movimento executável; retornar pendência tipada para o caller.
- [x] Rodar para saídas `540×960`, `960×540` e `540×540`, verificando retângulos dentro da mídia e opacidade entre `0` e `1`; commit `feat(video): compile image motion and focus resolution`.

## Task 4: Propostas do Cortex junto com o roteiro e replanejamento por cenas

**Files:** Create `src/DepoisDoSlogan.CortexPlugin/EditorialDecisionPlanner.cs`, `DraftWithDecisionsGenerator.cs`; modify `DraftScriptGenerator.cs`, `SloganTypes.cs`, `SloganNodes.cs`, `ReferenceVisualPlanner.cs`, `src/DepoisDoSlogan/SloganGraph.cs`. Test `tests/DepoisDoSlogan.CortexPlugin.Tests/EditorialDecisionPlannerTests.cs`, `DraftWithDecisionsTests.cs` e os testes de contrato de graph existentes.

**Interfaces:** `SloganDraftWithDecisions` contém `Script`, `Decisions?`, `PlanningError?`. `DraftWithDecisionsGenerator.GenerateAsync(VideoBrief, IFlowCompletion, CancellationToken, string? model, string? feedback)` faz uma única chamada estruturada que inclui rascunho de cenas e receitas editoriais. Manter `DraftScriptGenerator.GenerateAsync` como API antiga para consumidores antigos. `EditorialDecisionPlanner.ReplanAsync(script, priorPlan, IReadOnlyList<string> beatIds, completion, token, model)` retorna proposta somente para IDs solicitados, recebendo o roteiro inteiro e overrides preservados como contexto.

- [x] Escrever testes com completion fixture: geração inicial usa uma chamada e produz roteiro e decisões coerentes; parâmetros saem preenchidos; cenas existentes, textos e pessoas são respeitados. Em JSON válido com roteiro válido e proposta inválida, retornar roteiro e `PlanningError`, preservando uma versão editável. JSON inválido ou rascunho inválido não produz novos documentos.

  Teste `InitialGenerationReturnsOneCoherentPair`:
  ```csharp
  Assert.Equal(1, observedProviderCalls);
  Assert.Equal(result.Script.Beats.Select(b => b.Id), result.Decisions!.Scenes.Select(s => s.BeatId));
  Assert.Null(result.PlanningError);
  ```
- [x] Testar replanejamento com ID desconhecido/duplicado, cenas extras, palavra não dita, referência inventada e intenção de texto sem coordenadas. O modelo não preenche hashes, revisão, paths ou evidência visual; o host os deriva. Testar merge com identidade alterada via tarefa 2.
- [x] Rodar `dotnet test tests/DepoisDoSlogan.CortexPlugin.Tests/DepoisDoSlogan.CortexPlugin.Tests.csproj -p:CortexRoot=C:/dev/cortex --filter "FullyQualifiedName~EditorialDecisionPlannerTests|FullyQualifiedName~DraftWithDecisionsTests"`; observar falha.
- [x] Implementar as APIs e prompt. O node `draftSloganScript` acrescenta saídas `decisionsJson` e `planningError` à geração nova; graphs anteriores sem esses fios continuam compilando. A etapa lógica de planejamento acontece antes da pausa de revisão. Registrar `replanSloganEditing` como auxiliar de modelo. A geração inicial não faz uma segunda chamada de planejamento visual.
- [x] Fazer o pedido de imagem sair da receita de material, deduplicado por request key. Material compartilhado precisa ter composição/tamanho coerentes entre cenas; contradição vira erro de proposta. Rodar a suíte do plugin e atualizar somente expectativas de contratos realmente ampliados; commit `feat(slogan): draft scripts with editorial decisions`.

## Task 5: Versões no banco, autorização e coordenação sem resultados obsoletos

**Files:** Create `src/DepoisDoSlogan.Web/SloganStore.EditorialDecisions.cs`, `EditorialDecisionEndpoints.cs`, `VideoRuntimeManager.EditorialDecisions.cs`; modify `DepoisDoSlogan.Web.csproj`, `SloganStore.cs`, `VideoRuntimeManager.cs`, `VideoRuntimeManager.Spending.cs`, `ReferenceImageRequests.cs`, `ReferenceImageService.cs`, `SloganStore.ReferenceImages.cs`, `SloganWebHost.cs`, `Models.cs`. Test `EditorialDecisionStoreTests.cs`, `EditorialDecisionRuntimeTests.cs`, `EditorialDecisionAccessTests.cs` em `tests/DepoisDoSlogan.Web.Tests`.

**Interfaces:** `GetEditorialDecisionsAsync(videoId) -> EditorialDecisionState`; `TrySaveEditorialDecisionsAsync(videoId, plan, int expectedRevision, string expectedContextFingerprint) -> EditorialSaveResult`; `SaveDraftWithDecisionsAsync(videoId, script, plan?, error?)` persiste o par em transação; `TrySaveFocusResolutionAsync(videoId, resolution, int expectedPlanRevision, string expectedMediaHash)` verifica a mídia corrente. `EnsureEditorialDecisionsAsync(videoId, script, token, bool allowUnsaved=false, string? confirmationId=null)` usa cache ou proposta autorizada; `ReplanEditorialScenesAsync(videoId, beatIds, expectedRevision, confirmationId, token)` preserva overrides e descarta resultados antigos.

- [x] Escrever testes de reabertura, duas abas, transação do par, revisão/cenário antigo, troca de imagem durante seleção, falha persistida sem retry automático e render/proposta baseada em contexto antigo. Export/import usa a tarefa 2 e não altera uploads. Anônimo/viewer/outro proprietário são recusados; mutações sem CSRF falham.

  Teste `SecondTabCannotOverwriteRevisionEight`, com as duas abas lendo a revisão `7` e a primeira já tendo salvo:
  ```csharp
  Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
  Assert.Equal(8, reopenedPlan.Revision);
  Assert.Equal(1.6, reopenedHumanZoomScale);
  ```
- [x] Rodar `dotnet test tests/DepoisDoSlogan.Web.Tests/DepoisDoSlogan.Web.Tests.csproj -p:CortexRoot=C:/dev/cortex --filter "FullyQualifiedName~EditorialDecision"`; confirmar falha.
- [x] Implementar tabelas aditivas de versões e resoluções; guardar proposta, overrides, revisão, fingerprint e erro. O host Web referencia os tipos compartilhados de `Cortex.Video` pelo `CortexRoot`, sem acrescentar referência ao plugin do consumidor nem alterar o assembly CLI `DepoisDoSlogan`. Endpoints protegidos são `GET /api/videos/{id}/editing-plan`, `PATCH` no mesmo path, `POST /editing-plan/replan`, `PUT /editing-plan/focus/{beatId}`, `GET /editing-plan/export` e `POST /editing-plan/import`, todos sob o mesmo vídeo. PATCH recebe revisão esperada; stale responde `409`. DTOs de revisão retornam cenas, parâmetros, revisão e fingerprints, conservando paths internos no servidor; bundles de exportação usam referências relativas ao projeto, sem paths absolutos do servidor ou credenciais.
- [x] Integrar spending: geração inicial permanece na operação `step`, incluindo o tamanho da resposta com decisões na estimativa existente; replanejamento adiciona operação `editing-plan`. Testar cotação expirada, usada duas vezes e contexto/modelo alterado. Ajustes manuais, render de plano em cache e provider local não consomem uma nova aprovação paga. Não chamar replanejamento pago no startup, save ou polling.
- [x] Derivar `ReferenceVisualPlan` de adaptação das decisões efetivas para conservar a infraestrutura de referência durante a migração. Se não existir decisão nova, conservar o caminho legado; não gerar uma proposta paralela que discorde do novo plano. Rodar a suíte Web e commit `feat(slogan-web): persist reviewable editing decisions`.

## Task 6: Plano de edição revisável e seleção de foco no site

**Files:** Create `src/DepoisDoSlogan/App/wwwroot/editing-plan.mjs`, `image-focus-selector.mjs`; modify `app.mjs`, `session.mjs`, `app.css` e `index.html` apenas nos pontos de integração. Test `tests/frontend/editing-plan.test.mjs`, `image-focus-selector.test.mjs`, `preview-session.test.mjs`.

**Interfaces:** `renderEditingPlan(root, {scene, effective, planningState, image, onPatch, onReplan, onUseProposal})` mantém controles por beat e suas justificativas. `createImageFocusSelector(root, {url, naturalWidth, naturalHeight, existingRegion, onCommit, disabled})` converte seleção exibida para pixels da imagem natural; inclui entradas numéricas acessíveis e botão para aplicar. O adapter de sessão obtém/salva documentos com revisão esperada e encaminha o fluxo de custo existente no replanejamento.

- [x] Escrever testes para valores iniciais Cortex, indicador de origem humana, desligar zoom, usar proposta novamente, operação pending/error, conflito `409` e texto pendente. Conferir que atualizações assíncronas não apagam input em edição nem deslocam o foco do teclado.

  Teste `async refresh preserves an unsaved human zoom and keyboard focus`:
  ```javascript
  assert.equal(zoomInput.value, '1.6');
  assert.equal(document.activeElement, zoomInput);
  assert.equal(zoomInput.dataset.origin, 'human');
  ```
- [x] Testar imagem com letterboxing e escalada: um retângulo desenhado no preview vira coordenadas naturais corretas; bordas fora da imagem são recusadas. Os inputs numéricos permitem o mesmo resultado sem mouse. Trocar imagem descarta uma seleção ainda não aplicada e recarrega o estado válido.
- [x] Rodar `node --test tests/frontend/editing-plan.test.mjs tests/frontend/image-focus-selector.test.mjs`; observar falha, depois implementar as interfaces na identidade visual vigente usando impeccable antes da edição UI.
- [x] Integrar a seção por cena em Roteiro, mantendo falas e legenda nos controles atuais. Prévia de edição não salva envia roteiro e plano temporários, sem persistir o temporário sobre o plano salvo. Exportar projeto inclui os dois documentos; exportar somente roteiro continua disponível.
- [x] Rodar `node --test tests/frontend/*.test.mjs` e conferir desktop/celular em uma rodada de capturas, incluindo teclado, pending/error e foco selecionado; commit `feat(slogan-ui): review editorial plans and image focus`.

## Task 7: Renderer de movimento compartilhado no Cortex

**Files:** Create `src/Cortex.Video/Editing/ImageMotionRenderer.cs`; test `tests/Cortex.Video.Tests/ImageMotionRenderingTests.cs` e fixtures de imagem nesse projeto.

**Interfaces:** `ImageMotionRenderRequest` contém path raster, raiz de assets permitida, diretório de saída, `CompiledImageMotion`, tamanho da região de apresentação, FPS e cor de fundo validada. `IImageMotionRenderer.RenderAsync(request, token) -> ImageMotionFrames`; `ImageMotionFrames` informa pattern PNG RGBA, número de frames, dimensões e se a camada é estática. `FfmpegImageMotionRenderer(ffmpegExecutable)` executa comandos criados pelo compiler com `ProcessStartInfo.ArgumentList`, sem shell ou expressão fornecida por modelo.

- [x] Criar teste real com imagem colorida e região marcada: conferir opacidade de entrada, posição do foco e mudança do campo de visão nos frames inicial/intermediário/final. Caso `none` gera/reutiliza um frame estático. Dois movimentos diferentes sobre a mesma imagem não compartilham uma sequência errada.

  Teste `RendersSeventyTwoFramesForThreeSeconds`, com `24` FPS e saída vertical. `ImageMotionFrames` expõe `FrameCount`, `Width` e `Height`:
  ```csharp
  Assert.Equal(72, frames.FrameCount);
  Assert.Equal((540, 960), (frames.Width, frames.Height));
  Assert.InRange(firstFrameAlpha, 0, 8);
  ```
- [x] Rodar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter FullyQualifiedName~ImageMotionRenderingTests`; confirmar falha pela API ausente. FFmpeg é o binário local já utilizado; não baixar dependências.
- [x] Implementar geração de PNGs RGBA no FPS da prévia, usando a transformação da tarefa 3. Validar fronteira/reparse points da raiz, raster permitido e diretório de artefato; cancelar o subprocesso próprio e remover somente temporários dentro dessa saída. Sem consulta a modelo ou GPU gerador.
- [x] Rodar os casos vertical/horizontal/quadrado; amostrar os pixels esperados com tolerância de reamostragem e conferir o número exato de frames. Commit `feat(video): render image fade and zoom layers`.

## Task 8: Prévia consome o plano e invalida todas as dependências relevantes

**Files:** Modify `src/DepoisDoSlogan.CortexPlugin/ScriptPreviewImages.cs`, `ScriptPreviewRenderer.cs`, `ScriptPreviewPlanner.cs`, `SloganNodePlugin.cs`; `src/DepoisDoSlogan.Web/PreviewImageBindings.cs`, `VideoRuntimeManager.cs`, `SloganStore.cs`; `src/DepoisDoSlogan/App/wwwroot/session.mjs`. Test `tests/DepoisDoSlogan.CortexPlugin.Tests/EditorialPreviewTests.cs`, `tests/DepoisDoSlogan.Web.Tests/EditorialPreviewRuntimeTests.cs` e o teste frontend de validade da prévia.

**Interfaces:** O manifesto privado acrescenta referências validadas à revisão do plano e resoluções por beat; o plano é passado por novo parâmetro opcional `decisionsFile` do node de prévia. O renderer antigo continua aceitando seus overloads sem plano. A adaptação lê `Effective(plan)`, resolve o tempo local pelo `ScriptPreviewPlanner` e usa `IImageMotionRenderer` apenas nas camadas animadas. O fingerprint público continua uma string opaca, agora incluindo SHA-256 dos arquivos, plano efetivo, legendas e resolução do foco.

- [x] Escrever testes: mesmo upload em hook/question com movimento só no hook; foco pendente aparece no estado e não vira zoom central silencioso; overlay e legenda conservam tamanho; duração e resolução do arquivo final permanecem corretas. Sem decisões novas, o caminho estático legado continua funcionando.

  Teste `SamePostHasIndependentSceneMotionAndStableText`:
  ```csharp
  Assert.NotEqual(hookStartImageBounds, hookEndImageBounds);
  Assert.Equal(questionStartImageBounds, questionEndImageBounds);
  Assert.Equal(overlayBoundsBeforeZoom, overlayBoundsAfterZoom);
  ```
- [x] Rodar os filtros `EditorialPreviewTests` e `EditorialPreviewRuntimeTests` e observar falha. Testar invalidação por movimento, hash da imagem, seleção de mídia e legenda; um render concluído de revisão antiga não substitui a prévia atual.
- [x] Implementar composição de frames com `setpts` deslocado para o início da cena, preservando corte no final exclusivo. Instâncias animadas ficam agrupadas por receita e intervalo, e não apenas pelo path da imagem. Compor overlays e legendas depois da imagem, reutilizando os estilos e timestamps em andamento.
- [x] Integrar o estado pendente no DTO e na UI; uma prévia incompleta pode mostrar a camada estática com o aviso de foco pendente, conservando o plano que pede zoom. A execução do zoom só ocorre com resolução válida. O preview não recalcula proposta paga escondido nem começa uma geração ComfyUI por mudar a animação.
- [x] Rodar suítes do plugin/Web/frontend, conferir um vídeo real nos três formatos e commit `feat(slogan): preview resolved editorial image motion`.

## Task 9: Integração final, documentação e ativação no vídeo atual

**Files:** Update `docs/cortex-edit-v1.md` e exemplo no Cortex; `README.md`, `PRODUCT.md`, `docs/reference-visual-planning.md` no Depois do Slogan. Ferramentas de instalação/publicação existentes permanecem os entrypoints; helper temporário de QA fica em diretório de trabalho próprio.

- [x] Conferir cada critério da spec e executar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj`; no produto executar `dotnet test DepoisDoSlogan.slnx -p:CortexRoot=C:/dev/cortex`, a suíte Web completa e `node --test tests/frontend/*.test.mjs`. Relatar qualquer falha, inclusive independente desta feature, com nome e evidência.
- [x] Validar o schema portátil e o bundle de exportação, duas abas, reabertura, dados anteriores e ausência de chamadas de modelo no registro/carregamento dos plugins. Inspecionar o diff próprio contra snapshots dos dois checkouts para preservar o trabalho paralelo.
- [x] Conferir a interface em desktop/celular, autenticação pelo proprietário e o foco marcado. Gerar uma prévia real da abertura com frame a `0`, `0.8`, `1.5` e `2.9` s; verificar crescimento/foco da imagem, opacidade, textos estáveis e três proporções suportadas. Guardar artefatos de QA e a revisão do plano usada.
- [x] Publicar os plugins opcionais Video/Depois do Slogan e uma geração imutável do host usando os scripts existentes. Preservar args, data directory, perfil e keys; backup antes da migração aditiva. Health check local e rollback para a geração anterior se o host não ficar pronto. A ativação usa as confirmações de custo existentes; não iniciar inferência paga durante a publicação.
- [x] Aplicar a proposta ao vídeo atual pelo fluxo do produto. Usar a área da frase indicada no upload real como resolução humana identificada; conferir a prévia final e a preservação de arquivos. Deixar pendências de gravação explícitas e fechar ComfyUI apenas se ele tiver sido utilizado e estiver ocioso. Atualizar a documentação e registrar commits de integração próprios.

## Handoff

Recomendação: execução direta nesta sessão, usando `superpowers:executing-plans`. As tarefas dependem dos mesmos contratos e de mudanças concorrentes nos dois repositórios; checkpoints por tarefa dão verificação sem exigir novos agentes. Revisão independente em uma sessão nova só será iniciada com autorização específica de Helena.

Após a revisão deste plano, implementar em ordem, registrar o progresso nas caixas e manter a comunicação sobre comportamento, validação e pendências. A aprovação deste documento autoriza o método de execução escolhido; ela não altera a política de confirmação de custo que o produto já possui.


Execution evidence: `.tmp/editorial-implementation/ledger.md`; verified on the integrated roots and the current local product. No independent review session was started.
