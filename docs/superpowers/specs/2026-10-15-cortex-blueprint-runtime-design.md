# Cortex Blueprint: runtime e contratos

> **Escopo atualizado pela Helena em 2026-10-15: sem suporte a legacy.** Atualizar os grafos distribuídos e seus consumidores para v4; recusar formatos antigos com erro claro. Não implementar compatibilidade histórica, retomada v3, conversor genérico de arquivos antigos ou infraestrutura exclusiva de migração de dados legados. Preservar arquivos existentes em disco. Esta decisão substitui requisitos anteriores de conversão/consulta de dados antigos neste documento; eventual suporte fica para uma entrega futura explicitamente solicitada.

Data: 2026-10-15
Estado: aprovado pela Helena em 2026-10-15; implementação delegada ao GPT-6 Luna.
Responsável pela implementação: GPT-6 Luna, raciocínio high; coordenação e revisão pelo agente principal.

## Intenção e decisões da Helena

Cortex deve ser uma linguagem visual próxima de Unreal Blueprints. O grafo deve mostrar operações, chamadas de funções, dados tipados e fluxo de execução. Adaptar texto JSON entre operações não deve exigir blocos visíveis. A mudança começa no Cortex e alcança seus consumidores; não se limita ao Cortex Agents.

Helena autorizou explicitamente quebrar o formato antigo e migrar tudo. Não manteremos caminhos paralelos de execução legada apenas para preservar o formato. Isso não autoriza apagar dados de usuários, transcrições ou histórico. A execução será incremental e verificável, com GPT-6 Luna implementando cada entrega.

Referência conceitual: https://dev.epicgames.com/documentation/unreal-engine/functions-in-unreal-engine — funções puras e funções com execução. Não buscamos implementar toda a Unreal nem copiar seus assets.

## Problema comprovado

- `C:\dev\cortex\src\Cortex.Core\Flows\FlowTypes.cs` já representa strings, números, booleanos, arrays, objetos, enums e nullable. O transporte interno pode carregar valores JSON estruturados sem transformá-los em strings.
- `C:\dev\cortex\src\Cortex.Core\Flows\FlowNodeHandler.cs` exige exec/then até em handlers Pure. Efeito e forma de execução estão misturados.
- `C:\dev\cortex\src\Cortex.Core\Flows\TypedFlowCompiler.cs` só dispensa property/count e entradas nativas da exigência de alcance por execução; a disponibilidade dos dados também trata esses getters por exceção.
- `C:\dev\cortex\src\Cortex.Core\Flows\Execution\FlowExecutionKernel.cs` resolve property/count sob demanda; outros handlers só executam pela sequência de controle. O kernel é compartilhado pela execução local e durável.
- Cortex Agents embrulha e desembrulha strings JSON para chamar camadas. Entrada possui oito operações onde cinco bastam.

## Contrato proposto

### Documento v4, sem legacy

O formato de grafo executável passa de versão 3 para versão 4. Formato portátil, documento de projeto/brain, plugins e snapshots são contratos diferentes; não incrementar suas versões por substituição textual indiscriminada.

Atualizar os grafos distribuídos, geradores, fixtures e consumidores do código. Compilação e execução recusam formatos antigos com diagnóstico claro. Não criar caminhos para executar/retomar v3, ler especialmente estados de execuções antigas ou converter arquivos legados. Não reescrever nem apagar dados reais existentes como parte desta entrega. Leitores e mecanismos de histórico/cancelamento já existentes não precisam ser removidos; não ganharão uma camada de compatibilidade aqui.

A ferramenta genérica de migração offline, a consulta snapshot-only proposta e a infraestrutura exclusiva de coordenação da migração de dados saíram do escopo por decisão explícita da Helena. Eventual suporte a dados antigos será discutido futuramente.

### Três formas de nó

- **Entrada/evento:** inicia a execução e publica parâmetros tipados. Não recebe fio de execução.
- **Operação:** recebe execução; pode publicar saídas de execução e dados. Chamadas de modelo, ferramentas, gravações e chamadas de camada com efeitos permanecem operações.
- **Função pura:** não tem portas de execução. Recebe dados, calcula saídas e é avaliada quando uma operação consumidora precisa delas.

A forma de execução é explícita no contrato do nó e independente de FlowEffect. Um nó com efeito Pure pode continuar sendo uma operação deliberada, por exemplo uma validação que interrompe um fluxo. Não converter automaticamente todos os handlers marcados Pure.

Funções puras não consultam relógio, rede, banco vivo ou modelo e não gravam estado. Podem ler dados imutáveis capturados para a invocação. Leituras que precisam atualizar estado permanecem operações.

