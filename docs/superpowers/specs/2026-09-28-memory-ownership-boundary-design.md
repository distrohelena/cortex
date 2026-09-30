# Ownership de memória: proposta incremental da fase 3

Status: proposta específica derivada da auditoria; **não implementada nem ativada**.
Referência: `2026-09-27-intelligence-platform-evolution-design.md`, seções 8–10 e fase 3.
Não substitui as pendências de aceitação PostgreSQL da oficina.

## Evidência do checkout

| Local / símbolo | Comportamento observado | Consequência |
|---|---|---|
| `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Memory.cs`, `ConversationNamespace` | Deriva namespace de channel/native conversation; não usa projeto de isolamento ou conta | Não pode ser ownership universal para consumidores novos |
| Mesmo arquivo, `QueryMemoryAsync` | Monta `MemoryQuery`, valida pessoas, consulta o reader único do runtime, aplica `MemoryResultPolicy` e registra memória para Boundary | Preservar todo esse processamento; mudar somente a composição autorizada |
| `src/Filomena.Core/Memory/MemoryContracts.cs`, `MemoryQuery` | Namespace, AgentUuid e participantes são dados do contrato; nenhum grant é carregado | Um request sozinho não comprova autorização |
| `src/Filomena.Core/Memory/MemoryIdentity.cs`, `Person` | Canonicalização aceita WhatsApp/Telegram e deriva UUID no namespace | Não estender por nomes de exibição; não fingir que documento Damma é pessoa de chat |
| `src/Filomena.Core/Identity/FilomenaIdentity.cs`, `AgentUuid` | UUID inclui namespace e `agent:filomena` | Renomear agente altera identidade persistida |
| `src/Filomena.Memory/GraphitiMemoryReader.cs`, `QueryAsync` | Exact consulta um neighborhood por pessoa; hybrid pode acrescentar busca semântica | Revalidar antes de cada chamada física, não apenas antes do método externo |
| Mesmo arquivo, `Endpoint` | Verifica UUID/labels/stable_id; reconhece `agent:filomena` | Não generalizar silenciosamente a semântica de relações/Boundary |
| `src/Filomena.Memory/GraphitiReadClient.cs` | Adapter MCP lazy, somente neighborhood e facts, namespace fornecido como argumento | Permissões de host precisam estar acima desse cliente; não adicionar memória escrita ao facade de leitura |
| `src/Filomena.App/NativeShadowCommand.cs`, `ManualShadowCommand.cs`, `LocalPromptPlayground.cs` | Composition roots criam `GraphitiMemoryReader` diretamente | Preservar legado; novos consumidores não devem receber reader bruto |
| `src/Filomena.Core/LiveSessionContextStore.cs`, `Load` | Contexto privado vincula authority, revisão e fingerprints dos journals | Não migrar diretórios/checkpoints implicitamente nem fazer inferência de ownership por nome |
| `src/Filomena.App/NativeMemoryHost.cs` | Valida sessão e namespace da configuração contra binding operacional antes de compor ingestão | Preservar escrita separada; esta primeira extensão não concede ingestão a consumidores novos |

Esta inspeção não verificou grants SpiceDB implantados, ownership de dados reais,
segredos, serviços ou resultados live. Não constitui prova de isolamento backend.

## Problema preciso

O contrato atual de memória atende à Filomena sob composition roots confiáveis.
Não oferece, por si, uma fronteira reutilizável de acesso entre aplicações. Dar o
mesmo reader a Dota/Damma e esperar que escolham namespaces corretos não é isolamento.
Uma pessoa com a mesma identidade em dois produtos também não concede compartilhamento.

## Alternativas

1. **Prefixar todos os namespaces/UUIDs com project ID.** Rejeitada: altera dados,
   identidade canônica, vínculos de episódios e autorização existentes antes de
   haver inventário/migração verificável.
2. **Manter readers brutos e adicionar filtros nos callers novos.** Rejeitada:
   deixa a autoridade distribuída e acesso direto por ID/query sem fronteira única.
3. **Binding legado explícito + readers bound pelo host.** Recomendada: mantém
   IDs e pipeline; adiciona prova de ownership antes de acesso, sem mudar dados.

## Boundary proposto

### Manifesto de ownership

O host registra um manifesto versionado, não um campo no projeto/graph/prompt:

- projeto de isolamento opaco, distinto do ID/revision do flow;
- binding ID e revision imutável;
- SessionKey completo: runtime/personagem/channel/account/conversation;
- namespace físico existente e identidade de agente esperada;
- operações permitidas e limites de consulta;
- referência à evidência de ownership legado, sem incluir segredo;
- estado `verified` ou `unresolved`; só `verified` pode produzir acesso.

IDs reais devem ser inventariados em artifact privado quando essa leitura for
autorizada. Nenhum valor é inferido de nome de pessoa, texto ou semelhança de JID.
Dois scopes não podem registrar o mesmo namespace físico como propriedade exclusiva.
Sharing cross-project não será implementado como exceção nesta versão.
Ownership desconhecido permanece indisponível a novos consumidores.

### Autoridade e composição

