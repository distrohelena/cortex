# Editor próprio do Cortex e plugins DLL opcionais

## Objetivo e limite

O Cortex terá seu próprio editor de graphs, usando o componente `packages/flow-graph` e o compilador/runner existentes. Os nodes `videoFile`, `transcribe` e `transcript` serão reutilizáveis por qualquer consumidor, distribuídos em `Cortex.Video.dll`. O Depois do Slogan abre o editor passando seu graph; usa automaticamente as settings locais compartilhadas do Cortex.

Este recorte contém o editor, a configuração de plugins e o mesmo fluxo de um único vídeo. Não inclui outros nodes de mídia, edição automática, agentes, lotes de vídeos, cache de transcrição ou integração com editores de vídeo.

## Comportamento acordado

- A DLL de vídeo não faz parte das referências estáticas do editor ou do Depois do Slogan e não é carregada por padrão.
- Configurações → Plugins permite habilitar Vídeo e configurar os caminhos das ferramentas/modelo.
- As settings são persistidas em JSON local compartilhado. Abrir o editor por outro consumidor ou outra pasta mantém a configuração escolhida.
- Um plugin habilitado é carregado dinamicamente, registrando seus nodes com os contratos do Cortex.
- Registrar nodes não inicia ffmpeg/Whisper nem carrega um modelo na GPU. Isso ocorre na execução do node Transcribe.
- Um graph com plugin desativado continua preservado e visível, com indicação de dependência indisponível. Sua execução é bloqueada com um diagnóstico claro.
- Para este primeiro recorte, habilitar/desabilitar uma DLL aplica-se ao reiniciar o editor. A tela informa quando há uma mudança pendente. Não há promessa de descarregamento imediato de uma DLL já em uso.

## Implementação deste recorte

### Contratos e carregamento

`Cortex.Core` mantém `GraphDocument`, `FlowNode`, `NodeDefinition`, `FlowType`, `FlowNodeHandler`, `TypedFlowCompiler` e `TypedFlowRunner`. Acrescenta um contrato pequeno de plugin que entrega handlers existentes, as opções JSON de carregamento e o loader. Não acrescenta outro motor de graphs.

O loader examina apenas as entradas explicitamente habilitadas nas settings. A opção desativada deve ser verificada antes de abrir a DLL, resolver seu tipo ou criar uma instância. A interface do plugin e os tipos do grafo pertencem à mesma instância de `Cortex.Core` usada pelo host; o carregamento precisa preservar essa identidade e resolver dependências do plugin a partir de sua instalação.

Nome/descrição do plugin e o estado habilitado vêm de configuração/manifesto leve, disponíveis sem carregar sua DLL. Caminhos relativos, se usados, são resolvidos contra a localização de sua configuração/manifesto, nunca contra a pasta atual do consumidor. A versão do contrato é validada ao carregar. Erros de caminho, dependência ou contrato aparecem na tela de Plugins e impedem a execução dos nodes afetados.

### Plugin de vídeo

`src/Cortex.Video` recebe as definições dos três nodes, os tipos Video/Transcript, `IVideoTranscriber` e `WhisperCppTranscriber` hoje mantidos no Depois do Slogan. A implementação de transcrição preserva a execução local já verificada: ffprobe confirma vídeo e áudio, ffmpeg extrai PCM mono 16 kHz, whisper.cpp transcreve com `-l pt`, e offsets em milissegundos são convertidos para segundos.

Os três nodes mantêm os mesmos kinds, ports e ligações, para o graph já salvo continuar utilizável. O output continua sendo `{ text, segments: [{ start, end, text }] }`. O processo recebe cancelamento e seus temporários são removidos. Os modelos e executáveis externos são configurados localmente e ficam fora do Git.

### Settings compartilhadas

O caminho padrão será `%LOCALAPPDATA%/Cortex/editor/settings.json`, com possibilidade de fornecer um caminho alternativo explicitamente ao editor para testes e instalações particulares. O arquivo possui versão e uma lista de plugins com ID, localização, estado habilitado e opções do plugin. Uma instalação nova inicia com Vídeo desativado.