### Avaliação de dados

Resolver dependências puras em ordem topológica e apenas no caminho efetivamente consumido. Cada ativação de uma operação tem um cache de resolução: múltiplos fios que leem a mesma função pura nessa ativação compartilham uma avaliação. A próxima operação pode reavaliar. Não usar cache global nem reutilizar valores derivados após edição de outputs no modo passo a passo. O cache é efêmero e separado de ExecutionSnapshotV1.Values/Outputs; não persistir resultados puros derivados como valores autoritativos. Inspeção desses derivados usa a observação da ativação, sem mudar o checkpoint.

O compilador continua rejeitando ciclos e dados de operações que não executam em todos os caminhos anteriores. A análise percorre cadeias de funções puras até os produtores operacionais reais. Um ramo não escolhido não dispara computação, modelos ou ferramentas por causa de fios de dados.

A resolução respeita cancelamento, valida inputs/outputs e atribui falha ao nó e porta reais. Resultados puros podem aparecer na inspeção da ativação consumidora; não criam novas ações externas, checkpoints de efeitos ou mudanças na ordem dos efeitos. Retomada durável reconstitui derivados dos valores persistidos autoritativos. Testar retomada e revisão de outputs; não mudar a política de repetição de efeitos.

### Portas tipadas e valores

Todos os novos contratos expõem nomes de domínio concretos e FlowType reais. Arrays são arrays; objetos são objetos; bool não é string. Optional e nullable mantêm significados distintos. Sem coerção implícita entre string JSON e objeto.

Decisão adicional aprovada pela Helena em 2026-10-15: saídas nomeadas opcionais podem estar ausentes. A ausência é preservada ao passar por fios para entradas opcionais; uma entrada obrigatória que receber ausência falha com diagnóstico claro. Saídas obrigatórias ausentes continuam inválidas. Um null explícito é um valor presente e só passa se o tipo o permitir. Não introduzir objetos intermediários nem blocos Make/Break para contornar essa regra. A regra específica de Separar estrutura permanece: solicitar um campo ausente falha.

Defaults constantes opcionais pertencem às entradas e são validados com o mesmo tipo do fio. Fio e valor configurado não competem silenciosamente. Configuração estática da operação continua separada dos inputs de dados.

**Criar estrutura / Make Struct** recebe campos e produz um objeto tipado. **Separar estrutura / Break Struct** recebe um objeto e expõe campos. Ambos são puros e só aparecem quando a composição é significativa para a pessoa. Decisão da Helena: pedir um campo opcional ausente em Separar estrutura falha com erro claro; campo presente com null continua válido somente se seu tipo aceita null. Não inventar saídas de presença nem converter ausência em null. Separar estrutura permite selecionar os campos que serão extraídos; campos não solicitados não precisam existir. Não colocá-los automaticamente antes/depois de toda chamada. As projeções existentes podem ser reaproveitadas na implementação sem acrescentar um segundo sistema de tipos.

### Dicionários tipados — decisão adicional de 2026-10-15

Helena escolheu adicionar o tipo dicionário ao Cortex e ao editor, preservando o orçamento por modelo como mapa, em vez de convertê-lo para uma lista de pares. O tipo compartilhado representa chaves textuais e valores de um FlowType concreto; o valor continua sendo um objeto JSON, sem serialização para string ou adaptação para arrays.

A extensão participa da mesma validação, compatibilidade, compilação, metadados, constantes, execução e serialização de snapshots. Valores podem ser estruturas, listas, outros dicionários ou nullable. Ausência de chave difere de null; acessar uma chave inexistente por uma projeção solicitada falha claramente. Chaves são preservadas e o cálculo de limites do Agents não muda.

A representação JSON Schema usa object com additionalProperties contendo o schema dos valores; a representação portátil usa kind dictionary e valueType. Objetos de campos fixos mantêm seu contrato. Não aceitar um objeto sem tipo, nem conversão implícita para objeto fixo/lista/string, nem descartar campos de schemas mistos. A apresentação no editor identifica dicionário e o tipo de valor, usando o mesmo contrato para validar constantes. Não inclui novas operações de coleção, loops ou um novo subsistema de editor.

Implementar e revisar esta extensão separadamente antes de corrigir o schema PlanBudget no Agents. Brief e critérios de teste: C:\temp\cortex-blueprint-redesign\task-5a-dictionary-brief.md.

### Funções e chamadas

