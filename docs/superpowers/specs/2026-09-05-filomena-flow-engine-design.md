# Filomena — motor visual de personagens e conversação, v0

Estado: decisões de produto aprovadas na conversa; consolidação técnica para revisão.
Data: 2026-09-05.
Substitui o painel exclusivamente de configurações e seu plano de implementação.

## 1. Objetivo e fronteiras

Filomena passa a ser um personagem/configuração inicial de um motor reutilizável.
O administrador desenha a inteligência em gráficos: prompts, modelos, dados,
memória, decisões, esperas, processamento paralelo e ações de saída.

Uma instalação pode executar vários personagens na mesma conversa e na mesma
conta de transporte. Gestor/atendente e narrador/NPCs são exemplos, não modos
especiais do código. Um personagem não é uma conta, um participante humano,
um modelo ou um grafo Graphiti.

O cérebro continua em C#/.NET. OpenClaw continua como gateway/tools/transporte,
sem retomar decisões conversacionais. LM Studio e Graphiti continuam integrações.
Não reimplementar Baileys, Telegram ou Discord. Não trocar a Gemma nem carregar
outro modelo automaticamente: cada etapa pode escolher um perfil explicitamente.

O administrador não precisa programar. Prompts resolvem decisões semânticas;
o motor executa conexões, condições tipadas, validação, autorização, persistência,
timers e envios. Não haverá bloco de código arbitrário, shell ou subflows na v0.

## 2. Estado atual e transição

O código atual tem ConversationTurnManager, árbitro, geração e envio C#;
captura persistente ainda pertence ao plugin Graphiti durante a migração.
O servidor HTTP já recebe eventos normalizados pela bridge, necessária onde MCP
não expõe metadados suficientes. Manter esse caminho sem inventar identidade.

O painel anterior não foi entregue. A tentativa com subagentes encerrou por limite
de uso; existem testes parciais SettingsStoreTests.cs e FrontendBuildTests.cs,
sem os componentes correspondentes. Não apagar nem assumir que passam. Avaliar
reaproveitamento quando as entregas relacionadas começarem. Os outros arquivos
sujos pertencem ao trabalho anterior e devem ser preservados.

Esta rodada documenta. Não altera runtime, configuração efetiva, Gateway, grafo,
destinos autorizados ou workspace/DREAMS.md. Não faz push.

## 3. Conceitos estáveis

| Conceito | Responsabilidade |
| --- | --- |
| Ambiente | Testnet/mainnet: dados, rotas, publicações e execuções isolados |
| Conta de transporte | Conta WhatsApp/bot Telegram/etc.; não é personalidade |
| Conversa | Identidade nativa normalizada pelo adapter; nome é apresentação |
| Ator externo | Pessoa/provider conhecido estruturalmente; nome não é identidade |
| Personagem | ID estável, personalidade, gráfico completo e permissões |
| Etapa de IA | Prompt, perfil de modelo e contrato de saída; personagem opcional |
| Gráfico de entrada | Admite/roteia eventos e distribui explicitamente aos personagens |
| Versão publicada | Snapshot imutável de gráfico e configurações versionáveis |
| Execução | Evento de origem, versão, ambiente, principal e checkpoints |
| Espaço de memória | Escopo nomeado com ACL, histórico/fatos/estado e proveniência |

Dois personagens podem usar a mesma conta sem compartilhar memória ou poder de
envio automaticamente. Etapas técnicas (árbitro/revisor/extrator) não criam novas
identidades sociais. Agir como personagem vincula o ID canônico desse personagem,
não uma entidade probabilística extraída de seu nome.

## 4. Entrada compartilhada e gráficos completos

Cada ambiente publica rotas de entrada que selecionam conversas/contas por IDs.
Um evento admitido entra em um gráfico de entrada compartilhado, que pode:

- Encaminhar a um personagem por condição determinística.
- Usar uma etapa de IA e seu resultado tipado para escolher o encaminhamento.
- Distribuir explicitamente a vários personagens, inclusive em paralelo.

Cada personagem tem um gráfico completo na v0. Essa invocação de personagem é
uma fronteira de execução/autoridade, não uma biblioteca de subflows reutilizáveis.
O chamador escolhe os campos enviados e recebe apenas as saídas declaradas.
Trocar personagem não herda credenciais ou memórias do chamador.

Não existe distribuição global implícita, último remetente global ou polling por
personagem. Debounce agrupa comportamento efêmero, nunca identidade persistente.

## 5. Blocos v0

