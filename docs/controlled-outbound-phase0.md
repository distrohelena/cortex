# Controlled Outbound — infraestrutura Fase 0

Data: 2026-09-11. Apenas disabled/shadow. Nenhum outbound real ou Shadow Live executado.

## Implementação

- `src/Filomena.Core/Outbound/ShadowOutboundHost.cs`: authority do host, allowlist exata de SessionKey, DM-only, documento revisado pinado por hash, roles/portas de recall/compiler/generator/enforcement vinculadas administrativamente. Nenhum título de node decide autoridade. `Rearm` é API do host; não existe endpoint de projeto que a invoque. Mode é a solicitação operacional; EffectiveMode nunca é Enabled nesta fase.
- `OutboundExecution`: contexto implícito criado somente pelo runtime com origem/execution/IDs de inbound. Referência privada ao TypedValue corrente, recall e conjunto compilado; mesma string/hash/JSON não herda autorização. Generation/regeneration revoga a aprovação anterior. O resultado `permitir` normal/fabricado não cria recibo. Empty set exige recall confiável bem-sucedido e compilação daquele mesmo valor.
- `Enviar`: `exec + message:Texto`; sem recipient, override de canal, retry, metadata, provenance serializada ou configuração de modo. Outputs `status` e `diagnostico`; controles `disabled`, `shadowed`, `blocked`, `failed`. Sem saída sent/then. O compilador e o backend rejeitam configuração extra; editor mostra as restrições read-only.
- Hooks no runner acompanham valores reais de recall, compiler, generation e retorno interno do enforcement. Nenhuma mudança de contrato/prompt/schema/evaluator/aggregation. Acrescentada ao backend somente aceitação de três campos de composição já existentes, necessários ao roundtrip: enforcementStrategy, complementaryContractVersion, aggregationPolicyVersion.
- Max-one tentativa por execution, inclusive quando a primeira falha/bloqueia. Segundo Enviar retorna duplicate_attempt. Invalid provenance, recall outage, EnforcementFailure e Block não ganham autorização.

## Modos e journal

Host inicia disabled em toda construção/restart. Shadow exige rearmamento explícito, allowlist e aprovação privada válidos. Guarda consulta modo no sink, com fence local contra rearm concorrente. Uma execução iniciada enquanto shadow pode terminar disabled se desarmada antes de Enviar.

Enabled é impossível: capabilities incompletas → `transport_capabilities_incomplete`; mesmo um fake com capabilities completas → `phase0_dispatch_unavailable`. Capability single physical message ausente é blocker independente. O host não recebe sender/transport/history writer: dispatch real não está implementado.

Journal pequeno append-only, arquivo local do host com writer exclusivo e flush-to-disk antes de shadowed. Namespace `shadow:v1` é terminal. Guarda apenas refs opacas e revisão, não conteúdo nem JID. Reabertura reconstrói índices de execution e inbound; novos GUIDs/rebatch não promovem nem repetem o shadow previamente registrado. Corrupção/registro incompleto bloqueia abertura; falha de escrita impede continuar gravando naquele writer. Sem retries, queue, worker ou expiração automática de tombstones.

Vocabulário futuro `real:v1`: pending/indeterminate exigem reconciliation_required e bloqueiam novo outbound. A Fase 0 não cria esses estados nem oferece resolução; futura operação explícita será sent/not_sent/abandoned com evidência, nunca texto/timing. Sem retry, history append ou suposição de entrega. Inbound continua preservado pelo runtime. A coordenação completa de inbox/send/history/reconciliation futura não foi antecipada.

Shadow NÃO append history, NÃO escreve Graphiti, NÃO chama transport e NÃO aplica transição por fala. Mesmo wiring shadowed→Trocar Estado produz só diagnóstico hipotético, não TargetStateId para commit. `wouldTransitionTo` aparece no trace. Transições legítimas anteriores/sem tentativa continuam com a FSM existente. Não existe shadow-state paralelo. A dinâmica pós-fala real e predecessor-outbound history complement ficam para DM Live.

Trace específico: execution, sessionRef opaca, channel, modo solicitado no sink, allowlist/provenance decision, generationRef, boundarySetRef, enforcement revision, attempt ordinal, journalRef, status/diagnóstico e transição hipotética. Nenhum texto/JID novo nesses metadados. As saídas de execução existentes continuam separadas do trace operacional.

## Verificação local

| Verificação | Resultado |
|---|---:|
| Core completo | 452 passed; 10 live skipped; 0 failed |
| Novos casos Core Outbound (incluídos acima) | 31 passed |
| Backend focado: ShadowProject, PlaygroundProject, MultiDetectorGate, LunaGeneratorGate, BoundaryGate | 26 passed |
| Frontend completo | 102 passed |
| Build Filomena.slnx --no-restore | 0 warnings / 0 errors |
| dotnet format whitespace, novos arquivos, verify-no-changes | exit 0 |
| git diff --check | sem erros de whitespace |

Casos adversariais: copied/reconstructed/modified candidate, fake Allow, fake compilation, cross-execution e cross-session mesmo com ambas allowlisted, candidate pré-regeneration, recall substituído, empty success versus outage, detector Block/failure, projeto alterado, group, outro DM, kill antes do sink, segundo Enviar, replay após restart, writer concorrente, journal incompleto, pending/indeterminate e ausência de single-part.

Integração usa runtime/compiler/enforcement reais com Reader/Completion fake. Zero chamadas reais de transporte, messages_send, modelos ou Graphiti. Backend tests abriram apenas servidores HTTP locais efêmeros de teste. Não conectaram recursos live de Helena.

Fixture aprovada `typed-flow-slice42.json` permaneceu com SHA256 `C468F9EE2594A1C671B7BDA11E67A5F95CE39F88DCCD8F82984B34E3E6540676`. Nenhuma edição em Boundary compiler/detectors/evaluator, prompts, FSM, memória, OpenClaw, Gateway, configurações pessoais ou DREAMS.

## Readiness e próximo passo operacional

**Infraestrutura local pronta para preflight; Shadow Live não iniciado e ainda não conectado ao aplicativo live.** O host está disponível como dependência opcional de SessionFlowRuntime. Os callers existentes continuam sem host e portanto disabled. O endpoint de simulação não foi elevado a ingress confiável.

Antes das ~20 interações reais é necessário preparar/revisar o bootstrap do piloto: encaminhamento de inbound autenticado/confiável ao novo runtime (o worker legado ainda usa outro lifecycle), confirmar exatamente a sessão/conta/canal e self-message classification, conectar o reader Graphiti LIVE com isolamento/empty-vs-outage, revisar uma cópia do projeto com Enviar em vez do sink simulado, piná-la no host, escolher local durável do journal e oferecer rearm/kill switch administrativo. Não iniciar o worker legado supondo que ele executa este graph ou que não tem outro caminho outbound.

Esses recursos e a ativação continuam sob controle de Helena; nenhum foi iniciado/reconfigurado. Não declarar gate Shadow Live aprovado com os testes sintéticos. DM Live segue impossível nesta implementação e bloqueado pelas lacunas do MCP, incluindo chunking.

Sem commit de fechamento: worktree tem extensa base experimental pré-existente não commitada e arquivos sobrepostos; não empacotar automaticamente toda essa história como implementação nova. Sem push. Recomendada revisão de código autorizada antes de ligar o piloto; nenhuma revisão independente foi iniciada sem autorização.
