# Helengine Video and Audio Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Preserve Helena's choice of execution method. Independent review requires her explicit approval and is not included automatically.

**Goal:** Fazer a Helengine renderizar a composição audiovisual completa e entregar apenas quadros e áudio finais ao encoder FFmpeg.

**Architecture:** Um contrato versionado e um relógio comum alimentam o compositor DirectX 11 e um mixer determinístico. Os adaptadores de mídia ficam na engine; o Cortex compila suas decisões para esse contrato. Prévia e exportação usam a mesma avaliação, com um adaptador final de codificação.

**Tech Stack:** C#/.NET 9 na Helengine; C#/.NET 10 no Cortex e Depois do Slogan; SharpDX 4.2.0 existente; HLSL; C++ para o backend de leitura nativo; xUnit; FFmpeg como encoder externo.

**Spec:** [Composição completa de vídeo e áudio na Helengine](../specs/2026-10-07-helengine-video-composition-design.md), aprovada por Helena em 2026-10-07, commit `009e391`.

## Global Constraints

- Edição, composição visual e mistura de áudio pertencem à Helengine; o executável FFmpeg apenas codifica e empacota o resultado.
- O catálogo inicial inclui cortes, transformação 2D, opacidade, imagens, vídeo, texto/legendas, máscaras, overlays, crossfade visual e ganho/fades/crossfade de áudio.
- Intervalos são fechados no início e abertos no fim; frame rate racional e índices inteiros de amostras não acumulam deriva.
- Configuração inicial de áudio: PCM float, 48 kHz, estéreo. Ganhos acima do limite geram diagnóstico; não há normalização silenciosa.
- Preservar a curva smoothstep do zoom existente, os materiais, as referências geradas, a autoria e os ajustes humanos.
- Uma classe por arquivo na Helengine, XML comments substantivos em todos os membros, nullable desabilitado, sem tuples, helpers locais ou alterações em código gerado.
- Builds, dependências preparadas, logs e fixtures ficam em diretórios visíveis sob `C:/dev/helworks/builds/helengine/media-composition/`. Não direcionar saídas para `%TEMP%`.
- Não capturar a tela de Helena nesta tarefa. A inspeção usa arquivos renderizados e testes de componentes.
- Implementar em worktrees isolados dos três repositórios, preservando trabalho concorrente. Registrar decisões e comandos no ledger. Não iniciar agentes ou revisão independente sem autorização aplicável.
- A migração só termina quando prévia e montagem de gravações passam pela Helengine. Código legado pode permanecer acessível explicitamente para compatibilidade, mas não servir de fallback silencioso.

## Review Focus

1. Clipes VFR, timestamps negativos, B-frames e atraso de codec: selecionar quadros por tempo e conservar sincronismo. Tarefas 2, 3 e 12.
2. Seek repetido durante reprodução: limpar buffers antigos e obter o mesmo quadro e bloco de áudio da exportação. Tarefas 4, 6 e 8.
3. Material ou plano mudando enquanto um job termina: descartar resultado obsoleto e manter a prévia vigente. Tarefa 11.
4. Alpha, orientação, espaço de cor e tamanho de prévia: preservar composição proporcional e impedir halos ou gamma incorreto. Tarefas 3, 5 e 9.
5. Cancelamento ou erro nativo/encoder com buffers ocupados: liberar recursos, não travar nem publicar arquivos incompletos. Tarefas 2, 8, 9 e 12.

## Repositórios e estrutura

`H` significa `C:/dev/helworks/helengine`; `C` significa `C:/dev/cortex`; `S` significa `C:/dev/depois-do-slogan`. Comandos de cada tarefa são executados no respectivo worktree.

- `H/engine/helengine.media`: contratos JSON, relógio, avaliação de propriedades, catálogo, mixer, interfaces de fontes e saída. Sem dependência de editor, Cortex ou DirectX.
- `H/engine/helengine.media.windows`: fontes Windows, compositor GPU, reprodução e encoder externo. Reutiliza legendas e shaders da engine.
- `H/engine/helengine.video.ffmpeg`: implementação nativa da ABI existente e leitura de áudio. Usa bibliotecas de codec para leitura; não contém grafo editorial nem filtros.
- `H/engine/helengine.media.tests` e `helengine.media.windows.tests`: testes puros e de integração.
- `H/engine/helengine.vfx.cli`: acrescentar comandos de composição preservando efeitos e legendas existentes.
- `C/src/Cortex.Video/Composition`: catálogo consumido, compiler e cliente do CLI. O protocolo JSON evita acoplar a biblioteca opcional Video ao editor da Helengine.
- `S/src/DepoisDoSlogan.CortexPlugin` e `S/src/DepoisDoSlogan.Web`: integração, edição e persistência. Dividir o renderer atual por responsabilidade ao substituir sua composição FFmpeg.

