# Composição completa de vídeo e áudio na Helengine

A Helengine deve executar a edição audiovisual inteira do Depois do Slogan. O Cortex propõe e resolve a edição; a Helengine controla o tempo, renderiza os quadros e mistura o áudio; o executável FFmpeg recebe vídeo e áudio prontos e apenas codifica e empacota MP4, MOV ou outro formato de saída. Este desenho substitui a distribuição anterior, que deixava composição e movimento de imagens no FFmpeg.

## Responsabilidades

| Componente | Responsabilidade |
|---|---|
| Cortex | Roteiro, proposta editorial, revisão humana, seleção de materiais e resolução de foco e tempos de fala. Compila essas decisões em uma composição executável. |
| Helengine | Leitura de mídias, timeline, cortes, camadas, animações, máscaras, overlays, legendas, transições, mistura de áudio, prévia e renderização para exportação. |
| Encoder de saída | Recebe quadros compostos e amostras de áudio misturadas pela Helengine. Converte para codecs e contêiner escolhidos. |
| Depois do Slogan | Autoria e revisão, materiais, controles de edição, jobs e apresentação da prévia. |

Os efeitos pertencem à Helengine e são reutilizáveis por outros produtos. O Cortex usa um catálogo de capacidades publicado pela engine para sugerir apenas efeitos executáveis. Um projeto existente não ganha efeitos novos automaticamente quando o catálogo muda.

## Base existente

A Helengine tem `IVfxEffect`, `VfxEffectRegistry` e um runner DirectX 11 para efeitos HLSL. O runner atual lê sequências EXR e processa um efeito por invocação. O renderer de legendas já avalia texto em um instante absoluto. Essas peças podem participar do compositor, mas não representam uma timeline audiovisual completa.

Há contratos e reprodução de áudio em `AudioManager`, `IAudioBackend` e `AudioAsset`, além de importação WAV/MP3 com NAudio. Reprodução em um dispositivo não substitui a produção determinística de amostras para exportação. A mistura offline precisa de um contrato próprio compartilhado com a prévia.

`DirectX11VideoDecoder` expõe leitura de quadros e seek e depende da biblioteca nativa `helengine.video.ffmpeg`. A presença e o funcionamento dessa biblioteca precisam ser verificados antes de considerar a importação de clipes pronta. O backend de codec fica encapsulado na Helengine: bibliotecas de leitura de mídia não executam edição, filtros, transições ou mistura. O executável FFmpeg continua exclusivo da saída final.

Fontes locais: [VFX](C:/dev/helworks/helengine/engine/helengine.vfx/IVfxEffect.cs), [runner](C:/dev/helworks/helengine/engine/helengine.vfx.directx11/DirectX11VfxEffectRunner.cs), [legendas](C:/dev/helworks/helengine/engine/helengine.vfx.captions/CaptionRenderer.cs), [áudio](C:/dev/helworks/helengine/engine/helengine.core/audio/IAudioBackend.cs) e [decoder](C:/dev/helworks/helengine/engine/helengine.directx11.video/DirectX11VideoDecoder.cs).

## Composição executável

Criar um contrato versionado da Helengine para composição. Ele descreve resolução, frame rate racional, duração, configuração de áudio, mídias, camadas visuais, trilhas de áudio, animações e transições. IDs estáveis e versões de efeitos permitem reproduzir uma edição e revisar diferenças.

O contrato recebe materiais e tempos resolvidos. Não conhece prompts, justificativas editoriais, chamadas a modelos nem tabelas do Depois do Slogan. Fontes têm identidade, hash e metadados verificados. Arquivos são resolvidos pelo host dentro das raízes autorizadas; o modelo não fornece caminhos de execução nem código de shader.

Cada clipe descreve fonte, entrada e saída na mídia e posição na composição. Camadas têm ordem explícita, região de apresentação, ajuste `contain` ou `cover`, transformação, opacidade, máscara e lista ordenada de efeitos. Textos e legendas são camadas próprias; aproximar uma imagem não amplia involuntariamente a legenda.

Animações usam propriedades tipadas, keyframes e curvas declaradas pela engine. A primeira migração mantém entrada por corte/fade e zoom com foco central, ponto ou região resolvida. Posição, escala, rotação e opacidade formam a base extensível. A curva `ease_in_out` já usada no Cortex é smoothstep; a curva com o mesmo nome em `VfxEasing` tem outra fórmula. A migração deve conservar a curva existente com identificador explícito, sem mudar o movimento silenciosamente.

As transições relacionam duas fontes visuais ou grupos de camadas, têm intervalo e parâmetros próprios e produzem um quadro composto. A primeira transição é crossfade. Fade de uma camada e transição entre cenas continuam operações distintas. O crossfade usa sobreposição dentro da duração declarada da timeline e exige intervalos de fonte suficientes; não encurta o projeto implicitamente.

## Relógio, prévia e exportação

Um relógio da composição determina qual quadro e quais amostras pertencem a cada instante. Tempos são racionais ou inteiros na unidade declarada, sem acumulação por incrementos de ponto flutuante. Intervalos são fechados no início e abertos no fim. FPS de saída e timestamps de entrada são conceitos distintos; clipes com frame rate variável são selecionados por timestamp.