| Família | Blocos/operações |
| --- | --- |
| Entradas | DM recebido, grupo recebido, entrada manual/chat do painel |
| Personagem | Encaminhar evento/chamar gráfico de personagem autorizado |
| IA | Prompt + perfil de modelo + texto ou schema de saída |
| Conversação | Obter contexto recente, agregar burst/debounce |
| Controle | Condição por campo, distribuição paralela, junção, aguardar |
| Histórico | Registrar conversa, consultar histórico |
| Memória extraída | Aprender com conteúdo, consultar memória |
| Estado oficial | Ler estado, atualizar estado com versão esperada |
| Proteção | Consultar protetor com solicitante e destino estruturais |
| Saída | Enviar mensagem; preview/chat interno é um destino explícito |

Cada bloco tem portas de sucesso/erro/timeout quando aplicável. Seletores visuais
de campos e comparações tipadas bastam na v0; não oferecer uma linguagem geral
de expressões como requisito escondido. Importação de grafos também valida tipos,
versões de bloco e permissões, não só o JSON do arquivo.

Reações/emojis e humor podem ser campos de dados no schema, mas não disparam
ações ou gravações automaticamente. Uma ação nativa de reação só poderá existir
se houver uma capability de transporte implementada e autorizada; não é assumida.

## 6. Dados explícitos e etapas de IA

Uma aresta passa a saída selecionada, não o contexto inteiro da execução.
O editor oferece campos como texto da mensagem, resposta do revisor, memórias
encontradas e personagem. Dados internos de roteamento não são injetados no prompt.

Cada IA recebe somente inputs ligados, prompt configurado e personalidade se
explicitamente associada a um personagem. O contrato técnico de saída e as
restrições do motor são protegidos. Não existem recall/histórico/tools ocultos.

Modos de saída:

1. Texto (padrão).
2. Objeto estruturado com schema, incluindo texto e metadados quando desejado.

Exemplo de objeto, sem efeitos automáticos:

```json
{
  "texto": "Tá bom, Professor 😏",
  "humor": "brincalhona",
  "reacao": "😏"
}
```

O construtor de schema oferece string, número, inteiro, booleano, enum, objeto,
lista, campos opcionais e null, com limites de tamanho/profundidade. O backend
valida o schema e a saída. Suporte a structured output do provider ajuda, mas não
substitui validação local. Retry recebe erro específico seguro; saída inválida
continua na porta de erro, nunca entra como dado válido.

Referências de mensagem/pessoa apresentadas a modelos são locais à avaliação,
como m1/p1; o motor faz a tradução estrita. Nome, texto de transcript, sessionKey,
proximidade temporal e ordem de chegada não provam identidade/addressee.

Perfis de modelo têm endpoint, modelo, temperatura, orçamento e timeout. Nós podem
usar perfis distintos. Paralelismo lógico não exige carregamento simultâneo:
filas/limites por perfil e backend protegem o LM Studio, sem segundo modelo implícito.

## 7. Execução, paralelismo e efeitos reais

Gerar produz dados; só Enviar publica. Uma resposta pode alimentar outro agente,
protetor, memória ou condição sem nunca chegar a um canal externo.

Junções:

- Todos: resultados nomeados por ramo; não concatenar contextos implicitamente.
- Primeiro válido: primeiro sucesso com schema válido, vencedor registrado de
  forma atômica; cancelar perdedores quando possível.
- Independentes: cada ramo segue sem junção.

Cancelamento não desfaz envio/gravação já realizados. O editor avisa sobre efeitos
antes de uma junção primeiro-válido; não promete que cancelamento elimina duplicatas.
Timeout, erro e resultado parcial são políticas explícitas, com padrão conservador.

Proposta técnica v0: DAG dentro de cada trecho executável, com laços temporais
somente por Await/continuação durável. Rejeitar ciclos arbitrários e recursão entre
personagens; limites de passos, fan-out e custo evitam execução ilimitada.

Execuções preservam ambiente, release, nodeId, principal, evento de origem,
referências canônicas e proveniência. Tokens de autorização/segredos ficam fora
das entradas de IA e dos logs. Ramos de conversas diferentes não compartilham estado.

Retry seguro é padrão para processamento puro quando aplicável; todo resultado
já persistido é reutilizado no recovery. Temperatura baixa não torna IA determinística.
Envio usa idempotência do provider quando comprovada; sem isso, um crash/timeout
após despacho pode ficar Unknown/NeedsVerification, sem retry cego. Não prometer
exactly-once para efeitos externos sem suporte real do provider.