As tarefas dependem do contrato e relógio comuns; formar uma entrega integrada. Não começar três implementações concorrentes que redefinam esses contratos separadamente.

## Task 1: Contrato da composição e relógio

**Files:** criar em `H/engine/helengine.media/` os projetos e arquivos `CompositionDocument.cs`, `MediaTime.cs`, `CompositionClock.cs`, `VisualLayer.cs`, `AudioClip.cs`, `MediaReference.cs`, `CompositionValidator.cs`, `Schemas/helengine.media.composition.v1.schema.json`; criar projetos de testes com o padrão de `helengine.vfx.tests`. Cada tipo auxiliar tem arquivo próprio.

**Interfaces:** `MediaTime(long numerator, long denominator)` normalizado; `CompositionClock.FrameTime(long frameIndex)`; `CompositionClock.SampleTime(long sampleIndex)`; `CompositionValidator.Validate(CompositionDocument document, MediaCapabilities capabilities)` retorna diagnósticos estruturados. Documento tem `schema`, `id`, `revision`, `width`, `height`, `frame_rate`, `duration`, `audio`, `media`, `layers`, `audio_clips`, `transitions`. Áudio pode ser desabilitado explicitamente.

- [ ] Escrever `CompositionClockTests.NoDriftAt30000Over1001`, `IntervalsAreEndExclusive` e `CompositionContractTests.RejectsUnknownEffectAndInvalidReference`: quadro 30000 em 30000/1001 é exatamente 1001 s; amostra 48000 é 1 s; fim exclusivo; rejeitar denominador zero, NaN, IDs duplicados, dimensão inválida e efeito sem versão.
- [ ] Rodar `dotnet test engine/helengine.media.tests/helengine.media.tests.csproj -v minimal`; observar falha pelas interfaces ausentes.
- [ ] Implementar tempo racional com aritmética verificada, limites de memória e schema `helengine.media.composition.v1`. Duração determina `ceil(duration*fps)` quadros e `ceil(duration*sampleRate)` amostras; nunca derivar duração de relógio de parede.
- [ ] Repetir o comando; exigir sucesso e round trip JSON preservando tempos.
- [ ] Commitar só contrato, schema e testes: `feat(media): add composition contract and rational clock`.

## Task 2: Backend real de leitura de vídeo

**Files:** criar `H/engine/helengine.video.ffmpeg/helengine.video.ffmpeg.vcxproj`, `include/helengine_video_ffmpeg.h`, `src/VideoDecoderExports.cpp`, `src/VideoFileProbeExports.cpp`, `build-native.ps1`; modificar `H/engine/helengine.directx11.video/FfmpegNativeApi.cs` e `DirectX11VideoDecoder.cs`; criar `H/engine/helengine.media.windows.tests/NativeVideoDecoderTests.cs`.

**Interfaces:** implementar os exports existentes `he_video_decoder_create`, `try_get_frame`, `seek`, `flush`, `release_frame`, `destroy`. `try_get_frame` retorna 1 para quadro, 0 para EOF e -1 para erro; acrescentar diagnóstico nativo e tratar -1 no wrapper. Preservar layout dos structs e regras `RequireHardware`, `PreferHardware`, `DisableHardware`.

