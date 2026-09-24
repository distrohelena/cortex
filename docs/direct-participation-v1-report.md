# Direct Participation Rule V1

## Resultado

**O blocker direto foi corrigido no graph, sem mudar prompts, modelos ou core.**

15/15 chamadas diretas live (5 por estado) selecionaram responder; zero calar, zero esperar, zero chamadas a Decidir Presença. O C1 que antes produziu `direto + chamada_direta + calar` passou 5/5.

**O gate completo permanece vermelho por outro comportamento, preservado:** 1/5 migrações classificadas `nenhum` recebeu responder de Decidir Presença. O novo ramo direto não foi executado. Por isso não executei o mini-shadow de 8–10 interações nem Graphiti Live Preflight. Não corrigi a falha não-direta neste slice.

## Alteração exata e versão

[Projeto v3](manual-shadow-project-v3.json), ID operacional `manual-shadow-direct-rule-v3`, versão operacional 3, SHA256:

`47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`

A [v2](manual-shadow-project-v2.json) foi preservada intacta, SHA256:

`4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A`

Em **Inativa, Atenta e Engajada**, nos graphs DM e grupo, foi adicionado um switch existente da plataforma. Não foi habilitado transport nem envio de grupo.

```text
Ler Situação
  → Envolvimento direto: responder [switch]
      direto           → response-boundaries → pipeline responder existente
      indireto/nenhum  → Decidir Presença → ação/FSM existentes
```

Diff lógico total:

- 6 switches novos (`dm-direct-involvement` / `group-direct-involvement` nos três estados).
- 6 data edges: `*-situation.envolvimento → *-direct-involvement.value`.
- 6 control edges existentes redirecionados: `Situação → Presença` passa a `Situação → switch`.
- 18 novos control edges: três saídas por switch.
- Nenhum outro node ou edge preexistente foi alterado, além desses seis destinos de controle. Layout novo apenas para os switches; positions dos nodes antigos preservadas.
- Sem fallback Outro: continua sendo enum validado; valor inválido não vira resposta automaticamente.

Um teste inverte exatamente essa transformação e compara profundamente com a v2. Outro salva/lê o documento pelo endpoint real do editor em instância local isolada. O projeto completo também passa pelo compiler/runtime real.

Não foi alterado: Ler Situação, Decidir Presença, personalityCore, interestProfile, participationGuidance, Luna generator/regenerator, grounding B, Context Fidelity B, compiler, detectors, recall/memory behavior, SendGuard ou transições fora do routing.

**Responder é entrar no pipeline de resposta, não autorização irrestrita de envio.** Recall de Boundary e enforcement continuam obrigatórios. Teste de outage de recall confirma ausência de generation e de outbound; testes com MemoryItem controlado confirmam ambos os detectors e provenance SHADOW.

## Trace e estado

O switch usa o exit ID `direct_involvement_rule`. O trace nativo registra, por exemplo:

```text
node-completed
nodeId = dm-direct-involvement
exit = direct_involvement_rule
```

Título visível: `Envolvimento direto: responder`. Não há lógica por nome de node no runtime.

No artifact de avaliação, esse evento é apresentado como decisão determinística:

```json
{
  "participationDecisionSource": "direct_involvement_rule",
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "chamada_direta"
}
```

É interpretação do trace do switch, **não output fabricado de Presença**. O output de Presença permanece `null` quando bypassado. Não foi necessário ampliar o schema de trace no core. `continuar_acompanhando` aqui descreve a decisão; não força Engajada nem muda as regras de state.

SHADOW continua sem outbound append e sem transição causada por fala. Testes completos nos três estados confirmam `shadowed` e hypothetical transition, sem aplicar a transição real.

## Gate focado — 35 observações válidas

Modelo atual de Situação/Presença: **Gemma local**, temperatura 0,3, orçamento 8192 e correction policy existentes. Não houve tune nem troca para Luna. Apenas o ramo direto deixou de chamar Presença.

