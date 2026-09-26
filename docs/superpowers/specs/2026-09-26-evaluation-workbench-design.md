# Oficina de avaliação e ajuste de fluxos

Status: desenho proposto para revisão; implementação não iniciada.
Data: 2026-09-26.

## 1. Intenção e decisões acordadas

Criar primitivas reutilizáveis para um agente investigar problemas, propor
configurações, executar experimentos e comparar qualidade, consistência,
latência e custo. Filomena é o primeiro consumidor, não uma dependência do
domínio. Outros projetos, steps e fluxos devem usar as mesmas operações.

Helena aprovou guardar evidências em banco e preparar ferramentas operáveis
por agentes. **Não haverá promoção automática nesta versão.** O agente pode
operar dentro do escopo autorizado; o término de um experimento nunca altera
a configuração ativa. Ativação continua sendo uma operação explícita, separada,
executada pelo responsável operacional dentro da autorização vigente.

Não exigir nova aprovação para cada edição de candidato já coberta pelo escopo
autorizado. Isso não permite ampliar orçamento, permissões ou mudanças live.

Esta especificação não autoriza executar experimentos, subir containers,
reativar Filomena ou modificar o ambiente ativo agora.

## 2. Estado observado e escolha de armazenamento

O repositório já declara PostgreSQL para SpiceDB e FalkorDB para Graphiti em
`runtime/docker-compose.yml`. Há persistência de contexto por arquivos JSON,
como `LiveSessionContextStore`. Essa inspeção não verifica serviços rodando.

Escolha: PostgreSQL dedicado à oficina, sem reutilizar banco, usuário ou volume
do SpiceDB/Graphiti. Relações, constraints e transações sustentam a integridade;
JSONB admite configurações e payloads variáveis sem transformar tudo em tabelas
específicas de cada personagem. Não introduzir MongoDB nem substituir stores
operacionais existentes neste slice.

Na implementação futura:

- container e volume nomeados próprios; imagem fixada, sem atualização automática;
- porta host alocada pelo Docker e publicada somente em loopback;
- descoberta da porta pelo cliente, sem hardcode e sem procurar porta livre
  para liberá-la antes do bind, evitando essa corrida;
- usuário de migração separado do usuário limitado da aplicação;
- segredo fora do repositório e fora dos registros de experimento;
- rede sem egress externo para o banco; publicação local não é, sozinha,
  comprovação de isolamento de rede;
- banco funciona sem internet; workers podem acessar providers remotos somente
  quando a execução e esse acesso estiverem autorizados;
- migrações versionadas, backup e restauração verificável antes de uso durável.

Não acoplar disponibilidade do runtime de conversa à disponibilidade da oficina.
Se o banco da oficina estiver indisponível, novos experimentos ficam bloqueados;
o runtime segue sua configuração aprovada, sem fallback de modelo improvisado.

## 3. Fronteiras

1. **Domínio de avaliação:** revisões, casos, critérios, execuções, avaliações,
   comparações e decisões. Sem nomes de Filomena, WhatsApp ou providers fixos.
2. **Executor:** adapta as fronteiras reais de step/fluxo já existentes.
   Não replica um segundo motor de conversa.
3. **Persistência:** PostgreSQL, artifacts e queries de histórico.
4. **Ferramentas:** operações da aplicação consumidas primeiro por CLI, depois
   REST/MCP, sem duplicar validação ou authority por interface.
5. **Ativação operacional:** fora do executor de avaliação. Não é consequência
   de uma nota, ranking, resposta de LLM ou status de experimento.

CLI é o primeiro transporte das ferramentas. REST autenticado e MCP sobre HTTP
podem ser adicionados depois sobre o mesmo contrato. Não implantar três servers
ou um framework de plugins para entregar o primeiro experimento.

## 4. Modelo de evidência

Todas as entidades pertencem a um workspace/projeto. Referências cruzadas não
podem misturar projetos acidentalmente. IDs são opacos; nomes são apresentação.

| Entidade | Conteúdo e finalidade |
|---|---|
| Target revision | Step/subfluxo/fluxo, contratos e bindings necessários para executar |
| Configuration revision | Perfil completo e imutável: provider/modelo, parâmetros, prompt, schema, configuração do fluxo e hashes |
| Dataset revision | Casos exatos, origem, cortes, tags e divisão desenvolvimento/validação reservada |
| Case | Entrada/contexto, requisitos, resultados aceitáveis e classificação de criticidade |
| Evaluator revision | Critério, implementação ou modelo julgador, rubric e versão |
| Experiment | Hipótese, baseline, candidatos, dataset, avaliadores, repetições e orçamento aprovados |
| Trial/attempt | Caso, candidato, repetição, step e tentativas reais; saída/erro, latência, usage e proveniência |
| Assessment | Medição ou julgamento, evidência, origem humana/determinística/modelo, incerteza e supersessão |
| Comparison | Referências exatas, diferenças pareadas, cobertura, dispersão e limitações |
| Decision | Selecionar, rejeitar, adiar, não mudar; justificativa e comparações utilizadas |
| Operational activation record | Registro de uma ativação externa explícita e sua autorização; não um comando executado pelo benchmark |