- [ ] Criar fixtures curtas H.264 com B-frames, VFR e timestamp de origem não zero; escrever `DecodesThroughEndAndSeeksByPts`, `NativeErrorDoesNotBecomeEof`, `FramesReleasedBeforeDecoder` e `RequireHardwareDoesNotSilentlyFallBack`.
- [ ] Rodar `dotnet test engine/helengine.media.windows.tests/helengine.media.windows.tests.csproj --filter NativeVideoDecoderTests -v minimal`; observar falta da DLL nativa ou funcionalidades.
- [ ] Implementar demux/decode com libavformat/libavcodec, drenagem no EOF e seek seguido de flush. D3D11VA fornece NV12 na GPU; conversão GPU torna o quadro amostrável. Software só é permitido pelo modo explícito existente. Preparar SDK de leitura em diretório de build, registrar origem, versão, arquitetura, licenças e hashes; não presumir que `ffmpeg.exe` contém headers/import libraries. Build usa `FFmpegSdkRoot` explícito e copia DLLs para publicação.
- [ ] Executar `engine/helengine.video.ffmpeg/build-native.ps1 -OutputDirectory <build-root>/native` e repetir testes com a DLL publicada. Comparar PTS, frame count e seek, inclusive após EOF.
- [ ] Commitar implementação e testes: `feat(video): implement native video decoding for compositions`.

## Task 3: Fontes de imagem, vídeo e áudio

**Files:** criar em `H/engine/helengine.media/` `IMediaSource.cs`, `MediaVideoFrame.cs`, `IVideoSurface.cs`, `AudioBlock.cs`, `AudioFormat.cs`, `IMediaSourceResolver.cs`; em `helengine.media.windows/` criar `WindowsMediaSourceResolver.cs`, `ImageMediaSource.cs`, `VideoMediaSource.cs`, `AudioMediaSource.cs`; acrescentar `AudioDecoderExports.cpp` e structs/exports tipados no backend nativo; testes `MediaSourceTests.cs`.

**Interfaces:** `IMediaSource.ReadVideoFrame(MediaTime sourceTime)` devolve quadro proprietário descartável; `ReadAudio(long sourceSampleIndex, int sampleCount, AudioFormat target)` devolve bloco PCM interleaved com primeiro índice e frames válidos. `IMediaSourceResolver.Open(MediaReference reference)` verifica raiz, hash e metadados. Cada cursor de reprodução tem estado próprio.

- [ ] Escrever `ImageOrientationAndAlphaPreserved`, `DifferentCursorsDoNotShareSeekState`, `VideoAudioUsesSameSourceOrigin`, `Mono44100BecomesStereo48000`, `MissingAudioIsExplicitSilence` e `RejectsStaleHashOrEscapedPath`.
- [ ] Rodar suíte Windows com filtro `MediaSourceTests`; confirmar falhas.
- [ ] Implementar imagens PNG/JPG, vídeo e áudio embutido por timestamps. Usar backend nativo para PCM de AAC/WAV/MP3 e conversão por fonte para taxa/canais de destino; compensar atraso de decodificação/resampling e definir matriz mono→estéreo. Não misturar trilhas no decoder. Respeitar rotação e metadados de cor, limitar buffers e preservar alpha de PNG.
- [ ] Repetir testes; áudio de cada fixture deve coincidir com sua origem e não alterar resultados de outro cursor.
- [ ] Commit: `feat(media): add owned image video and audio sources`.

## Task 4: Avaliação de camadas e animações

**Files:** criar `H/engine/helengine.media/CompositionEvaluator.cs`, `LayerEvaluation.cs`, `PropertyAnimation.cs`, `AnimationKeyframe.cs`, `MediaCurve.cs`, `PresentationTransform.cs`; testes `CompositionEvaluationTests.cs`.

**Interfaces:** `CompositionEvaluator.Evaluate(CompositionDocument document, MediaTime time)` retorna camadas ativas em ordem estável e tempos de fonte. `MediaCurve.Evaluate(string id, double progress)` inclui `linear.v1` e `smoothstep.v1`; `PresentationTransform.Resolve(...)` mapeia fit e foco para o viewport.

- [ ] Escrever `RepeatedSeekIsDeterministic`, `SmoothstepQuarterEquals015625`, `FitBeforeZoomPreservesPostEdges`, `TextLayerDoesNotInheritImageScale`, `InactiveLayerNotSampled` e `KeyframesRequireStrictOrder`.
- [ ] Rodar testes puros com filtro `CompositionEvaluationTests`; confirmar falhas.
- [ ] Implementar avaliação independente da ordem das consultas, coordenadas normalizadas, posição/escala/rotação/opacidade e clamp da região de amostragem. Catálogo inicial só admite efeitos sem histórico. Aplicar fit antes de zoom, como a referência já corrigida do post.
- [ ] Repetir testes, incluindo uma imagem 685×898 em viewport vertical com região perto das bordas.
- [ ] Commit: `feat(media): evaluate deterministic layer animations`.