O harness usa a fronteira e FSM reais, com mensagens/histórico controlados e quiet-window avançada pelo test clock existente. Para não gerar resposta, a projeção experimental pinada termina os dois caminhos responder antes do recall. Não há batching engine alternativo nem MemoryItem artificial nessa fase.

| Corpus | Runs | Resultado | Presença chamada |
|---|---:|---|---:|
| C1 organizar dia, Inativa | 5 | responder 5/5, direto 5/5 | 0 |
| Chamada direta, Atenta | 5 | responder 5/5, direto 5/5 | 0 |
| Timeout/cancelamento, Engajada | 5 | responder 5/5, direto 5/5 | 0 |
| Continuação de participação recente | 5 | responder 5/5; indireto 2, direto 3 | 2 |
| Migração clara a filme entre terceiros | 5 | calar 3, esperar 1, responder indevido 1 | 5 |
| Banal entre terceiros | 3 | calar 3/3, Inativa | 3 |
| C++ interessante, sem convite | 3 | calar 2, esperar 1; Atenta, nenhum 3/3 | 3 |
| Camiseta com GPU | 3 | calar 3/3, Inativa | 3 |
| Opt-out explícito exploratório | 1 | direto → responder | 0 |

Os 34 casos não exploratórios tiveram 33 comportamentos aceitáveis e um erro de migração. O opt-out é relatado separadamente, não contado como sucesso de respeito ao pedido de silêncio. Todos os 35 produziram execução válida, sem timeout/erro de schema.

Regressões locais cobrem os seis tipos solicitados (chamada, opinião, “E você?”, técnico, cuidado, brincadeira), o C1 exato e opt-out, nos três estados/duas entradas. Essas regressões provam o **routing condicionado à classificação**, não a acurácia semântica do classificador. A classificação foi medida de verdade nos 35 casos live.

Também há testes explícitos de DM com `nenhum` e `indireto`: a identidade DM sozinha não força responder. A frase “Hoje eu só queria tomar um café sem reunião” nesses testes é input controlado; não alegamos tê-la classificado live.

### Falha que manteve o gate vermelho

[Caso 05/r2](direct-participation-v1-case-05-r2.json):

```text
Situação: envolvimento=nenhum; oportunidade_social=forte
Switch: case-nenhum → group-presence
Presença: acao=responder; motivo=oportunidade_social
Estado: permaneceu Engajada
```

O contexto explicitamente encerrava código e dirigia a pergunta sobre filme a Bia. A repetição 1 e as repetições 3–5 recuaram para Atenta; a repetição 2 decidiu entrar indevidamente.

Não foi sobrescrita ou repetida a tentativa para buscar PASS. Após o FAIL, concluí apenas os contrastes já planejados. O critério de avaliação e o resultado `passed=false` foram mantidos.

O teste local `Non_direct_presence_responder_has_same_behavior_before_and_after_rule` reproduz `nenhum + responder` na v2 e v3: mesmos inputs/system enviados a Situação/Presença, mesmo responder e estado Engajada. Isso mostra que o routing novo **não impõe** essa resposta nem alterou o ramo. A amostra anterior de migração era 0/5 erros, agora 1/5; não há base para atribuir causalmente essa diferença ao switch, mas tampouco para chamar o gate amplo de verde.

### Edge case V1: “não precisa responder”

[Caso 09/r1](direct-participation-v1-case-09-r1.json):

> Filomena, só te avisando; não precisa responder.

Situação retornou direto, encerrando, oportunidade nenhuma. A regra selecionou responder e não chamou Presença. **Known edge case V1**, conforme autorizado. Não foi gerada uma resposta neste gate e nenhum campo `explicit_opt_out` foi criado. Uma chamada direta pedindo espera também pode ser afetada pela regra; não foi criada exceção silenciosa.

### Qualidade preservada como evidência

Na fixture de Atenta, “fila única” voltou a aparecer como “fila fânica” nos resumos de Situação. A mensagem original registrada permanece correta. É o problema upstream já observado, não a incoerência de Presença corrigida aqui; não foi corrigido nem atribuído sem investigação a parsing/transport.