## 8. Esperas retomáveis

Flows curtos são o padrão. Await grava checkpoint, condição de retomada, prazo,
rota de timeout, versão e restrições antes de suspender.

A condição seleciona tipo de evento e conversa/ator/correlation ID estruturais.
Um worker retoma com claim atômico, deduplicação de evento e deadline persistente.
Após restart, retoma a mesma versão. Evento de retomada é uma entrada explícita,
não todo o histórico acumulado enquanto aguardava.

Proposta v0: uma espera consumidora por chave de correlação; conflito é detectado,
não resolvido pelo último sender. Fan-out de retomada precisa de distribuição
explícita. Se mensagem e timer competirem, o armazenamento escolhe um único vencedor.
Pausar/cancelar mantém registros; não desfaz ações externas.

## 9. Memória e estado

Separar três produtos com operações e proveniência próprias:

- Histórico: o que foi efetivamente observado/enviado, quem, quando, reply e ID.
- Memória extraída: alegações, preferências, relações e eventos com evidência e tempo.
- Estado oficial: dado controlado por ação autorizada, com revisão e atualização
  concorrente validada, não por resolução probabilística de entidades.

“Cliente afirmou que pagou” não equivale a pagamento confirmado; “NPC disse que
o rei morreu” não modifica o estado oficial. Confiança/psicologia não são inferidas
como verdades a partir de uma reação isolada. Graphiti segue responsável por fatos
e temporalidade; scheduler e estado oficial não devem virar graph facts.

Espaços nomeados têm três alcances:

1. Personagem + conversa (padrão privado).
2. Conversa, com acesso dos personagens autorizados.
3. Conjunto explícito de conversas, por exemplo Campanha ou Equipe.

Escopo define armazenamento; ACL define quem lê/escreve. Não inferir permissão
porque duas pessoas/personagens usam o mesmo número. Espaço compartilhado não
injeta conteúdo automaticamente: Consulta seleciona, retorna dados e só arestas
explicitamente ligadas os levam ao próximo prompt.

Recuperação ativa respeita tempo, limites e orçamento definidos no bloco. Histórico
entra só por consulta ligada. Ausência de resultados é ausência de evidência, não
autorização para inventar memória. Não fazer merge legado automático por nome.

## 10. Protetor, permissões e destino

Somente o administrador concede/revoga acesso e publica flows. Personagens e IA
não editam suas permissões nem impersonam outro principal por texto.

Acesso direto concede o espaço inteiro selecionado dentro de seu alcance; leitura
e escrita são permissões separadas. Acesso mediado chama o protetor do dono com:

- Solicitante autenticado pelo motor (personagem/etapa e sua autoridade).
- Destino concreto: processamento interno de etapa/principal, DM ou grupo.
- Espaço, operação e finalidade/pergunta explicitamente enviados.

O solicitante nunca recebe o corpus privado bruto só por chamar o protetor.
Saídas: Liberado (conteúdo filtrado), Negado (motivo seguro), Precisa de aprovação.
Falha ou ambiguidade não libera. V0 tem apenas o admin local e uma caixa de
pendências; não implementar usuários, papéis de aprovadores ou login multitenant.

Uma aprovação registra exatamente conteúdo/solicitante/destino/ambiente/revisão,
com expiração/revogação e uso delimitado. Alterar o destino exige nova autorização.
Nunca transportar uma autorização entre testnet/mainnet ou aceitar aprovação
expressa em texto de uma mensagem como credencial.

Garantias de código: isolamento, ACL, principal, destino, labels e envio autorizado.
Limitação explícita: um protetor LLM pode errar o significado de uma informação.
Proibições rígidas devem ser implementadas como restrições de acesso/destino,
não apenas instruções no prompt.

Proposta técnica: valores derivados carregam labels de origem/restrição; a saída
de IA herda a união das restrições dos inputs. Renomear/copiar um campo não remove
labels. Apenas o boundary de liberação autorizado pode produzir um valor liberado
para um destino. Revalidar no envio, gravação em espaço mais amplo e handoff a
outro principal. Isso impede que um ramo passe direto ao Enviar para contornar
o protetor, sem fingir que o motor consegue interpretar toda semântica textual.

## 11. Draft, publicação e ambientes

Draft é editável e não afeta produção. Publish cria versão imutável, após validar
grafo, schemas, referências, capabilities e permissões. Executores fixam a versão
e a configuração versionável; alterações entram somente em novos eventos.