## Task 5: Compositor GPU, texto, máscaras e catálogo

**Files:** criar em `H/engine/helengine.media/` `MediaCapabilities.cs`, `MediaEffectDescriptor.cs`, `IMediaCompositor.cs`, `RenderSize.cs`; em `helengine.media.windows/` `DirectX11MediaCompositor.cs`, `DirectX11LayerPass.cs`, `CaptionLayerSource.cs`, `shaders/MediaComposite.hlsl`, `shaders/MediaTransform.hlsl`, `shaders/MediaMask.hlsl`; modificar `helengine.vfx.directx11/DirectX11VfxEffectRunner.cs` apenas para extrair/reutilizar um pass quando necessário. Testes `GpuCompositionTests.cs`.

**Interfaces:** `IMediaCompositor.Render(CompositionDocument document, MediaTime time, RenderSize size)` retorna `MediaVideoFrame`; `MediaCapabilities.Describe()` publica IDs/versões, propriedades, ranges, tipos de input e schema. Efeitos de imagem existentes entram por adaptador tipado, sem aceitar HLSL do chamador.

- [ ] Escrever `OverlayOrderAndAlpha`, `MaskIsAppliedBeforeComposite`, `CaptionAtAbsoluteTime`, `PreviewUsesProportionalLayout`, `TransparentEdgeHasNoDarkHalo` e `UnknownEffectFailsBeforeGpuAllocation`.
- [ ] Rodar testes GPU; observar falhas nos novos casos e manter a suíte VFX existente como regressão da extração.
- [ ] Implementar passes GPU, texturas persistentes, transformações, normal alpha-over, máscara alpha/grayscale e fontes de texto usando `CaptionRenderer`. Definir entradas sRGB, composição em espaço linear e saída sRGB; normalizar alpha internamente de forma consistente. Um zoom altera apenas a camada escolhida.
- [ ] Verificar pixels determinísticos nas bordas, opacidade e máscaras, tolerância máxima de 1 unidade em RGBA8; gerar arquivos de frames de teste para inspeção, sem screenshots da tela.
- [ ] Commit: `feat(media): compose animated layers captions and masks on gpu`.

## Task 6: Mixer de áudio determinístico

**Files:** criar `H/engine/helengine.media/CompositionAudioMixer.cs`, `AudioEnvelope.cs`, `AudioMixResult.cs`, `AudioPeakReport.cs`; testes `CompositionAudioMixerTests.cs`.

**Interfaces:** `CompositionAudioMixer.Render(CompositionDocument document, long firstSample, int sampleCount)` retorna `AudioMixResult` com PCM, intervalo e relatório de pico. Voz, música, áudio de clipe e SFX usam o mesmo caminho; silêncio não é erro quando declarado.

- [ ] Escrever `TwoTracksSumWithoutNormalization`, `FadeAtExactSample`, `EndExclusiveDoesNotDuplicateSample`, `MutedTrackDoesNotDecode`, `RandomBlockOrderMatchesWholeRender` e `CrossfadeEqualPowerAvoidsDip`: ganho unitário de duas fontes 0.75 produz 1.5 e diagnóstico; silêncio produz zeros.
- [ ] Rodar testes com filtro `CompositionAudioMixerTests`; confirmar falhas.
- [ ] Implementar mistura por amostra com ganho, mute, envelopes lineares e crossfade equal-power explícito. Acumular em precisão suficiente antes de PCM float; ganho não é clampado silenciosamente. Resampling pertence à fonte da tarefa 3, não ao mixer.
- [ ] Comparar render em blocos de 1024 com consulta inteira e seek fora de ordem; diferença máxima 1e-6 e mesma contagem de amostras.
- [ ] Commit: `feat(media): mix timeline audio with sample accurate envelopes`.

## Task 7: Transições entre cenas e grupos

**Files:** criar `H/engine/helengine.media/CompositionTransition.cs`, `TransitionEvaluation.cs`; criar `H/engine/helengine.media.windows/shaders/MediaCrossfade.hlsl`; modificar validator, evaluator e compositor; testes `CompositionTransitionTests.cs` e `GpuTransitionTests.cs`.

**Interfaces:** transição contém `from_layer`, `to_layer`, `start`, `duration`, `effect_id`, `effect_version`, `parameters`. IDs podem referenciar grupos; grupos avaliam as suas camadas antes da transição. `TransitionEvaluation.At(...)` resolve progressão e fontes exigidas.