Cada payload possui versão de contrato. Configurações referenciam aliases de
credenciais, nunca o segredo. Prompts/inputs completos ficam em artifacts de
acesso restrito; logs normais carregam refs, hashes e métricas.

Registrar erro, timeout, cancelamento, ausência de métrica e resultado inválido
explicitamente. Nenhum deles vira sucesso vazio ou custo zero. Não coletar cadeia
de raciocínio privada; guardar saídas necessárias e justificativas verificáveis.

Revisões utilizadas por execuções são imutáveis. Correções de julgamento criam
nova avaliação vinculada à anterior, sem apagar a divergência. Isso não exige
reter dados pessoais para sempre: exclusão/redação autorizada pode remover um
artifact, mantendo registro da remoção e marcando o caso como não reexecutável.

## 5. Perfis configuráveis sem alterar o core

Perfil declara provider, modelo, parâmetros suportados, contratos de entrada e
saída, revisões de prompt e configuração. Capability mismatch gera rejeição
explícita; não remover silenciosamente parâmetros para conseguir executar.

Aliases como nomes comerciais podem mudar de implementação. Registrar também
identificadores retornados pelo provider, versão disponível, data e limitações
de pinning. Preservar o pedido não garante resposta idêntica de modelo remoto.

Contratos de comportamento e segurança não podem ser relaxados pelo candidato.
Critérios, limites de autoridade e orçamento pertencem à política aprovada,
não ao texto gerado pelo agente otimizador.

A meta de longevidade é ajustar modelos compatíveis, parâmetros, prompts,
datasets e políticas sem editar o motor. Novos protocolos de provider,
capacidades e atualizações de segurança ainda podem exigir manutenção de código.
Não prometer anos sem qualquer atualização de software.

## 6. Execução e orçamento

Experimento fixa todas as revisões antes da primeira chamada. Execução registra
tentativas de cada step, inclusive resumo, correção e regeneração quando parte
do alvo. Utiliza o pipeline real com efeitos externos desabilitados por construção:
sem sender de produção, escrita de memória ou acesso a ferramentas arbitrárias.
Inputs de conversa são dados, não instruções autorizando ações da oficina.

Estados: planned → running → completed/failed/cancelled/interrupted.
Completude exige reconciliar todas as tentativas iniciadas. Chave idempotente
impede dupla criação de job; leases e ownership controlam workers concorrentes.
Isso não promete exactly-once em APIs remotas. Após crash, tentativa cuja resposta
é desconhecida permanece indeterminate e não é reenviada silenciosamente.

Orçamento limita chamadas, tokens quando controláveis, gasto, concorrência e
recursos locais. Reservar capacidade antes do dispatch e reconciliar usage
depois. Custos desconhecidos impedem execução desassistida sob teto financeiro
estrito até haver uma estimativa conservadora autorizada. Admitir limites do
provider: falhas podem ser cobradas sem usage devolvido.

Latência separa fila, execução, retries e total; medir duração com relógio
monotônico e registrar instante civil para auditoria. Identificar contexto de
carga/cache/hardware quando disponível, sem atribuir toda variação ao modelo.

Snapshot de preço inclui fonte, vigência, moeda e componentes suportados, como
tokens de entrada/saída/cache e cobrança por chamada. Separar consumo observado,
custo estimado e cobrança confirmada. Reprecificar historicamente cria uma
projeção nova; não altera o custo registrado no experimento original.

Local: registrar tempo/ocupação disponível da GPU e recursos separadamente.
Não inventar energia medida nem classificar custo local como zero.

## 7. Comparação e escolha do próximo experimento

Não enumerar o produto cartesiano de todas as configurações.

1. Agrupar falhas por critério, step e contexto; considerar frequência,
   severidade e cobertura da observação.
2. Registrar hipótese e priorização: impacto esperado, evidência, incerteza,
   custo de investigar e benefício de obter informação.
3. Comparar mudanças pequenas, pareadas nos mesmos casos, com baseline
   contemporâneo, ordem intercalada e repetições.
4. Ampliar apenas candidatos promissores; declarar regras de parada antes
   de executar e não ocultar casos ruins por encerramento seletivo.
5. Testar interações relevantes entre poucos fatores finalistas e avaliar
   o fluxo completo antes de recomendar uso operacional.
6. Registrar também decisões de não mudar e motivos para reabrir a hipótese.

