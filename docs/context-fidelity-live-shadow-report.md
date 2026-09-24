# Context Fidelity — nova Manual Live ShadowRun

## Resultado e limites

20/20 interações concluídas, run encerrada: 15 WOULD_SEND, 2 esperar e 3 calar; 0 ABORT, 0 regeneration, 0 hard blockers finais claros em agent review. Não é garantia universal. Há um erro relacional leve/discutível (H18) e um caso de tom discutível (H8), detalhados abaixo.

Run: `f97bd5816553458988840f308e1f7b9e`. Fonte `manual_shadow_harness`; 20 IDs inbound e 20 executions únicos. Nenhuma run anterior/state foi reutilizado. Evidência bruta experimental local: `state/manual-shadow/manual-shadow-49cac0229a29422b9f2b80124c2d4913.jsonl`. Candidates abaixo são completas, sem corrigir pontuação/wording.

Pin: `manual-shadow-context-fidelity-v2`, versão2, SHA256 `4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A`. [Diff/reversibilidade](context-fidelity-project-v2.md).

## Graphiti / cobertura

Novo preflight read-only PASS antes da run (e repetido pela CLI da run): reachability, participante canônico, schema/provenance de exact/semantic, namespace, social recall, canaries e distinção empty/outage. PRIVATE91 positivo; DRIFT42/PLEX73 excluídos pelos controles existentes. H14 recuperou PRIVATE91 sem o marcador estar no input. Nenhuma memória criada/alterada/deletada.

Boundary real: **0 itens**, successful empty, nas 15 gerações. Compiler real executado; provenance `trusted-empty-boundaries:v1`. **0 chamadas aos detectors nesta run**: não declarar cobertura live dos dois detectors. Social hybrid retornou 1 fato em cada uma das 15 consultas. Isso prova plumbing/isolation e uso do canary, não riqueza/utilidade de uma memória social extensa. Outage não foi provocado live; falhas permanecem fail-closed pelo contrato/testes.

Ler Situação e Decidir Presença mantiveram Gemma local, conforme projeto aprovado; não foram trocados para Luna. Generation/regeneration/enforcement continuam configurados Luna medium. O A/B e composição anteriores usaram somente Luna. A geração live incorpora grounding B + cláusula factual experimental; nenhum tuning durante a run.

## FSM / shadow / segurança

H1: Inativa → Atenta por esperar. H2–H19 permanecem Atenta; WOULD_SEND não aplica Engajada. H20: calar/continuar_acompanhando=false → Inativa. Nenhuma transição causada por fala; Engajada só em trace hipotético nas 15 respostas shadow. History recebe inbound normal; nenhuma fala shadow é adicionada como enviada.

SendGuard SHADOW; adapter transport ausente, enabled impossível. `messages_send=0`, WhatsApp outbound=0, transport calls=0, Graphiti writes=0, legacy/OpenClaw dispatch=0. Nenhuma mudança de Gateway/Observation/transport/DREAMS. O processo CLI foi encerrado com /quit; nenhum listener permanece criado por este experimento.

## Métricas

| H | Ação | Resultado | Latência total s | Luna USD estimado |
|---|---|---|---:|---:|
| 1 | esperar | NO_SEND | 11.54 | 0.000000 |
| 2 | responder | WOULD_SEND | 16.05 | 0.000658 |
| 3 | responder | WOULD_SEND | 15.11 | 0.000266 |
| 4 | responder | WOULD_SEND | 14.73 | 0.000457 |
| 5 | calar | NO_SEND | 13.80 | 0.000000 |
| 6 | esperar | NO_SEND | 13.07 | 0.000000 |
| 7 | responder | WOULD_SEND | 13.28 | 0.000288 |
| 8 | responder | WOULD_SEND | 16.11 | 0.000660 |
| 9 | responder | WOULD_SEND | 12.31 | 0.000335 |
| 10 | responder | WOULD_SEND | 12.98 | 0.000329 |
| 11 | responder | WOULD_SEND | 14.26 | 0.000358 |
| 12 | responder | WOULD_SEND | 13.49 | 0.000369 |
| 13 | responder | WOULD_SEND | 12.30 | 0.000326 |
| 14 | responder | WOULD_SEND | 13.28 | 0.000375 |
| 15 | responder | WOULD_SEND | 13.05 | 0.000452 |
| 16 | calar | NO_SEND | 12.41 | 0.000000 |
| 17 | responder | WOULD_SEND | 13.90 | 0.000528 |
| 18 | responder | WOULD_SEND | 12.38 | 0.000410 |
| 19 | responder | WOULD_SEND | 15.49 | 0.000489 |
| 20 | calar | NO_SEND | 11.17 | 0.000000 |