- [ ] Escrever `CrossfadeEndpointsAndMidpoint`, `InsufficientSourceHandlesRejected`, `TransitionDoesNotShortenTimeline`, `GroupTransitionIncludesItsOverlays` e `PictureCrossfadeDoesNotForceAudioFade`.
- [ ] Rodar testes das duas classes; confirmar falhas.
- [ ] Implementar crossfade visual com progressão normalizada em intervalo explícito; áudio cruza apenas quando sua edição declara o crossfade da tarefa 6. Rejeitar sobreposições cíclicas ou duas transições incompatíveis sobre a mesma camada.
- [ ] Verificar quadros no início, meio e fim, duração total e fontes de entrada suficientes.
- [ ] Commit: `feat(media): add timed scene and layer group transitions`.

## Task 8: Reprodução e seek usando o mesmo render

**Files:** criar `H/engine/helengine.media/CompositionPlaybackSession.cs`, `IAudioOutput.cs`, `PlaybackSnapshot.cs`; em `helengine.media.windows/` `WindowsCompositionAudioOutput.cs`; testes `CompositionPlaybackTests.cs`.

**Interfaces:** sessão recebe compositor, mixer, áudio de saída e composição; expõe `Play()`, `Pause()`, `Seek(MediaTime time)`, `ReadPreviewFrame(RenderSize size)`, `Dispose()`. Saída reporta quantidade de amostras efetivamente consumidas; relógio da sessão usa esse número quando há áudio e relógio monotônico quando há silêncio.

- [ ] Escrever `PauseSeekResumeDoesNotPlayOldBuffer`, `DroppedPictureDoesNotDropAudio`, `PreviewMatchesOfflineAtSameTime` e `DisposeDuringBlockedWriteReleasesSources` com áudio de saída falso.
- [ ] Rodar suíte pura para a sessão; confirmar falhas.
- [ ] Implementar um stream do mixer para NAudio/Windows, buffers limitados e geração de seek: limpar fila, reposicionar fontes e ignorar callbacks antigos. Não reproduzir cada trilha em um dispositivo separado como faz o backend de gameplay.
- [ ] Repetir testes e usar uma fixture local com impulso/flash sincronizados para exercitar a saída Windows; não gravar áudio do sistema como exportação.
- [ ] Commit: `feat(media): preview compositions with synchronized audio and seeking`.

## Task 9: Exportador e protocolo CLI

**Files:** criar `H/engine/helengine.media/ICompositionEncoder.cs`, `CompositionExporter.cs`, `CompositionRenderReport.cs`, `OutputProfile.cs`; em `helengine.media.windows/` `FfmpegCompositionEncoder.cs`, `FfmpegEncodeArguments.cs`; criar `H/engine/helengine.vfx.cli/CompositionCliRunner.cs`, modificar `VfxCliRunner.cs`, projeto e help. Testes `CompositionExportTests.cs`, `FfmpegEncodeArgumentsTests.cs`, `CompositionCliTests.cs`.

**Interfaces:** encoder tem `Begin(profile)`, `WriteVideoFrame(frame, index)`, `WriteAudioBlock(block)`, `Complete()` e abort/Dispose. `CompositionExporter.ExportAsync(document, profile, output, token)` retorna relatório. CLI: `composition capabilities --json`; `composition validate --input <json> --assets-root <root>`; `composition render --input <json> --assets-root <root> --out <file> --profile <id>`; `composition frame --input <json> --time <rational> --out <png>`.

- [ ] Escrever `EncoderReceivesComposedFramesAndMixedPcmOnly`, `NoEditorialFiltersInArguments`, `CancelDoesNotPublish`, `SlowEncoderIsBounded`, `Mp4FlattensAlphaAndMovRetainsIt` e testes de JSON/códigos de saída.
- [ ] Rodar testes novos, incluindo captura de argumentos com processo falso; confirmar falhas.
- [ ] Implementar saída contínua com dois named pipes Windows e backpressure; logs limitados e encerramento de processo no cancelamento. Perfis iniciais: `mp4-h264-aac.v1` e `mov-prores4444-pcm.v1`; validate codec availability antes de render. Engine prepara RGBA/fundo, duração e PCM; FFmpeg apenas lê entradas e codifica. Publicar arquivo final e relatório somente após sucesso.
- [ ] Renderizar fixture de 2 s: 48 quadros a 24 fps e 96000 amostras PCM pré-encode. Inspecionar MP4/MOV, descontando padding documentado do codec, e repetir suíte CLI existente.
- [ ] Commit: `feat(media): export complete compositions through encoding only ffmpeg`.

