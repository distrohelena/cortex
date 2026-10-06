# Cortex Editor

Editor local de graphs genéricos. Usa o canvas de `packages/flow-graph`, `TypedFlowCompiler` e `TypedFlowRunner`; os nodes adicionais são entregues por DLLs opcionais.

## Instalar e abrir

```powershell
cd C:/dev/cortex
./tools/install-cortex-editor.ps1
./tools/start-cortex-editor.ps1
./tools/start-cortex-editor.ps1 -Graph 'C:/caminho/graph.json' -Entry video
```

A instalação publica o editor e instala a DLL de vídeo separadamente. Uma instalação nova deixa Vídeo desativado. O editor mantém sua instalação e configurações em `%LOCALAPPDATA%/Cortex/editor/`:

- `installation.json`: localização do executável e diretório de manifestos/plugins.
- `settings.json`: preferências compartilhadas, plugins habilitados e suas opções.
- `install-<versão local>/`: executável, assets e subpastas de plugins.

Cada reinstalação publica em uma pasta nova e atualiza o descritor; preserva as settings existentes. Substituições dos JSONs são atômicas, com backups privados do conteúdo anterior. O leitor do SDK não substitui settings inválidas durante a leitura. Uma reparação explícita pelo editor preserva o original inválido antes de escrever.

O script `start-cortex-editor.ps1` abre o navegador local. `-NoOpen` permite iniciar apenas o host; `-Settings` e `-Port` são overrides explícitos para testes/instalações particulares. Por padrão, o host usa uma porta livre de loopback.

## Plugins

Em Settings → Plugins, escolha o plugin e configure suas opções. A tela mostra o estado ativo neste início e avisa quando uma configuração salva exige reiniciar.

O editor e os consumidores não referenciam `Cortex.Video` estaticamente. Quando desativado, o loader não abre sua DLL nem resolve/constrói seu tipo. Um manifesto JSON oferece nome/descrição/campos de configuração sem carregar código. Ao habilitar e reiniciar, o loader usa `AssemblyLoadContext`/`AssemblyDependencyResolver`, compartilhando os contratos da instância de `Cortex.Core` do host. [Referência oficial .NET](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support).

Plugins implementam `IFlowNodePlugin`, contrato versão 1, e entregam os `FlowNodeHandler` existentes. A lista de handlers é passada ao compilador/runner existentes. Versões incompatíveis, dependências ausentes e kinds duplicados/reservados são reportados por plugin.

O plugin Vídeo oferece somente `videoFile`, `transcribe` e `transcript`. Registrar seus nodes não executa ferramentas nem carrega um modelo. Transcribe confirma áudio/vídeo com ffprobe, extrai PCM mono 16 kHz com ffmpeg e transcreve em português com whisper.cpp. As opções de executável/modelo são locais. O backend real aparece no diagnóstico; GPU depende da distribuição/runtime instalado.

```json
{
  "schemaVersion": 1,
  "plugins": [
    {
      "id": "video",
      "enabled": false,
      "configuration": {
        "whisperExecutable": "",
        "modelFile": "",
        "ffmpegExecutable": "ffmpeg",
        "ffprobeExecutable": "ffprobe"
      }
    }
  ]
}
```

Caminhos relativos de um manifesto são resolvidos a partir dele. Caminhos de ferramentas/modelos são resolvidos contra a localização das settings, com nomes de comandos ffmpeg/ffprobe podendo usar o PATH. A pasta atual do consumidor não altera o perfil nem esses caminhos.

## Usar o editor

Abra um graph v3 ou adicione nodes do catálogo. Configure suas entradas, conecte fios de dados e de execução, valide e execute. A seleção de um node mostra suas propriedades e outputs tipados; objetos são apresentados por campos e listas de objetos como tabelas, com o schema disponível para inspeção. Fit ajusta o canvas; pan/zoom e arraste usam o componente existente.

Um graph com nodes indisponíveis continua visível e preservado. A execução é bloqueada até os plugins necessários estarem ativos. O editor preserva campos/metadata desconhecidos do documento e permite salvar o arquivo aberto ou baixar um JSON quando o arquivo foi importado pelo navegador.

Durante uma execução, mudanças de graph/settings ficam bloqueadas e Cancelar encerra a operação. A tela usa estados/resultados efetivos, não repete a inferência automaticamente e não faz cache de transcrições. Conteúdo de graph, plugin e transcript é renderizado como texto.

O host escuta somente em loopback; a sessão é inicializada pelo URL local aberto pelo launcher. O token é efêmero e não é salvo nas settings. APIs requerem essa sessão e mutações requerem a mesma origem. Reabra pelo launcher se a sessão da janela não corresponder ao host atual.

## Abrir por um consumidor

O SDK expõe `CortexEditorLauncher.Open(graphPath, entryId)`. O consumidor passa seu arquivo e encontra a instalação/perfil compartilhados. Nenhuma classe de vídeo precisa ser referenciada ou implementada no consumidor.

```powershell
cd C:/dev/depois-do-slogan
./tools/open-editor.ps1
./tools/open-editor.ps1 -Video 'C:/caminho/video.mp4'
./tools/open-editor.ps1 -Graph 'C:/caminho/graph.json'
```

O CLI do Depois do Slogan também usa o mesmo loader/settings. A composição de seu graph permanece no repositório separado; os nodes e seus testes de transcrição pertencem a `Cortex.Video`.

## Demonstração verificada

O perfil local desta máquina foi explicitamente preparado com Vídeo habilitado e a distribuição whisper.cpp v1.9.2 CUDA 12.4/modelo small existentes em `C:/dev/depois-do-slogan/.local/tools/`. Isso não altera o default desativado de uma instalação nova.

O vídeo público **Como recortar imagens no Wikimedia Commons**, de Wiki Movimento Brasil, tem 100,22 segundos. Foi transcrito integralmente pelo editor e pelo consumidor usando o plugin compartilhado, produzindo 23 segmentos e diagnóstico `using CUDA0 backend` na RTX 4090. [Fonte e atribuição](https://commons.wikimedia.org/wiki/File:Como_recortar_imagens_no_Wikimedia_Commons.webm), [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). As transcrições automáticas mantêm os erros do modelo; atribuição completa está no README do consumidor.

Artefatos locais do consumidor: `.local/runs/shared-plugin-verified/{graph.json,run.json,transcript.json,tools.log}`. A inspeção visual do editor/settings em desktop e viewport compacto e a execução real pela UI ficaram em `C:/dev/cortex/state/editor-qa/`. São dados privados/gerados, fora do Git.

## Verificação

```powershell
dotnet build Cortex.slnx
dotnet test Cortex.slnx --no-build --filter 'Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests'
dotnet test tests/Cortex.Editor.Tests/Cortex.Editor.Tests.csproj --no-build --filter Category=LocalSocket
node --test tests/architecture/*.test.mjs packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
```

Os testes de processo novo instalam a DLL em um perfil temporário e inspecionam assemblies realmente carregadas. Cobrem perfil ausente/default, estado explicitamente desativado e habilitado. Testes de APIs locais são executados explicitamente, separados da seleção offline. Testes do módulo usam fixtures para transcrição; a demonstração descrita acima usa o vídeo e o modelo reais.
