# Cortex Blueprint: migração dos consumidores

> **Escopo atualizado pela Helena em 2026-10-15: sem suporte a legacy.** Atualizar os grafos distribuídos e seus consumidores para v4; recusar formatos antigos com erro claro. Não implementar compatibilidade histórica, retomada v3, conversor genérico de arquivos antigos ou infraestrutura exclusiva de migração de dados legados. Preservar arquivos existentes em disco. Esta decisão substitui requisitos anteriores de conversão/consulta de dados antigos neste documento; eventual suporte fica para uma entrega futura explicitamente solicitada.

Data: 2026-10-15
Estado: aprovado pela Helena em 2026-10-15; inventário inicial verificado em arquivos fonte.
Dependências:
- C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-runtime-design.md
- C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-editor-design.md

## Política acordada

Helena escolheu quebrar o formato antigo e migrar os consumidores. Não manter adaptadores legados públicos para evitar a migração. Preservar dados e histórico existentes em disco. Atualizar somente grafos e consumidores do código distribuído; recusar formatos antigos claramente, sem conversão de arquivos salvos. Não substituir configurações personalizadas por defaults nem executar agentes durante a migração.

A implementação será feita pelo GPT-6 Luna, esforço high, em entregas sequenciais com revisão. Uma tarefa tem dono, arquivos e teste de aceitação próprios. Nenhum worker decide novas semânticas arquiteturais: divergências voltam à Helena. Não commitar, publicar ou fazer deploy automaticamente.

## Inventário confirmado

| Repositório | Consumo comprovado | Migração necessária |
| --- | --- | --- |
| C:\dev\cortex | Core, Editor, plugins e exemplos internos, renderer compartilhado | Runtime/compiler, catálogo, editor, exemplos e testes v4, recusa explícita de versões antigas |
| C:\dev\cortex-agents | ProjectReference Core e bundle flow-graph; handlers de camadas e Brain | Contratos, sete camadas, outros brains/fixtures, inspector e recusa de overrides antigos |
| C:\dev\filo | Filomena.App e Filomena.Llm referenciam Core; App embute flow-graph; tools também usam Core | Runtime/sessões, playground, grafos, testes e ferramentas de manutenção |
| C:\dev\damma | Damma.Personas.Tests referencia Core e implementa IFlowCompletion/inference | Adaptar somente interfaces afetadas e fixtures; não inventar um editor ou sistema de grafos de produção |
| C:\dev\new-wine | NewWine.TrustLab.Cortex referencia Core, plugins, TypedFlowCompiler/Runner e grafo embutido | Contratos/artefato embutido, plugin, execução de replay e testes |
| C:\dev\depois-do-slogan | Web e FluxStudio.Device referenciam Cortex.Editor; plugin próprio usa Core | Plugins, catálogo/serviços de editor, runner, grafos e testes do produto |

C:\dev\filomena contém material auxiliar/runtime, não o checkout-fonte ativo da Filomena. Não alterar os dados runtime auxiliares nem iniciar serviços; esta entrega atualiza o checkout-fonte ativo.

Busca inicial: referências de projeto e imports em arquivos fonte, excluindo bin/obj/node_modules/.git e cópias em worktrees/helworks builds. Houve pastas de cache sem permissão na busca ampla de C:\dev. O inventário é confirmado para os repositórios acima, não uma prova sobre diretórios inacessíveis. Antes de encerrar, repetir a busca limitada a referências de projeto/código para detectar consumidores adicionais; cada achado entra nesta matriz antes de alteração.

## Ordem e entregas

Primeiro estabilizar Cortex com uma demonstração offline pequena. Depois Cortex Agents, Filomena, Depois do Slogan, NewWine e Damma. A ordem dos consumidores pode mudar por dependência comprovada, sem executar dois builds que compartilhem outputs. Não fazer uma onda de alterações sem entregar um consumidor verificável entre elas.

### Cortex Agents

Arquivos centrais: C:\dev\cortex-agents\Layers\LayerNodes.cs, LayerReadNodes.cs, LayerOutputNodes.cs, LayerContracts.cs, LayerCapabilities.cs e os nós de ação; C:\dev\cortex-agents\Brain; C:\dev\cortex-agents\layers; C:\dev\cortex-agents\src\CortexAgents.Desktop\Hosting\CortexLayers.cs; C:\dev\cortex-agents\src\CortexAgents.Desktop\Web\cortex-graph.js.

Entrada se torna cinco operações:

Receber mensagem -> Chamar Roteamento -> Executar tarefa -> Chamar Revisão -> Retornar resposta.

- Mensagem fornece tarefa diretamente à chamada.
- Roteamento retorna ranking como lista estruturada, diretamente para Executar tarefa.
- Trabalho fornece resultado, status, agente, modelo, modelo pedido e decisão como portas para Revisão.
- Revisão fornece texto e identidade finais diretamente ao retorno.
- Remover rankingLer, revisaoEntrada e entradaDados deste fluxo, recompondo seus fios e notas. O objetivo é eliminar a necessidade dos adaptadores, não ocultar operações no desenho.

Depois migrar Roteamento, Setup, Revisão, Quadro, Delegação e Limite. Leituras do snapshot viram funções puras quando satisfazem o contrato. Dados de domínio deixam de ser strings de JSON. Entrada/saída de cada camada corresponde ao contrato real, inclusive regras de campos opcionais.