## Task 10: Compiler e catálogo no Cortex

**Files:** criar `C/src/Cortex.Video/Composition/HelengineCapabilities.cs`, `HelengineCompositionCompiler.cs`, `HelengineCompositionClient.cs`, `HelengineCompositionResult.cs`, schema externo versionado da engine; ampliar contratos editoriais em arquivos específicos sob `Editing/` para `cortex.edit.decisions.v2`; testes `HelengineCompositionCompilerTests.cs`, `HelengineCapabilitiesTests.cs`, `EditorialDecisionV2Tests.cs`.

**Interfaces:** `HelengineCompositionCompiler.Compile(EditScriptDocument script, EditorialDecisionPlanDocument decisions, ResolvedEditPlanDocument resolved, HelengineCapabilities capabilities)` retorna composição JSON e pendências. Um overload de planejamento aceita bindings de imagens e tempos estimados, marcando mídia ausente; exportação exige resolução. `HelengineCompositionClient.RenderAsync(JsonElement composition, string assetsRoot, string output, string profile, CancellationToken token)` chama o CLI sem shell.

- [ ] Escrever `V1ZoomCompilesToSmoothstepV1`, `V2TransitionAndAudioCompile`, `PendingFocusIsNotInvented`, `CatalogVersionPinned`, `HumanOverrideSurvivesCatalogRefresh`, `ResolvedSpeechTimingRequiredForFinal` e `GeneratedDocumentPassesEngineValidation`.
- [ ] Rodar `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter "Helengine|EditorialDecisionV2" -v minimal`; confirmar falhas.
- [ ] Criar v2 aditivo para transformação/keyframes, lista de efeitos, máscara, transição e edição de áudio, preservando leitura de v1 e não convertendo projetos automaticamente. Validar parâmetros contra capacidades tipadas, não aceitar código ou caminhos do modelo. Copiar schema publicado sem reescrever saída gerada. Compiler mapeia mídia resolvida e respeita região/foco, tempo e overrides.
- [ ] Repetir suíte Video e validar as composições de teste pelo CLI real. Confirmar que projetos v1 continuam com o mesmo conteúdo e curva.
- [ ] Commit: `feat(video): compile editorial decisions to helengine compositions`.

## Task 11: Migrar prévia, gravações, editor e persistência

**Files:** modificar `S/src/DepoisDoSlogan.CortexPlugin/ScriptPreviewRenderer.cs`, `SloganNodePlugin.cs`, `EditorialDecisionPlanner.cs`, `ScriptPreviewImages.cs`; criar `SloganCompositionBuilder.cs` e `HelengineScriptPreviewRenderer.cs` nessa pasta; ampliar `S/src/DepoisDoSlogan.Web/PreviewImageBindings.cs`, `VideoRuntimeManager.cs`, `SloganStore.cs`, `Models.cs`, endpoints e controles `S/src/DepoisDoSlogan/App/wwwroot/editing-plan.mjs`. Em `C/src/Cortex.Video/`, criar `HelengineCutRenderer.cs` e modificar `VideoNodePlugin.cs`. Acrescentar testes nos projetos existentes, `S/tests/DepoisDoSlogan.CortexPlugin.Tests/CompositionPreviewTests.cs` e `S/tests/frontend/editing-plan.test.mjs`.

**Interfaces:** manter `IScriptPreviewRenderer` e `IVideoCutRenderer`; usar novo renderer por padrão na instalação. Adicionar migração SQL de `has_audio` e fingerprint de composição em previews e CAS `TryAddCurrentPreviewAsync(...)` com revisão/hash de roteiro, plano e mídias capturados no início. Dados antigos têm `has_audio=false`.

