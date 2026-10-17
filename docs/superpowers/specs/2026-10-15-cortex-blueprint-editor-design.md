# Cortex Blueprint: editor compartilhado

> **Escopo atualizado pela Helena em 2026-10-15: sem suporte a legacy.** Atualizar os grafos distribuídos e seus consumidores para v4; recusar formatos antigos com erro claro. Não implementar compatibilidade histórica, retomada v3, conversor genérico de arquivos antigos ou infraestrutura exclusiva de migração de dados legados. Preservar arquivos existentes em disco. Esta decisão substitui requisitos anteriores de conversão/consulta de dados antigos neste documento; eventual suporte fica para uma entrega futura explicitamente solicitada.

Data: 2026-10-15
Estado: aprovado pela Helena em 2026-10-15, dependente do spec de runtime.
Spec associado: C:\dev\cortex\docs\superpowers\specs\2026-10-15-cortex-blueprint-runtime-design.md

## Resultado desejado

A pessoa acompanha o fio de execução para entender o que acontece, e os fios de dados para entender de onde vem cada valor. Os títulos descrevem ações reais: Chamar Roteamento, Executar tarefa, Chamar Revisão, Retornar resposta. Uma conversão estrutural se chama Criar/Separar estrutura, nunca o nome de uma responsabilidade que ela não executa.

O renderer único continua em C:\dev\cortex\packages\flow-graph. Cortex.Editor, Filomena e Cortex Agents usam o mesmo contrato visual. Cada aplicativo mantém seu tema, idioma e permissões; não criar outro renderer em cada consumidor.

## Superfície dos blocos

- Entrada/evento, operação, função pura e nota têm formas semânticas reconhecíveis. Função pura tem dados e nenhum pino de execução.
- Execução usa pinos com seta e fio visualmente distinto. Dados usam pinos identificados por nome, tipo e direção. Cor ajuda a leitura, mas não é a única distinção.
- Arrays/estruturas exibem o tipo real de seus elementos/campos por descrição acessível. Não chamar tudo de dados.
- Inputs à esquerda, outputs à direita; exec separado. Mostrar portas disponíveis mesmo sem conexão. Inputs com defaults editáveis não exigem um bloco de constante para um único valor simples.
- Título humano é o texto principal. ID técnico fica na inspeção; não repetir id, kind e nome da função em cada título.
- Chamada abre a implementação da função quando o host fornece navegação. Voltar preserva câmera, seleção e contexto do chamador.
- Notas continuam livres, multiline, sem portas, sem execução e com texto literal seguro. Notas explicam intenção, decisão e exceção; não compensam títulos ruins com parágrafos de serialização.

## Edição e inspeção

O catálogo usa descrições resolvidas fornecidas pelo Cortex, com schema completo. O renderer não decide se um tipo é compatível; o mesmo contrato do runtime determina a validação. O editor recusa conexões incompatíveis com erro junto à porta, mantendo o grafo intacto. Validação final do compilador continua obrigatória antes de executar.

Conexões inválidas carregadas de arquivos continuam visíveis e diagnosticadas. Plugins ausentes não viram nós válidos com tipo inventado. Modo somente leitura permite seleção, navegação e inspeção, sem mutar o documento.

Pan, zoom, ajuste à tela, seleção, teclado e destruição dos listeners continuam funcionando. Foco, câmera e formulário não são reiniciados por status/atualização de execução. Respeitar escala do app e regiões estáveis; sem transformações que tornem hit testing incoerente.

A primeira entrega usa os controles existentes para conexão e inspeção. Menus contextuais sofisticados, busca contextual completa e reorganização automática não são pré-requisitos e ficam fora deste passe.

## Integração

C:\dev\cortex\src\Cortex.Editor\EditorCatalog.cs deve descrever cada instância com sua configuração, incluindo contratos dinâmicos, em vez de copiar a entrada estática da paleta.

C:\dev\cortex-agents\src\CortexAgents.Desktop\Web\cortex-graph.js deve receber metadados resolvidos separadamente do JSON original. O endpoint de inspeção preserva o documento original e fornece a descrição sem iniciar execução ou provider.

C:\dev\filo\src\Filomena.App\Playground deve usar os contratos comuns na visualização/edição e continuar servindo o bundle compartilhado. A migração não substitui o playground inteiro.

## Aceitação visual e funcional

- Mesmo grafo tem as mesmas portas/tipos no editor Cortex e nos hosts.
- Grafo pequeno demonstra evento, operação, chamada, função pura, estrutura e nota sem bloco de adaptação JSON obrigatório.
- Porta sem fio continua visível; tipo incompatível não cria conexão; contrato configurável atualiza a descrição sem descartar fios silenciosamente.
- Navegação em chamada e volta conservam a câmera; inspection nunca executa a chamada.
- Contraste, foco visível e labels acessíveis permitem distinguir exec/dados sem depender de cor.
- Verificar um lote de capturas com janela ampla/reduzida e escala 150%/200%; uma correção conjunta e no máximo uma confirmação.