Testnet/mainnet rodam simultaneamente. Cada uma tem seu release, memória, estado,
histórico, filas, checkpoints, aprovações, rotas e destinos autorizados.
Testnet aceita chat do painel e canais reais explícitos (como Grupo Teste).
Nenhum desses ambientes é sinônimo de draft: ambos executam versões publicadas.

Uma rota (conta + conversa + critérios estruturais) tem um ambiente proprietário
por padrão. Duplicação é configuração explícita, com proveniência e autorização
para ambos; não replicar dados privados entre ambientes silenciosamente.
O mesmo evento duplicado mantém ID de origem, mas execução e episódio têm escopos
de ambiente distintos. Não criar conta/número extra como pré-requisito.

Promover copia gráfico/personagens/configuração versionável e revalida bindings
locais; não copia memórias, estado, histórico, pending waits, segredos ou permissões
de envio da testnet. Credenciais são handles locais ao ambiente.

Cancelamento de execuções antigas é explícito. Revogação de acesso e pausa de
envio valem imediatamente, inclusive em versões antigas; efeitos despachados
não são desfeitos. Retomar ambiente não reenvia drafts antigos.

## 12. Compatibilidade dos dados existentes

Separar identidade física da conversa de binding de armazenamento:

```text
ConversationRef + Environment + Character/MemorySpace
                   ↓ storage binding autorizado
            Graphiti group_id / store físico
```

O codec legado da conversa não muda:

- Drift: whatsapp-group-5511981042361-1589823314-g-us.
- Plex: whatsapp-group-120363318973251043-g-us.
- DM Helena: whatsapp-dm-5511944558218-s-whatsapp-net.
- Grupo Teste: whatsapp-group-120363413037344295-g-us.

O binding legado de Filomena preserva constantes UUID, participants e Agent
agent:filomena exatamente. Não incluir environment/character retrospectivamente
nos nomes já persistidos. Novos personagens e espaços ganham bindings distintos;
copiar display name não cria uma segunda Filomena com o mesmo ID.

A testnet nunca aponta para o binding físico legado de produção. Novos bindings
usam identificação persistida/versionada e collision-safe; escolher o codec em
uma entrega própria, com golden tests antes de ativar escrita. Uma configuração
errada de binding físico deve ser rejeitada, não causar cross-environment recall.

Na migração, descrever explicitamente qual ambiente fica dono de cada binding
legado, inclusive do Grupo Teste: não misturar dados existentes em uma testnet
nova por conveniência. Nenhuma cópia/migração automática do grafo nesta fase.

## 13. Armazenamento operacional e observação

Esperas/retries e execução retomável exigem armazenamento transacional. Proposta:
SQLite local para releases, runs, checkpoints, eventos deduplicados, effect journal,
aprovações, mappings e estado oficial; Graphiti/FalkorDB segue separado para memória.
Não modificar o banco interno do OpenClaw. Manter backups e migrações versionadas.

Historical observation deve continuar independente de respostas. O fluxo inicial
tem um ramo explícito de registro antes da arbitragem, com fila durável independente
para indisponibilidade do Graphiti não bloquear envio. Replay usa idempotência,
não reingestão de sliding windows. Somente um dono grava cada evento por espaço.

Um provider message gera no máximo um episódio por espaço autorizado. Deduplicar
antes do fan-out evita múltiplas cópias no mesmo espaço compartilhado. Um bloco
Aprender pode derivar fatos referenciando o episódio, não duplicar o histórico bruto.

Gravar fala social externa somente após confirmação de envio. Texto gerado mas
não enviado é resultado interno, não evento observado. Em conta compartilhada,
o sender externo não prova qual personagem falou: usar o effect journal/correlação
exata do motor. Outbound sem autoria comprovada permanece desconhecido.

## 14. Editor local e administração

Vue/TypeScript servido pelo mesmo executável .NET em 127.0.0.1. Build no startup
quando fontes mudarem, com cache último-válido e processo Node transitório; não
instalar dependências silenciosamente. Publish pode incluir assets pré-compilados.

Editor com canvas, portas tipadas, seletor visual de campos, painel de prompt/schema,
personagens, espaços/ACL, ambientes, rotas e versões. Configurações gerais e estado
das integrações permanecem acessíveis. Não é apenas um editor JSON com uma imagem
do grafo: conexões, configuração e execução devem ser operáveis visualmente.

Mostrar o ambiente claramente em toda ação, confirmação de destinos reais e
diferença draft/publicado. Testar um draft cria execução isolada na testnet, nunca
troca o release publicado dela. Chat interno e saída real são destinos distintos.