Reutilizar o padrão local de grants opacos da inferência, mas não reutilizar um
grant de inferência como permissão de memória. Não importar EvaluationAccess no
runtime, não criar login paralelo e não substituir as relações SpiceDB existentes.

O plano de implementação deverá escolher entre extrair o pequeno registro comum
de grants ou manter a composição de memória específica; não criar agora um motor
universal de policies. O contrato observável é o mesmo: host resolve principal,
scope/binding e operações; payload só carrega dados de consulta.

A factory de memória recebe o contexto de batch já capturado pelo runtime,
incluindo sessão completa e revisão, assim como a factory de completion existente.
Não criar um segundo contexto de execução nem tratar revision pin como autoridade
permanente. Contexto de review/preview também precisa usar a composição bound.
Construtores legados continuam compatíveis; novos roots não recebem adapter bruto.

### Consulta

1. Resolver binding/grant com scope e sessão exatos; rejeitar ausência/ambiguidade.
2. Verificar namespace, agent identity e participantes estruturais da query contra
   binding/policy confiável; não corrigir namespace errado por fallback silencioso.
3. Copiar coleções antes do primeiro await; manter orçamento de itens/caracteres.
4. Antes de cada chamada física exact/semantic do cliente, revalidar revogação e
   operação. Um hybrid não recebe permissão eterna por ter começado válido.
5. Preservar validação real de proveniência e MemoryResultPolicy; timeout/falha
   continua falha, não `[]`.
6. Antes de devolver conteúdo, verificar se o acesso continua autorizado. Revogação
   durante o recall impede divulgar resultado, embora não desfaça a consulta remota.
7. Boundary compiler e modelo recebem os mesmos valores seguros de antes, sem
   grant, segredo ou novo ID privado incluído no prompt.

Estas regras não prometem sandbox contra código arbitrário executando dentro do
mesmo processo/host. A composição controla as dependências entregues ao consumidor.

### Legado e domínios novos

O binding Filomena preserva precisamente namespace/agent UUID atuais, sob ownership
verificado. Não trocar `agent:filomena` nesta fatia nem migrar dados existentes.
Um reader genérico para outro agente exige contrato de identidade explícito e seus
testes; não anunciar esse suporte a partir do binding legado.
Damma documental e estado de partida Dota não são forçados a MemoryItem ou SessionKey.
Seus stores deverão usar o mesmo conceito de ownership, com seus próprios contratos.

## Critérios de aceite e evidência

| Prova | Verificação local proposta | Integração necessária depois |
|---|---|---|
| Scope errado / mesma pessoa em outro produto | Query negada antes do cliente; contraprova válida chama uma vez | Negação com dados de teste em backends reais |
| Account/DM/grupo errado | Factory com SessionKey distinto rejeita, sem derivação por display name | Binding host-owned real revisado |
| Namespace legado duplicado entre scopes | Manifesto rejeita ownership ambíguo | Inventário privado dos dados legados |
| Grant revogado enquanto consulta aguarda | Barreira controlada impede segunda chamada hybrid e devolução de conteúdo | Revogação vigente também no backend |
| Fato foreign / provenance ausente | Adapter real com respostas controladas falha, sem conteúdo no modelo | Queries exact/semantic/hybrid e SpiceDB autorizadas |
| Empty versus outage | Empty válido preservado; erro/cancelamento não vira empty | Backend indisponível sem fallback permissivo |
| Flow preservado | Testes reais v3, Boundary/silêncio/Shadow/review/preview, modelo controlado | Não ligar conversa para testar infraestrutura |
| ID direto, relação, cache, job, export | Enumerar cada acesso e negar scope alheio; nenhum caller omitido pode ser declarado coberto | Integração de stores/SpiceDB; caches existentes inventariados |
| Ingestão e checkpoint | Caracterizar contratos atuais, sem conceder escrita ou migrar journal | Plano específico para ownership durável e backfill, antes de ativar |

Primeiro incremento pode provar leitura bound e compatibilidade local, mas **não
encerra fase 3**: inventário privado, acesso por ID/relação e backend continuam gates.
Manifestos demonstrativos devem ser sintéticos; não preencher ownership real por palpite.

## Desativação e migração

Mudanças de composição são opt-in e não alteram roots operacionais automaticamente.
Sem migrations, namespace renames, dual-write ou backfill nesta proposta inicial.
Para migração posterior: manifesto verificável → mudanças aditivas → backfill com
checkpoint → comparação → corte explícito; reversão de app não desfaz dados escritos.

## Próximo passo

Plano local preparado, ainda sujeito à revisão desta proposta:
[`2026-09-28-host-bound-memory-read.md`](../plans/2026-09-28-host-bound-memory-read.md).
Inspeção adicional confirmou: participation review bloqueia memória; somente
execução normal e response preview chegam a `QueryMemoryAsync`. Preservar esse
bloqueio, não criar recall no review para satisfazer uma interpretação ampla da factory.

Revisar esta proposta específica e detalhar o plano com assinaturas/arquivos/testes,
preservando a autorização operacional separada. A continuidade da oficina não
precisa esperar essa revisão. Não implementar um facade parcial e chamá-lo de
isolamento completo da plataforma.