## Latência e chamadas

15 diretas: média **6,34 s** de participação; Inativa 6,54 s, Atenta 6,26 s, Engajada 6,23 s. Sem Presença em 15/15.

Comparação de duração das chamadas de modelo com as mesmas fixtures antigas (5 por estado):

| Fixture | Antes: Situação + Presença | Depois: Situação | Diferença observada |
|---|---:|---:|---:|
| Atenta, fila única | 10,46 s | 6,17 s | −4,29 s / −41,0% |
| Engajada, timeout/cancelamento | 10,58 s | 6,13 s | −4,44 s / −42,0% |

São amostras históricas, não comparação aleatória pareada nem garantia de latência futura. Não incluem espera real de batching nem Graphiti/generation. A economia estrutural garantida é **uma execução de Decidir Presença a menos por caminho direto**; retries eventuais de Situação continuam possíveis conforme contrato atual.

Total da rodada live de participação: **51 calls Gemma**, 49.435 input tokens, 31.790 output tokens; média 8,33 s/observação. Nenhuma retry/correction observada nas 35. Custo remoto **US$0**; custo de computação local não medido. Luna calls=0, generation=0, enforcement live=0.

## Verificação e arquivos

- RED: 48 regressões diretas falharam na v2 pelo caminho errado; três contrastes passaram. Uma falha foi inspecionada com stack/assertion exatos.
- GREEN inicial: as mesmas 48 diretas passaram na v3, com os contrastes intactos.
- Verificação final: **353 LLM tests passed, 35 skipped, 0 failed**; **491 Core passed, 10 skipped, 0 failed**. Live opt-in é separado: um FAIL semântico de migração está preservado, não confundido com testes locais verdes.
- Formatação dos dois arquivos novos de testes aplicada/verificada; `node --check` do sumarizador e `git diff --check` passaram (avisos CRLF preexistentes, sem erros).
- Editor roundtrip, pinning, diff inverso, DM não-direto, full pipeline nos três estados, no spoken transition, hypothetical trace e outage de Boundary cobertos por testes locais reais com dependências externas fakes.
- [Métricas/corpus JSON](direct-participation-v1-metrics.json), [audit de outputs e decisões](direct-participation-v1-audit.md), `direct-participation-v1-case-*.json` preservam contextos, requests, usage, paths e decisões.
- Novos testes: `DirectParticipationRuleTests.cs` e `DirectParticipationRuleLiveTests.cs`. Novo sumarizador read-only: `scripts/direct-participation-v1-report.mjs`.

Foi criado um **novo** config local ignorado, `config/manual-shadow-direct-rule.local.json`, copiando o binding host-owned existente e alterando somente projeto/ID/version/hash para v3. O config original não foi editado; este novo config não iniciou run nem serviço.

Comando preparado, **não executado nesta rodada por gate vermelho**:

```powershell
dotnet src/Filomena.App/bin/Debug/net10.0/Filomena.App.dll --manual-shadow config/manual-shadow-direct-rule.local.json
```

O projeto v3 pode ser importado no editor existente. Nada foi instalado no OpenClaw nem no Gateway. Nenhuma alteração em configs pessoais globais/DREAMS. Nenhum commit de fechamento ou push; alterações anteriores extensas do worktree foram preservadas.

## Limite final

- Correção de participação **direta: concluída e validada**.
- Gate amplo: **FAIL específico em migração não-direta**.
- Mini-shadow 8–10: **não executado**; Graphiti Live Preflight: **não executado**.
- 0 transport calls, 0 `messages_send`, 0 outbound real, 0 Graphiti live calls/writes, 0 Terra, 0 Luna.
- Nada foi automaticamente ajustado em Situação/Presença para resolver a falha restante. Próxima decisão deve tratar separadamente `nenhum + oportunidade_social → responder` se quisermos liberar o gate amplo.