- [ ] Escrever `PlanningPreviewUsesOnlyAvailableAudio`, `RecordedVideoUsesEngineForCuts`, `PersistedPreviewRestoresHasAudio`, `StaleCompletionCannotReplaceCurrentPreview`, `V1HumanEditsPreserved` e `RenderNeverStartsPaidPlanning`; criar teste JS para edição de transição e ganho com valores não salvos preservados.
- [ ] Rodar os testes novos de plugin/Web/Video e `node --test tests/frontend/*.test.mjs` a partir de S; observar falhas antes de modificar comportamento.
- [ ] Substituir composição FFmpeg por compilação + CLI Helengine. Texto e legendas viram camadas; cenas sem áudio permanecem em silêncio explicitamente. Planner recebe catálogo permitido, gera v2 para novos recursos e respeita confirmação de gasto existente. UI mostra controles do catálogo inicial, pendências e estado do áudio; seleção de materiais para trilha passa pelo acesso autenticado já existente.
- [ ] Validar preview/job com gravação real e tomada selecionada, verificar descarte atômico de render obsoleto e ausência de chamadas a modelos para edição manual/render. Repetir suítes alteradas. `FfmpegCutRenderer` e `FfmpegImageMotionRenderer` não são chamados pelo novo caminho.
- [ ] Commitar mudanças por repositório: `feat: render video and audio compositions with helengine`.

## Task 12: Integração, publicação local e aceitação

**Files:** criar `H/engine/helengine.media.windows.tests/CompositionAcceptanceTests.cs`; criar `H/tools/publish-media-composition.ps1`; atualizar `S/tools/install-cortex-plugin.ps1` e documentação dos três repositórios. Fixtures próprias ficam no diretório de build e têm hashes/manifesto; não entrar uploads reais nos commits.

- [ ] Escrever projeto de aceitação: 6 s com clipe falado/impulso sincronizado, post com fade/zoom, legenda, overlay, máscara, crossfade de 0.5 s e música com envelope. Testar `FullCompositionKeepsAvSyncAcrossFormats`, `NativePackageWorksOutsideCheckout`, `EncoderFailureAndCancelLeaveNoPublishedOutput`.
- [ ] Rodar teste com o pacote em diretório limpo; confirmar falhas de empacotamento ou integrações faltantes.
- [ ] Publicar contratos, shaders, runtime nativo e dependências; preservar configurações, credenciais, biblioteca e argumentos reais do site. Atualizar Cortex.Video e plugin/site sem quebrar processos ainda usando versões antigas. Documentar presets, limites, mensagens de erro e comando de render.
- [ ] Exigir 144 quadros/288000 amostras antes do codec na fixture de 6 s; renderizar 540×960, 960×540 e 540×540 em MP4 e MOV. Medir flash/impulso no mesmo tempo com tolerância de 1 quadro e 1 amostra no PCM; validar preview/offline em pontos fixos e inspecionar quadros e som em artefatos. Confirmar HTTP local e comparar inventário de usuários/materiais/refs antes/depois. Rodar apenas suites afetadas de engine, CLI, Cortex.Video, plugin, Web e frontend. Registrar logs e contagens reais no ledger.
- [ ] Commitar scripts/docs/testes próprios. Integrar conforme autorização vigente; não fazer push nem iniciar revisão independente automaticamente.

## Self review and handoff

Cobertura: contrato e relógio (1); leitura real e ciclo de vida nativo (2–3); animações/foco (4); camadas, máscara, texto e efeitos (5); áudio (6); transições (7); prévia/seek (8); exportação (9); autoria/Cortex (10); site e montagem de gravações (11); formatos, implantação e teste completo (12).

As cinco condições de Review Focus têm testes nomeados nas tarefas donas do código. Os três projetos usam o mesmo protocolo, os mesmos identificadores de curvas e os mesmos intervalos. A implementação nativa ainda não existe neste checkout e faz parte da entrega, não é um pré-requisito presumido.

Leitura de codecs segue a [API de decodificação oficial](https://ffmpeg.org/doxygen/trunk/group__lavc__encdec.html), incluindo drenagem e flush; conversão por fonte segue [libswresample](https://ffmpeg.org/doxygen/trunk/group__lswr.html). Essas bibliotecas de leitura permanecem internas à engine; composição e mistura de trilhas são implementadas nas tarefas 5–7.

Método proposto: executar nesta sessão sem agentes, conforme o modo usado anteriormente. Revisão independente só se Helena solicitar ou aprovar separadamente. Antes da primeira alteração de código, ela revisa este plano; depois disso executar todas as tarefas sem pedir autorização a cada etapa.