A avaliação de um quadro pode ocorrer fora de ordem, permitindo seek e comparação de prévia com exportação. Efeitos com histórico declaram essa necessidade e restauram seu estado de maneira determinística. A primeira entrega usa efeitos sem histórico.

A prévia pode renderizar com resolução reduzida e descartar quadros para acompanhar a reprodução. A exportação avalia todos os quadros e amostras, independentemente da velocidade da máquina. Ambas usam a mesma timeline, curvas, composição e mixer. A resolução da prévia não altera tempos, região de foco nem tamanhos proporcionais de texto.

## Áudio na Helengine

A composição tem trilhas para voz, som dos clipes, música e efeitos sonoros. Cada item de áudio tem intervalo de fonte, posição, ganho, mute, canais e envelopes. O áudio embutido de um vídeo permanece sincronizado com seu clipe, com opção explícita de separação.

O mixer converte fontes para a configuração comum do projeto e produz blocos PCM float. A configuração inicial proposta é 48 kHz estéreo. O mapeamento de canais e a conversão de taxa são explícitos. Ganhos, fade in/out e crossfade são avaliados por amostra; sobreposições são somadas pela engine. O render reporta picos acima do limite sem normalizar ou limitar o som silenciosamente.

A reprodução consome esses blocos em um dispositivo de áudio; a exportação entrega os mesmos blocos ao encoder. Nenhuma captura do alto-falante participa da exportação. Pausa, seek e retomada reinicializam os buffers e mantêm áudio e imagem no mesmo relógio.

## Renderização e saída

Um compositor DirectX 11 mantém texturas, avalia camadas e aplica efeitos na GPU. A leitura de imagens e vídeo, o mixer e o exportador ficam em componentes separados. Reaproveitar infraestrutura VFX não exige converter todos os clipes em sequências EXR nem duplicar uma imagem em disco para cada quadro.

O exportador entrega quadros finais e PCM final a uma interface de encoder. Um adaptador FFmpeg configura formato de entrada, codecs, contêiner e qualidade. Ele não recebe roteiro nem grafo de edição e não usa `filter_complex`, `overlay`, `zoompan`, `xfade`, `amix` ou filtros editoriais. Arquivos intermediários de quadros e áudio são permitidos para diagnóstico; transmissão contínua usa buffers limitados e controle de fluxo.

O perfil de saída determina transparência, espaço de cor e codec. MP4 com H.264 usa composição sobre fundo definido; MOV com codec apropriado pode preservar alpha. Essas diferenças são validadas antes do job. Suporte a novos codecs não equivale a novos efeitos da engine.

## Integração e migração

O plano editorial e as alterações humanas existentes permanecem no Cortex. Um compiler produz a composição Helengine depois de resolver materiais, foco e tempos. O cache considera composição efetiva, hashes das mídias, versões dos efeitos e configuração de saída. Um resultado de revisão antiga não substitui a prévia vigente.

O renderer do Depois do Slogan passa a chamar a composição Helengine. As legendas existentes viram camadas da composição. Transformações de imagem, posicionamento das legendas e overlays deixam os filtros FFmpeg. A montagem final de gravações e o áudio usam o mesmo caminho.

A entrega ocorre em etapas do mesmo subsistema: contrato e relógio; compositor de imagens, textos e animações; clipes e mixer de áudio; transições; exportação e integração completa. A aceitação da migração exige um vídeo real com gravação, som, imagem animada, texto sobreposto e transição, produzido por esse caminho completo.

O catálogo inicial abrange corte, transformações 2D, opacidade, imagem, texto/legendas, overlay, crossfade visual e ganho/fades/crossfade de áudio. Outros efeitos podem ser adicionados com parâmetros tipados. Um editor visual de timeline da Helengine, efeitos 3D e correção de cor avançada são trabalhos posteriores; o contrato deve permitir a extensão sem antecipar suas implementações.

## Falhas e validação

Validar referências, versões de efeitos, intervalos, dimensões, mídias e configuração de áudio antes de renderizar. Fonte ausente, codec indisponível, foco pendente ou transição sem material suficiente resultam em pendência ou erro explícito. A prévia de planejamento pode usar marcadores de mídia ausente identificados; a exportação final exige composição resolvida.

Cancelamento libera device, texturas, decoders, buffers e encoder. Saídas incompletas não são publicadas. Jobs concorrentes respeitam limites de GPU e memória; não carregam todos os quadros e todo o áudio do projeto de uma vez.

Verificar: limites de quadros e amostras, seek repetível, seleção por timestamp, curva e foco do zoom existente, composição e alpha, duração do crossfade, ganho e envelopes de áudio, sincronismo entre fala e imagem, recursos após cancelamento e invalidade de prévias antigas. Renderizar um projeto de integração nos formatos vertical, horizontal e quadrado. Inspecionar o comando do encoder e comprovar que ele apenas converte os fluxos finais.
