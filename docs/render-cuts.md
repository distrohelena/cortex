# Render Cuts

O node `renderCuts`, exibido como **Render Cuts**, pertence à DLL opcional `Cortex.Video`. Usa o catálogo, tipos, compilador e runner do Cortex e as opções existentes `ffmpegExecutable` e `ffprobeExecutable` do plugin.

```text
video + editPlan → Render Cuts → video { path, duration, timeline }
```

`video` é o objeto `{ path }` de **Video File**. O plano pode vir da porta `editPlan` de **Suggest Takes** ou de um JSON exportado e editado manualmente, indicado em **Edit plan file (when not connected)**. Use apenas uma dessas fontes. **Output directory** define a pasta onde cada execução cria um subdiretório novo com `cut.mp4` e `rendered-video.json`.

O renderer lê `candidates`, mantém os itens com `keep: true` e respeita a ordem do array. Usa `source.start` e `source.end` em segundos no vídeo original. Recalcula a timeline a partir desses valores; o campo `proposedTimeline` do plano não é usado para executar cortes. Assim, editar `keep`, intervalos ou a ordem dos candidatos altera o resultado sem deixar a timeline antiga como autoridade.

O vídeo é recodificado localmente para H.264 e o áudio para AAC, preservando a resolução e a taxa de frames da origem. Tempos de vídeo são quantizados pelos frames: a duração medida em `duration` pode diferir ligeiramente da soma dos intervalos em `timeline`. A saída só é publicada depois de verificar os streams e a duração com ffprobe. O original e resultados anteriores permanecem intactos. Cancelamento encerra a árvore de processos; falhas não repetem automaticamente a renderização.

O recorte aceita planos versão 1, base `sourceSeconds`, até 32 candidatos e pelo menos um candidato selecionado. IDs devem ser únicos e os intervalos selecionados finitos, positivos e contidos no vídeo. O arquivo precisa conter vídeo e áudio. O node não exige transcrever novamente nem chamar Jev quando recebe um plano salvo.

## Executar a partir de um JSON salvo

No Depois do Slogan:

```powershell
./tools/render-cuts.ps1 -Video 'C:/caminho/video.mp4' -EditPlan 'C:/caminho/edit-plan.json'
./tools/render-cuts.ps1 -Video 'C:/caminho/video.mp4' -EditPlan 'C:/caminho/edit-plan.json' -Editor
```

O consumidor compõe somente **Video File → Render Cuts** nesse modo. O CLI grava `graph.json`, `run.json`, `tools.log` e `rendered-video.json` numa pasta nova. No editor, o output tipado `video` informa o caminho do MP4, a duração medida e a timeline efetivamente usada.

A escrita de arquivo usa o efeito `WriteState` existente; o kernel o trata como um passo `Artifact`, separado de passos puros e inferência. Este renderer executa no host local do editor/CLI.

Este incremento entrega cortes diretos com áudio. O vídeo produzido ainda é uma amostra da proposta editorial para revisão humana.