O editor é responsável por ler/salvar essas settings, preservando os demais plugins. Escritas usam substituição atômica para evitar JSON truncado. Settings inválidas não são silenciosamente substituídas; a tela apresenta o erro e permite corrigi-las.

O perfil local desta demonstração será preparado com o plugin de vídeo habilitado e os caminhos já verificados nesta máquina. Isso é configuração local do exemplo, não mudança do default do produto.

### Editor

`src/Cortex.Editor` será um host local .NET/ASP.NET Core, coerente com o stack existente. Reutiliza `createFlowGraphEditor` e seus assets, sem duplicar o canvas ou adotar outro framework de frontend.

A tela possui canvas central, paleta dos nodes disponíveis, propriedades do node selecionado e inspeção de outputs/erros. A barra oferece abrir graph, salvar, validar, executar e Configurações. Para este graph, permite editar o caminho do vídeo, executar e inspecionar o transcript completo com seus segmentos. Os detalhes são derivados do catálogo carregado, sem reconhecimento de tipos de vídeo no código do editor.

Durante uma execução, alterações de graph e configuração ficam bloqueadas. Execuções usam o runner existente, não fazem retry automático da transcrição e oferecem cancelamento. A inspeção mostra o resultado da execução efetiva e não inventa progresso ou outputs. Texto de arquivos, plugins e transcrições é renderizado como texto.

O host escuta apenas em loopback. A API local que salva configuração/graph ou executa ferramentas requer uma sessão local e solicitações da mesma origem. O arquivo inicial do graph é passado explicitamente ao processo do editor; o editor não varre pastas de vídeos nem importa arquivos privados automaticamente.

### Abertura pelo Depois do Slogan

O consumidor mantém a composição do graph e passa seu arquivo ao comando/launcher do Cortex Editor. O editor encontra as mesmas settings compartilhadas e abre esse graph com os nodes já disponíveis no perfil preparado. A instalação padrão do editor é descoberta de forma consistente pelo launcher, com override explícito para outra instalação; não depende de editar código ou configurar cada node no consumidor.

O consumidor deixa de implementar os nodes de vídeo e de referenciar seus tipos ou assembly estaticamente. Seu modo CLI pode usar o mesmo loader/configuração para executar o graph sem abrir a tela. Os testes de transcrição migram para o módulo de vídeo; os testes do consumidor verificam composição e abertura/execução pelo contrato comum.

## Aceitação

1. Em processo novo e settings padrão, `Cortex.Video` não consta nas assemblies carregadas. Isso deve continuar verdadeiro mesmo se a DLL estiver instalada.
2. Plugin desativado com caminho inexistente não tenta abrir/carregar a DLL.
3. Ao habilitar Vídeo e reiniciar, o catálogo oferece exatamente os três nodes e o graph existente compila.
4. Desabilitar e reiniciar remove seus handlers; reabrir o mesmo graph preserva seus nodes e fios e informa a dependência indisponível.
5. Abrir pelo Depois do Slogan, a partir de outra pasta, usa o perfil compartilhado e não exige novas alterações no consumidor.
6. Executar o vídeo público integral já usado produz os três passos concluídos, transcript tipado e evidência de backend CUDA na RTX 4090. Nenhum modelo ou transporte remoto é usado.
7. A inspeção visual confirma palette, canvas, propriedades, transcript e a configuração de Plugins no editor próprio.
8. Testes do loader/settings, plugin, consumidor, editor e regressões apropriadas do Cortex/Filo passam. Artefatos de verificação do Filo são isolados quando seu Playground estiver aberto.
9. Código e documentação são commitados com escopo explícito na main dos respectivos repositórios. Alterações existentes de Personas e calibração do Filo são preservadas; configurações, mídia, modelos e resultados locais não são commitados.

## Decisões sem expansão de escopo

A preferência do usuário definiu um editor próprio, carregamento real de DLL e settings locais compartilhadas. Este documento descreve somente essa mudança e a reutilização do vertical slice já executado. Não define arquitetura de um editor de vídeo completo, catálogo remoto de plugins, distribuição pública ou novos workflows.