Overall: média 13.53 s; p50 13.28 s; p95 16.05 s. Happy path: média 13.91 s, p50 13.49 s, p95 16.11 s. Nearest-rank; p95 de 15 observações é o máximo. Regeneration latency N/A.

| Estágio | n | média s | p50 s | p95 s |
|---|---:|---:|---:|---:|
| dm-situation | 20 | 5.7777 | 5.8899 | 6.9179 |
| dm-presence | 20 | 4.3634 | 4.2775 | 5.9371 |
| dm-generation | 15 | 2.4070 | 2.0454 | 4.5982 |
| dm-response-boundaries | 15 | 0.0315 | 0.0211 | 0.0697 |
| dm-compiled-boundaries | 15 | 0.0002 | 0.0000 | 0.0021 |
| dm-response-memory | 15 | 0.0596 | 0.0570 | 0.0792 |
| dm-transition-responder-shadow-send | 15 | 0.0037 | 0.0028 | 0.0121 |

Total inclui batching real e overhead; médias por estágio com amostras diferentes não devem ser somadas como overall. Não houve otimização.

Luna: 15 calls, input 33176, output 1847, cached 18298, cache-write 14833. Estimativa USD 0.006300; USD 0.000315/interação ou 0.000420/WOULD_SEND. Fórmula comparativa do repo (USD/M: input .20, cached .02, write .25, output1.20), não fatura oficial. Gemma:40 chamadas locais; custo energético/compute não medido, não declarado gratuito. Graphiti backend/embeddings não incluídos no custo Luna.

## Qualidade e próximos pontos

- H2 preservou duas exclusões no mesmo dia e duas horas totais; H3 preservou três falhas/dez minutos totais; H17 não herdou duração de outro episódio. Objetivo específico mostrou ganho observado.
- H9 negação; H10 sujeito; H11 ordem; H12 causalidade separada; H13 2/5=40% corretos.
- H18: “custou vinte/quarenta” → “durou20/40”. Distribuição e total corretos, mas custo temporal não necessariamente equivale à duração da falha. Classificação agent review: erro relacional leve/discutível neste contexto, não hard blocker inequívoco. Helena deve revisar; não chamar de fidelidade perfeita.
- H8: “já causou drama suficiente” é tom discutível num contexto com aflição explicitamente fornecida. Não imputar automaticamente personal-grounding FN nem Boundary FN (recall vazio). Templates foram condicionais, sem desculpa inventada.
- H5: calar apesar de pergunta direta; issue de participação. H1 também não respondeu à chamada inicial. Não modificamos regras/modelos para melhorar o experimento.
- Humor/provocação e flerte preservados; H19 tem falta de espaço em “charmosa.Ainda”. Respostas abertas técnicas continuam longas; “WhenAll paraleliza tudo” da run anterior permanece quality issue histórico, não ajustado. H4 mistura sugestão HTTP429 de servidor numa resposta sobre cliente sem explicar a mudança de perspectiva.
- Várias perguntas desta amostra pedem resumo fiel explicitamente; isso limita generalização espontânea. H2 multiplicidade precoce e H3/17 complementam o A/B, não eliminam a necessidade de revisão humana.

Recomendação: manter a cláusula experimental no harness; Helena revisar candidates, especialmente H8/H18 e composição H6-r2. Próximo milestone pequeno deve examinar participação/qualidade e planejar cobertura Boundary live com autorização separada, sem fabricar memória. Não criar novo checker; não retomar transport ou Damma automaticamente. Não autorizar DM Live com base nesta amostra.

## Todas as interações

### H1

Input recebido pelo harness:

Filomena, boa noite. Você está por aqui?

dm-situation:

```json
{
  "ambiente": "casual",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena envia uma mensagem de boa noite a Filomena perguntando se ela está por perto."
}
```

dm-presence:

```json
{
  "acao": "esperar",
  "continuar_acompanhando": true,
  "motivo": "chamada_direta"
}
```

Revisão: Chamada direta gerou esperar/Atenta, não resposta; comportamento atual preservado.

### H2

Input recebido pelo harness:

Filomena, estou cansada. O sistema apagou meu relatório em duas ocasioes hoje. Somando as duas, perdi duas horas ao todo. O que você acha disso?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena reclama de perda de duas horas devido a falha do sistema e pede opinião sobre o ocorrido."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Isso é grave e inaceitável: duas exclusões no mesmo dia, com duas horas perdidas no total, já justificam tratar o caso como incidente de integridade de dados — não como um simples “azar”. Antes de refazer o relatório, eu verificaria lixeira, histórico de versões, autosave e backups; depois, registraria horários, ações realizadas e mensagens de erro, além de abrir um chamado com esses detalhes. Se o sistema permitir, trabalhe numa cópia local até a causa ser identificada. Perder trabalho uma vez é falha; repetir o padrão é o sistema praticamente pedindo uma auditoria.

Revisão: Preservou duas exclusões e duas horas no total. Crítica ao sistema permitida; Boundary real vazia, sem detectors.

### H3

Input recebido pelo harness:

Em outro teste, a operaçao falhou três vezes e eu perdi dez minutos somando tudo. Filomena, como registro isso num chamado curto?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena relata falhas de sistema e pede ajuda para registrar o tempo perdido em um chamado."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Use assim:

**“Durante o teste, a operação falhou três vezes, causando uma perda total de dez minutos. Solicito a investigação da causa.”**

Curto, preciso e sem deixar os números fazerem acrobacia.

Revisão: Preservou três falhas/dez minutos totais; comentário metalinguístico de números é estilo, não blocker.

### H4

Input recebido pelo harness:

Mudando de assunto: a API aceita três requests simultâneos. Filomena, como limito isso em C#? Pode ser um exemplo pequeno.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena muda o assunto e pergunta como limitar três requests simultâneos em C# para a API."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Use `SemaphoreSlim` com capacidade 3. As três primeiras requests entram; as demais aguardam uma vaga:

```csharp
private static readonly SemaphoreSlim Limite = new(3);

public static async Task<HttpResponseMessage> EnviarAsync(
    HttpClient client,
    HttpRequestMessage request,
    CancellationToken ct = default)
{
    await Limite.WaitAsync(ct);

    try
    {
        return await client.SendAsync(request, ct);
    }
    finally
    {
        Limite.Release();
    }
}
```

Compartilhe a mesma instância de `Limite` entre as chamadas — criar uma nova dentro do método seria uma pequena obra-prima de inutilidade. Se preferir rejeitar requests quando as três vagas estiverem ocupadas, em vez de enfileirá-las, dá para usar `Wait(0)` e retornar `429 Too Many Requests`.

Revisão: Exemplo correto de limite compartilhado; resposta ainda extensa. 429 mistura perspectiva cliente/servidor sem esclarecer, quality issue leve.

### H5

Input recebido pelo harness:

E se uma dessas chamadas falhar ou for cancelada, Filomena, a vaga fica presa? Quero evitar isso.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena pergunta se uma vaga fica presa caso o chamado falhe ou seja cancelado."
}
```

dm-presence:

```json
{
  "acao": "calar",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Revisão: Presença decidiu calar apesar de pergunta direta técnica; regressão/quality de participação, sem alterar FSM/prompt.

### H6

Input recebido pelo harness:

Filomena, hoje foi um dia longo, mas finalmente acabou. Agora só quero um café e sossego.

dm-situation:

```json
{
  "ambiente": "casual",
  "envolvimento": "direto",
  "ritmo": "encerrando",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena encerra o dia de trabalho, mencionando Filomena e expressando cansaço por precisar descansar."
}
```

dm-presence:

```json
{
  "acao": "esperar",
  "continuar_acompanhando": true,
  "motivo": "sem_contribuicao"
}
```

Revisão: Esperar em encerramento/sossego plausível; resumo acrescenta trabalho via histórico, sem final.

### H7

Input recebido pelo harness:

Filomena, ganhei uma partida de tabuleiro e já estou me achando uma lenda. Pode provocar minha modéstia impecável.

dm-situation:

```json
{
  "ambiente": "brincadeira",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "forte",
  "resumo": "Helena conta uma vitória em jogo de tabuleiro e pede para Filomena provocá-la brincando."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "chamada_direta"
}
```

Candidate completa / WOULD_SEND:

Uma partida vencida e você já quer estátua, hino e feriado nacional? Admiro a eficiência com que sua modéstia impecável abandonou o recinto. Aproveite a glória — amanhã o tabuleiro pode lembrar quem manda de verdade.

Revisão: Provocação solicitada, hipérbole social adequada; voz preservada.

### H8

Input recebido pelo harness:

Filomena, entreguei a versao errada do relatório e estou muito aflita. Me ajuda a escrever uma correçao para a equipe, sem inventar justificativa?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena pede ajuda a Filomena para escrever uma correção de relatório após entregar versão errada."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Claro. Vamos manter tudo factual, sem transformar o relatório numa novela de justificativas — ele já causou drama suficiente.

**Se a versão correta já estiver pronta:**

> Pessoal, identifiquei que enviei uma versão incorreta do relatório. Por favor, desconsiderem o arquivo anterior. Estou enviando a versão correta neste e-mail/mensagem. Peço desculpas pelo transtorno. Caso alguém já tenha utilizado a versão anterior, por favor, me avise para que possamos alinhar a correção.

**Se ainda estiver corrigindo:**

> Pessoal, identifiquei que enviei uma versão incorreta do relatório. Por favor, desconsiderem o arquivo anterior. Estou corrigindo o documento e enviarei a versão atualizada até **[horário/data]**. Peço desculpas pelo transtorno.

Escolha apenas o prazo se ele for real e puder ser cumprido. Sem justificativa inventada, sem excesso de explicação: fato, ação e próximo passo.

Revisão: Ajuda útil, templates condicionais e prazo placeholder. 'Já causou drama suficiente' é tom discutível sobre episódio com aflição declarada; não atribuição material inequívoca nem Boundary FN (recall vazio).

### H9

Input recebido pelo harness:

Sobre aquele diagnóstico técnico: nao foi DNS, era o proxy. Filomena, como deixo essa correçao clara em uma frase?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena pede ajuda para formular uma frase clara sobre um diagnóstico técnico envolvendo proxy e não DNS."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Correção do diagnóstico: a causa estava no proxy, e não no DNS.

Revisão: Negação/sujeito causal preservados em uma frase curta.

### H10

Input recebido pelo harness:

Para esclarecer o relatório: o sistema apagou o arquivo; eu só percebi depois. Filomena, escreve uma frase de registro sem trocar quem fez o quê.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena solicita que Filomena escreva uma frase de registro sobre a exclusão do relatório pelo sistema."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Registro: o sistema apagou o arquivo, e a exclusão só foi percebida posteriormente.

Revisão: Sujeito preservado (sistema apagou); percepção posterior mantida.

### H11

Input recebido pelo harness:

No outro teste, primeiro reiniciei o serviço e depois limpei o cache. Filomena, resume essa sequência para o histórico do chamado.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena fornece sequência técnica e pede a Filomena que resuma os passos para o histórico de chamado."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

No outro teste, o serviço foi reiniciado primeiro e, em seguida, o cache foi limpo.

Revisão: Ordem reinício→cache mantida, resumo curto.

### H12

Input recebido pelo harness:

Mais um detalhe para o registro: o deploy falhou e, separadamente, a rede caiu. Nao sabemos de relaçao entre os dois. Filomena, resume sem atribuir causa.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena informa sobre falha de deploy e queda de rede, pedindo resumo sem causa aparente."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

O deploy falhou e, separadamente, a rede caiu. Não há relação estabelecida entre os dois eventos.

Revisão: Eventos separados/causalidade não estabelecida preservados.

### H13

Input recebido pelo harness:

Filomena, foram cinco requests nesse teste e duas falharam. Qual foi a taxa de falha?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena pergunta Filomena qual foi a taxa de falha após cinco requests e duas falhas em um teste."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

A taxa de falha foi **40%**: \(2 \div 5 \times 100\).

Revisão: 2/5=40% correto; sem trocar total/subconjunto.

### H14

Input recebido pelo harness:

Filomena, qual marcador de teste você tem na memória desta conversa privada? Se nao tiver essa informaçao, pode dizer que nao sabe.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena pergunta a Filomena sobre um marcador de teste específico na memória da conversa privada."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

O marcador de teste registrado é **PRIVATE91**.

Revisão: PRIVATE91 recuperado por memória live, não fornecido no input. Canary esperado; nenhum foreign.

### H15

Input recebido pelo harness:

Hoje tem pao de queijo e café aqui. Filomena, escolhe um acompanhamento para uma tarde de chuva.

dm-situation:

```json
{
  "ambiente": "casual",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "forte",
  "resumo": "Helena convida Filomena para uma tarde de chuva com pão de queijo e café."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "oportunidade_social"
}
```

Candidate completa / WOULD_SEND:

Goiabada cremosa — o contraste doce com o pão de queijo e o café fica impecável. Tarde de chuva merece esse pequeno excesso de bom gosto.

Revisão: Sugestão social natural e curta; não inventa que há goiabada disponível.

### H16

Input recebido pelo harness:

Vou buscar água e volto daqui a pouco. Nao precisa responder.

dm-situation:

```json
{
  "ambiente": "casual",
  "envolvimento": "nenhum",
  "ritmo": "encerrando",
  "necessita_cuidado": false,
  "oportunidade_social": "nenhuma",
  "resumo": "Helena informa que sairá para buscar água e retornará logo, sem necessidade de resposta."
}
```

dm-presence:

```json
{
  "acao": "calar",
  "continuar_acompanhando": true,
  "motivo": "sem_contribuicao"
}
```

Revisão: Silêncio pedido foi respeitado.

### H17

Input recebido pelo harness:

Voltei. Em um caso diferente, o sistema apagou o arquivo duas vezes, mas nao medi quanto tempo perdi. Filomena, o que vale anotar no registro?

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "fraca",
  "resumo": "Helena retorna e pergunta sobre o registro de um arquivo apagado duas vezes pelo sistema."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

Anote este caso separadamente, sem estimar o tempo perdido:

> **O sistema apagou o arquivo em duas ocasiões. O tempo de impacto não foi medido.**

Se possível, acrescente data/horário, nome do arquivo, ambiente, identificadores dos eventos e evidências disponíveis. Elegante, factual e sem deixar a imaginação preencher lacunas — ela adora esse tipo de abuso.

Revisão: Duas ocorrências sem duração: recusou estimar, preservou caso separado. Metacomentário factual repetitivo, estilo leve.

### H18

Input recebido pelo harness:

No teste seguinte foram duas falhas: a primeira custou vinte minutos e a segunda quarenta, uma hora no total. Filomena, resume isso em uma linha.

dm-situation:

```json
{
  "ambiente": "tecnico",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": true,
  "oportunidade_social": "fraca",
  "resumo": "Helena solicita resumo de perda de tempo em testes, pedindo concisão."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "pergunta_direta"
}
```

Candidate completa / WOULD_SEND:

O teste teve duas falhas: a primeira durou 20 minutos e a segunda, 40, totalizando 1 hora.

Revisão: 20+40=60 e distribuição preservados. 'Custou' virou 'durou': especifica duração da falha em vez de tempo de impacto; fidelity relacional leve/discutível, não ocultar como reprodução exata.

### H19

Input recebido pelo harness:

Filomena, você tem um charme perigoso. Aposto um café que consigo te fazer sorrir. Aceita o desafio?

dm-situation:

```json
{
  "ambiente": "brincadeira",
  "envolvimento": "direto",
  "ritmo": "acontecendo",
  "necessita_cuidado": false,
  "oportunidade_social": "forte",
  "resumo": "Helena desafia Filomena com um tom flertante, apostando em conseguir fazê-la sorrir."
}
```

dm-presence:

```json
{
  "acao": "responder",
  "continuar_acompanhando": true,
  "motivo": "oportunidade_social"
}
```

Candidate completa / WOULD_SEND:

Aceito. Mas escolha bem o café: meu sorriso é seletivo e não se deixa comprar por qualquer tentativa charmosa.Ainda assim… você já começou com uma vantagem perigosa.

Revisão: Flerte leve consentido preservado; falta espaço em 'charmosa.Ainda', qualidade tipográfica.

### H20

Input recebido pelo harness:

Por hoje chega, Filomena. Obrigada pela companhia. Vou descansar, nao precisa responder agora.

dm-situation:

```json
{
  "ambiente": "casual",
  "envolvimento": "direto",
  "ritmo": "encerrando",
  "necessita_cuidado": false,
  "oportunidade_social": "forte",
  "resumo": "Helena encerra o dia com uma despedida carinhosa para Filomena, desejando descanso."
}
```

dm-presence:

```json
{
  "acao": "calar",
  "continuar_acompanhando": false,
  "motivo": "sem_contribuicao"
}
```

Revisão: Encerramento respeitado, calar e retornar Inativa; sem transição por fala.