Trace por bloco mostra entradas/saídas autorizadas, versão, duração, custo/tokens
quando disponíveis, erro e origem dos dados. Console/SSE padrão omitem conteúdo
privado; inspeção detalhada é admin-only, ambiente específico, com retenção limitada.
Não expor credenciais, headers de autenticação ou raciocínio interno por padrão.

Local-only não elimina CSRF/DNS rebinding: validar Host/Origin, proteger mutações,
não habilitar CORS, limitar corpo e evitar v-html de conteúdo não confiável.
Preservar a rota da bridge e não bloquear o cliente máquina com controles de browser.

## 15. Primeiro gráfico pronto

O primeiro personagem é a Filomena atual, com seus valores/prompt e permissões
preservados, não uma personalidade nova. O editor exibe explicitamente:

```text
DM/grupo → entrada/observação → burst/debounce → contexto recente → árbitro IA
                                                                  ↓
                                            responder | esperar | ignorar
                                                ↓          ↓          ↓
                                          geração IA     aguardar    terminar
                                                ↓
                                      autorização/Enviar
```

Consulta de memória, personalidade e contexto de geração são blocos/bindings
visíveis, não middleware secreto. A versão de equivalência reflete as capabilities
realmente ligadas hoje: não fingir que o C# já faz recall Graphiti se ainda é plugin.
O ramo de memória só troca de dono após integrar/testar e desativar captura duplicada.

Arbitragem/grupo e DM continuam distintos. Preservar espera de pensamento incompleto,
follow-up conservador, aliases usados como sinais e proteção contra resposta antiga.
Permissão para responder não significa resposta obrigatória a cada evento.

## 16. Entregas e gates

Este é um programa de entregas; cada entrega recebe plano e critérios específicos,
evitando um único refactor gigante. Nenhuma substitui o runtime real implicitamente.

| Entrega | Resultado demonstrável | Gate principal |
| --- | --- | --- |
| A. Contratos e compilador | Personagens, portas/schemas, draft/release, ambientes; validação sem execução externa | Identidade, versões, grafo inválido e bindings isolados |
| B. Executor durável | IA fake, condições, paralelo/junções, waits/restart, effect journal | Concorrência, replay, timeouts e resultado incerto |
| C. Editor vertical | Canvas Vue + backend local; criar/publicar/testar grafo em chat interno | UI real, persistência e API local segura |
| D. Memória/protetor | Histórico/fatos/estado, ACL/labels, aprovação admin e Graphiti adapter | Isolamento, proveniência e nenhuma liberação em falha |
| E. Transporte e equivalência | Filomena atual como grafo, testnet real allowlisted | Sem segunda LLM, sem dupla captura/envio, golden legacy |
| F. Ativação controlada | Releases testnet/mainnet simultâneos e promoção | Helena aprova rotas, gravação legada e canário |

Testes obrigatórios ao longo das entregas:

1. Dois personagens compartilham conta, mas não contexto/memória sem ligação e ACL.
2. Etapa recebe somente portas ligadas; resposta estruturada inválida vai ao erro.
3. m1/p1 são locais, nomes/device IDs não alteram identidade canônica.
4. Ramos concorrentes não trocam outputs; primeiro-válido escolhe um vencedor.
5. Wait sobrevive a restart e evento/timer concorrentes retomam uma única vez.
6. Falha após despacho não repete envio incerto; reconexão não é prova de nondelivery.
7. ACL/protetor verifica solicitante e destino, inclusive após transformação/handoff.
8. Unknown/erro/negado não inclui conteúdo privado; aprovação não serve outro destino.
9. Histórico registra alegação; estado oficial não muda por extração probabilística.
10. Private/shared/multi-conversation spaces obedecem limites e filtros temporais.
11. Testnet/mainnet nunca compartilham dados por padrão; promote não copia memória.
12. Draft editado não modifica release/run/wait antigo; revoke/pausa bloqueiam saída.
13. Golden namespace/UUID antigos permanecem byte-for-byte iguais.
14. Idempotência de ingestão por espaço, outbound social só após confirmação.
15. UI local, build/cache/publish, recovery de config, API segura e browser tests.
16. Regressões da Filomena atual passam antes de qualquer corte de produção.

Gate desta especificação: revisar a consolidação e as propostas técnicas indicadas
(DAG/Await, event claims, labels, SQLite e bindings legados). Depois detalhar primeiro
a entrega A. Não reiniciar a implementação do painel antigo.