Preservar ramo de status de trabalho, falhas obrigatórias, revisão por outro agente, limite de uma correção, contrato da decisão do Jev (falhar na primeira resposta inválida), roteamento autorizado, aprovação explícita de proposta de setup e limites de delegação. Não comprimir esses controles só para diminuir a contagem de nós.

Migrar também brains externos às sete camadas, grafos criados por código, fixtures e exemplos ativos. Remover handlers de adaptação que ficaram sem uso após atualizar os grafos. Não criar ferramentas de conversão de dados antigos.

### Filomena

Checkout: C:\dev\filo. Pontos de entrada confirmados:
- C:\dev\filo\src\Filomena.App\Filomena.App.csproj
- C:\dev\filo\src\Filomena.Llm\Filomena.Llm.csproj
- C:\dev\filo\src\Filomena.App\Playground\PlaygroundAssets.cs
- C:\dev\filo\tests\Filomena.Llm.Tests\FlowGraphAssetTests.cs

Migrar geradores/leitores de grafos, defaults, editor e fixtures. Preservar boundaries, envio confirmado, memória, waits/feedback e retomada durável. Ferramentas ReconcileNotSent e ImportLiveContext precisam compilar contra o contrato novo; não executá-las em dados reais como teste. Não reprocessar conversas ou enviar mensagens.

### Depois do Slogan / FluxStudio

Referências confirmadas:
- C:\dev\depois-do-slogan\src\DepoisDoSlogan.Web\DepoisDoSlogan.Web.csproj
- SloganWebHost, VideoRuntimeManager, EditorUseService e SloganNodePlugin no mesmo checkout.
- FluxStudio.Device e BriefRunner no mesmo checkout também dependem do Editor.

Migrar handlers/contratos e grafos sem alterar política de spending, plugins instalados, fluxos editoriais ou execução de vídeo. A aplicação continua consumindo o editor comum; não adicionar uma implementação local divergente.

### NewWine TrustLab

Referência: C:\dev\new-wine\src\NewWine.TrustLab.Cortex\NewWine.TrustLab.Cortex.csproj.

Migrar o grafo embutido em TrustLabCortexContracts, plugins e runner de replay. Preservar evidência/replay determinístico e contratos de saída da aplicação. Nenhuma mudança de negócio ou chamada externa real para provar a migração.

### Damma

Referência: C:\dev\damma\personas\tests\Damma.Personas.Tests\Damma.Personas.Tests.csproj e PersonaInferenceFixture.cs.

Uso atual confirmado é IFlowCompletion e contratos de autoridade de inferência, em testes. Se esses contratos continuarem compatíveis, registrar validação sem fabricar mudanças. Não iniciar inferência paga ou alterar produção só por existir ProjectReference.

## Validação por entrega

Antes de editar um checkout, ler instruções locais, registrar status/diff e preservar mudanças existentes. Não restaurar arquivos inteiros nem usar commits anteriores como base que apaguem trabalho atual. Tests offline primeiro; serviços externos não são pré-requisitos.

Cortex: testes focados de Core/Flows, avaliação pura e disponibilidade; Editor/contracts; retomada durável e outputs emendados; testes JS do renderer. Testar aceitação de grafos v4, recusa explícita de formatos antigos e erro em nó desconhecido, sem ferramentas de migração.

Agents: grupos do runner `layer-engine`, `layer-routing-defaults`, `layer-project-defaults`; testes de inspector, grafos e integração afetados; build Desktop. Comandos-base: `dotnet run --project tests/CortexAgents.Tests.csproj --no-restore -- layer-engine` e `dotnet test tests/CortexAgents.Desktop.Tests/CortexAgents.Desktop.Tests.csproj --filter <classes afetadas>`. Testes já sabidamente falhos fora do escopo devem ser registrados, sem serem usados como desculpa para ignorar regressões novas.

Filomena: projetos Filomena.Core.Tests e Filomena.Llm.Tests com filtros offline afetados; `node scripts/build-flow-graph.mjs` e testes frontend relevantes. Excluir integrações Postgres/LocalSocket e lifecycle real da prova local.

Depois do Slogan: projeto DepoisDoSlogan.Web.Tests e testes frontend afetados, mais build dos consumidores do Editor.

NewWine: usar C:\dev\new-wine\tests\NewWine.TrustLab.Tests\NewWine.TrustLab.Tests.csproj com filtro offline do runner; testes demo de cortex-replay/model pertinentes documentados em docs/allianz-trust-lab.md. Não substituir prova do runtime por teste de texto estático do demo.

Damma: `dotnet test personas/tests/Damma.Personas.Tests/Damma.Personas.Tests.csproj --no-restore --filter OfflinePersonaSmokeTests`, conferindo primeiro que o filtro executa testes e não depende de provider real.

## Conclusão verificável

Todos os consumidores confirmados compilam com o novo Cortex; grafos ativos usam o novo contrato; testes focados passam ou têm falhas anteriores documentadas com evidência. O usuário consegue abrir Entrada e acompanhar cinco operações, abrir Roteamento e entender os dados sem nós de serialização. Arquivos antigos permanecem intactos em disco. O formato antigo é recusado claramente; consulta compatível de histórico e conversão de grafos personalizados ficam fora desta entrega.