O Cortex define um contrato compartilhado de função: identidade estável, nome, parâmetros e retornos tipados, forma de execução e referência navegável à implementação quando disponível. Descrição de portas não pode construir sessões, carregar credenciais, executar handlers ou consultar providers.

O host registra as funções e controla sua resolução. Chamadas só podem atingir funções autorizadas e conhecidas no escopo; não aceitam caminhos arbitrários fornecidos pelo grafo. Não adicionar recursão ou execução dinâmica de código nesta entrega.

No Cortex Agents, camadas são funções do host. `Chamar Roteamento` expõe tarefa e ranking; `Chamar Revisão` recebe e devolve campos de domínio. Serialização fica na fronteira de persistência/integração. IDs de execução, vínculo pai/filho, autorização de camadas e propagação de erros permanecem preservados.

### Fonte única de contratos

Runtime, compilador e editor consomem a mesma descrição resolvida de portas, incluindo configurações que alteram o contrato. O catálogo não deve olhar só portas estáticas nem inferir os tipos a partir dos fios existentes. Contratos de built-ins e plugins precisam ficar disponíveis ao inspector sem executar o grafo. A API de descrição consome definições, sem depender de handlers de execução vinculados a sessões. A montagem/descrição de plugins deve ser livre de efeitos e ter teste com factories de credencial/provider/sessão que falhem se acessadas; não assumir que chamar CreateHandlers garante isso.

## Entrega incremental

1. Cortex: contrato explícito de nó, avaliação pura e validação, com prova pequena funcionando.
2. Cortex: descrição comum, chamadas tipadas, Make/Break e editor compartilhado.
3. Atualizar grafos e fixtures do próprio Cortex para v4 e recusar versões antigas.
4. Migrar consumidores segundo o spec de migração. Cada consumidor termina compilável e testado antes de passar ao próximo.

A quebra de compatibilidade é coordenada entre os repositórios; não publicar um pacote ou fazer deploy durante o estado intermediário. Não reescrever provedores, memória, roteamento de modelos ou APIs de chat sem necessidade demonstrada desta mudança.

## Critérios de aceitação

- Handler puro sem exec é aceito, ligado e executado sob demanda; efeitos não são aceitos como puros.
- Duas portas da mesma função pura, duas operações consumidoras, ramo não escolhido, cancelamento e erro têm comportamento definido acima.
- Ciclo de dados, tipo incompatível, porta ausente, alvo de chamada desconhecido e produtor disponível em só um ramo falham antes de qualquer efeito.
- Editor e execução concordam sobre tipos e portas configuráveis, inclusive sem fios conectados.
- Retomada durável não repete envio/ferramenta; emenda de output não reutiliza derivados antigos.
- Grafos distribuídos/fixtures usam v4; dados antigos no disco não são convertidos nem apagados.
- Grafo antigo não é silenciosamente aceito pelo runtime novo.
- Nenhum teste usa serviços pagos, mensagens reais, contas ou projetos do usuário.

## Limites desta proposta

Não inclui loops novos, macros, herança, compilação nativa, colaboração multiplayer, marketplace de nós, recursão, nem um clone completo da Unreal. O objetivo é que construir e ler os grafos atuais tenha a clareza de Blueprints.



## JSON bruto — decisão adicional de 2026-10-15

Helena escolheu adicionar um tipo JSON bruto compartilhado ao Cortex, em vez de serializar entrada/saída do histórico em campos de texto. Uma execução nova pode falhar por receber um valor inválido para seu contrato de domínio; o histórico precisa preservar e expor esse valor original sem validá-lo novamente como entrada bem-sucedida.

JsonType representa qualquer valor JSON nativo, incluindo objeto, lista, escalar e null explícito. Ausência de porta continua distinta de null, e Undefined não é valor JSON. Campos e valores internos são opacos e preservados. Tipos concretos podem alimentar uma porta JSON; JSON bruto não alimenta automaticamente um tipo mais restrito. Não usar esse tipo como fallback para schemas inválidos, tipos desconhecidos ou metadados não resolvidos.

O schema canônico é o schema vazio explícito {}; o formato portátil usa kind json. A extensão participa das mesmas validações, metadados, defaults, chamadas e checkpoints, sem nova versão de grafo/snapshot. O editor usa o controle JSON existente, incluindo escalares e null. O envelope do histórico continua tipado; somente a evidência original usa o tipo bruto. Não criar suporte a legacy nem descartar registros de falha.

Implementar com Luna e revisar separadamente antes de integrar ao histórico Agents. Requisitos e testes: C:\temp\cortex-blueprint-redesign\task-5a-json-brief.md.