Uma mudança em step upstream pode mudar o contexto downstream. Replay de
inputs fixos mede o step isolado, não valida sozinho o fluxo end-to-end.

Ranking primeiro aplica gates de qualidade/contrato/segurança. Entre aprovados,
mostrar alternativas não dominadas de custo/qualidade/latência; score ponderado
é opcional e só comparável sob a mesma política versionada. Exibir amostras,
falhas por categoria, consistência por caso e incerteza, não só média agregada.

Cache de resultados reaproveita evidência identificada, não substitui novas
repetições ao medir variância. Evidência antiga perde aplicabilidade quando
mudam modelo, prompt, dataset, rubric ou ambiente relevante.

Holdout não entra no ciclo de sugestão de ajustes; restringir artifacts acessíveis
ao proponente. Julgadores LLM são versões auditáveis, podem discordar e não
substituem ground truth independente. Um candidato não aprova sua própria
promoção. Revisões de rubric exigem reavaliação para comparações equivalentes.

## 8. Ferramentas e autoridade

Operações semânticas mínimas:

- consultar targets, perfis, casos, problemas e experimentos anteriores;
- registrar uma revisão candidata e sua hipótese;
- planejar/estimar um experimento;
- executar, consultar, cancelar e reconciliar execução autorizada;
- registrar avaliação humana ou automática com proveniência;
- comparar resultados e produzir uma recomendação;
- registrar uma decisão explícita e exportar um bundle revisado.

O bundle exportado não instala, não reinicia serviços nem troca o perfil ativo.
Nenhuma ferramenta de avaliação oferece SQL livre, shell arbitrário, publicação
de código ou credenciais ao modelo. Autorização é por workspace/projeto/operação
e orçamento; não apenas por origem localhost. REST e MCP devem ter paridade de
authority, validação, auditoria, idempotência e cancelamento com a CLI.

O agente pode propor e executar pesquisas dentro da autorização existente.
**Não existe endpoint de auto-promoção nesta versão.** Registro de ativação
aceita somente identidade operacional autorizada; avaliação não pode fabricá-la.
Preparar separação de permissões para evolução futura, não habilitá-la agora.

## 9. Falhas, durabilidade e privacidade

- Falha da oficina não troca configuração do runtime nem bloqueia conversa.
- Falha de persistência impede novas chamadas sem rastro durável.
- Cancelamento interrompe novos dispatches; chamada remota já iniciada pode
  terminar/custar. Registrar esse fato, sem alegar cancelamento retroativo.
- Rollback de configuração não desfaz efeitos externos já ocorridos. O executor
  de avaliação não deve produzir esses efeitos em primeiro lugar.
- Restringir artifacts por projeto e papel, com política de retenção e exportação.
- Backup inclui dados e artifacts necessários; restauração deve comprovar
  referências e hashes. Não depender do filesystem efêmero do container.
- Desacoplar credenciais, datasets e memória conversacional. Graphiti não é
  usado como store de experimentos nem modificado pelo benchmark.

## 10. Primeiro slice e exclusões

Primeiro slice: contratos/revisões, persistência PostgreSQL, execução local
controlada, comparação e CLI. Usar Ler Situação/classificação+resumo como caso
concreto, pelos adapters existentes, preservando o isolamento de efeitos.
O detalhe de implementação depende da aprovação desta especificação.

Ficam para depois: scheduler de auto-healing, promoção automática, otimização
Bayesiana, frontend completo, REST/MCP publicados, novos providers e framework
de plugins. A API de aplicação deve permitir esses adapters sem duplicação,
mas não implementá-los antecipadamente.

## 11. Critérios de aceitação futuros — não executados nesta etapa

- Histórico identifica exatamente candidato/baseline/caso/avaliador e retries.
- Alterar configuração gera revisão nova; execução em curso permanece pinada.
- Resultado do step real é capturado sem sender ou memória mutável live.
- Falha/outage/schema inválido não vira empty/success; custo ausente não vira zero.
- Orçamento e concorrência são aplicados inclusive com dois callers simultâneos.
- Crash não duplica silenciosamente chamada incerta nem perde sua evidência.
- Comparação exclui mistura de revisões incompatíveis e mostra casos incompletos.
- Preço atualizado permite projeção sem reescrever custo histórico.
- Usuário/agente sem authority não acessa artifacts alheios nem amplia orçamento.
- Nenhum ranking, conclusão de job ou ferramenta avaliativa ativa configuração.
- Trocar perfil de modelo compatível não exige editar o core.
- Banco restaurado preserva experimentos e referencia corretamente artifacts.

## 12. Próximo checkpoint

Revisão desta especificação por Helena. Depois, plano de implementação pequeno
e sequencial. Nenhum container, migração, teste de modelo ou alteração de
produção foi executado para elaborar este desenho.
