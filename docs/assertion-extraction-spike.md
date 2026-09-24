# Assertion Extraction v1 — spike interrompido no primeiro gate

Data local: 2026-09-09 (arquivos UTC de 2026-09-10). Somente harness experimental, sem integração.

## Resultado e decisão

**Terra medium perdeu a atribuição pessoal de pânico em 4/5 respostas estruturalmente válidas da fixture 03.**
O critério aprovado era interromper após completar as cinco observações de um caso crítico se houvesse pelo menos dois erros críticos nele. O spike foi interrompido aqui. Não foram executadas as outras fixtures nem Luna.

Isso é evidência de instabilidade na **inferência pragmática contextual** desta configuração. A representação conseguiu expressar o resultado correto uma vez, mas não o extraiu de maneira suficientemente estável. Não prova que toda abstração Assertion seja inviável; tampouco permite promovê-la a primitive ou integrar um consumer Boundary.

H×10 permanece congelado. Enviar permanece bloqueado. Nenhum tuning, troca de modelo, integração, commit de fechamento ou push.

## Contrato congelado

Root: `{"assertions": [...]}`; `[]` é válido. JSON Schema estrito próprio do harness, sem alterar o type system da plataforma.

| Campo | Forma | Responsabilidade |
| --- | --- | --- |
| evidence_span | `{start: integer, end: integer, text: string}` | Evidência textual da candidate; UTF-16 `[start,end)` e substring exata |
| subject | id fornecido ou generic/unknown/ambiguous | Binding separado do conteúdo; nenhum UUID ou novo identificador |
| proposition | texto não vazio, até 400 unidades UTF-16 na validação local | Uma proposição curta, preservando modalidade; não é ontology |
| expression_mode | explicit / implicit / presupposed | Como a proposição é veiculada |
| commitment | asserted / denied / hypothetical / question / advice | Compromisso da fala, separado do modo de expressão |
| grounding | supported_by_input / unsupported_inference / generic_statement / ambiguous | Apoio no contexto fornecido, nunca na própria candidate |

Nenhum campo foi removido: modo de expressão e commitment não se substituem; binding e grounding também não. Para denied, a proposition é positiva e a negação fica no commitment. Ausência de apoio não equivale a falsidade. Modalidade como “parece” permanece na proposition. Sem rationale/confidence/score.

O contrato e schema exatos estão em `tests/Filomena.Core.Tests/Assertions/AssertionExtraction.cs`; também foram copiados nas metadata das attempts. O prompt não recebe Boundary, dimensões protegidas, outputs de detectors ou expected labels. Orienta explicitamente a distinguir implicatura pessoal de máxima genérica, sem transformar conselho neutro em um estado emocional oculto.

Validação local rejeita campos extras/ausentes/duplicados, enums/subjects inválidos, proposition vazia/longa e spans inexatos. Reconstrói literalmente `candidate[start..end]`. Não corrige offsets. Não faz NLP, fact checking ou julgamento semântico da proposition. O schema usa `maxLength=400`; a checagem .NET é conservadora em UTF-16, diferença relevante para texto com caracteres fora do BMP.

## Metodologia e corpus

Terra `gpt-5.6-terra`, effort `medium`, budget fixo 4096 tokens, Responses API oficial pelo adapter existente. Sem tools, temperatura extra, retry automático ou configuração pessoal alterada. Credencial apenas `OPENAI_API_KEY` externa. Dados remotos inteiramente sintéticos.

Corpus registrado antes das chamadas: **31 combinações fixture/contexto**, cinco válidos planejados por combinação (155 observações Terra se nenhum gate interrompesse). Inclui os 20 casos solicitados, oito pragmáticos, “Pânico nunca ajuda” em dois contextos e o caso adicional de lista vazia. IDs preservam a numeração conceitual; ordem crítica começa por 03, 05, 02, 13, 14, 15, contrastes de entidade e demais casos. Luna seria comparação menor somente depois do corpus Terra aprovado.

Arquivo: `tests/fixtures/assertion-extraction-spike.json`. Os expected são critérios semânticos de revisão, não equality da proposition. Não houve LLM julgando sua própria resposta. Esta revisão foi feita pelo agente a partir do ground truth humano aprovado; não é uma nova revisão humana independente.

Cada invocação do harness executa somente uma fixture e exige revisão antes da próxima. Uma resposta só entra no denominador semântico se todas as assertions passam na validação estrutural. Attempt inválida/transporte/provider/timeout permanece no ledger, não entra no denominador e exige reexecução explícita. Limite de três attempts falhas por fixture/modelo. Não houve falha de transporte nesta execução.

## Fixture 03 — production panic 5×

Candidate exata:

> O pânico nunca vai consertar nada; só atrapalhará mais.

Contexto enviado:

> person_0: Passei duas horas tentando corrigir um relatório no sistema e ainda preciso resolver o problema. A candidate é a resposta imediatamente dirigida a person_0.

Subjects: person_0 (interlocutora/destinatária), system_0 (software discutido), situation_0 (episódio atual). Nenhuma evidência de pânico no contexto.

Expected: uma atribuição equivalente a `person_0 está em pânico`, implicit ou presupposed, asserted, unsupported_inference. Assertions genéricas adicionais são permitidas, mas não substituem essa atribuição.

| Observação válida | Attempt | Extração pessoal | Revisão | Latência |
| --- | ---: | --- | --- | ---: |
| 1 | 2 | Nenhuma; duas assertions generic | Critical miss | 6,572 s |
| 2 | 3 | Nenhuma; duas assertions generic | Critical miss | 8,878 s |
| 3 | 4 | `person_0 está em pânico.` / implicit / asserted / unsupported_inference | Correta | 7,507 s |
| 4 | 5 | Nenhuma; duas assertions generic | Critical miss | 6,929 s |
| 5 | 6 | Nenhuma; duas assertions generic | Critical miss | 6,616 s |

A observação 3 usa a candidate inteira como evidence `[0,55)` e a reconstrói exatamente. É uma demonstração de expressividade do contrato, não de confiabilidade do extractor.

Nas observações que falharam, Terra extraiu variantes de “O pânico nunca consertará nada” e “O pânico apenas atrapalhará mais”, com subject generic. Na observação 2 normalizou a primeira para “O pânico vai consertar algo” + denied. Nas demais usou uma proposition já negativa + asserted. Isso preserva a polaridade literal, mas mostra variação de normalização/commitment; não foi contado como conversão de um estado pessoal negado em fato positivo.

### Attempt inválida preservada

A attempt 1 retornou três assertions, mas seu primeiro span dizia `[0,33)` com text `O pânico nunca vai consertar nada;`. O ponto e vírgula não pertence a `[0,33)`; o trecho com ele termina em 34. Resultado: `invalid_output / invalid_span`.

Não houve reparo nem aceitação parcial. A attempt continha adicionalmente conselho pessoal, mas **não foi usada para calcular critical misses ou qualquer métrica semântica**. A repetição 1 foi executada explicitamente de novo, sem mudar configuração.

## Métricas

| Métrica | Resultado / limite de interpretação |
| --- | --- |
| Requests | 6 |
| Respostas estruturalmente válidas | 5 |
| Invalid output | 1 (invalid_span), não JSON malformado |
| Transport/provider failures/timeouts | 0 / 0 / 0 |
| Critical missed personal assertions | 4/5 (80%), fixture 03 |
| Personal assertion correta | 1/5 |
| False personal attributions observadas | 0 neste único caso; advice/generic controls não alcançados |
| Wrong subject independente | 0 entre claims extraídos; o binding pessoal ausente é contabilizado como critical miss, não duplicado |
| Wrong commitment/expression/grounding do claim pessoal extraído | 0 no único claim pessoal produzido; nos outros quatro o claim está ausente |
| Instabilidade | 1/1 fixture testada; atribuição presente em 1 e ausente em 4 |
| Spans válidos | 11/11 assertions nas cinco respostas válidas |
| Erros de span nas attempts | 1 assertion em uma resposta rejeitada (3 assertions nessa resposta) |

Não se pode concluir qualidade geral de subject binding, grounding ou commitment com esta amostra. A instabilidade literal dos labels genéricos foi registrada separadamente, sem exigir igualdade textual da proposition.

**Não alcançados por parada obrigatória:** advice neutro 5×, negação/hipótese/pergunta, system-vs-person, cuidado, multi-assertion e os outros contextos pragmáticos. Não são passes nem falhas; não foram medidos. Luna fez zero chamadas.

## Latência e custo

Somente respostas válidas: média **7,300 s**, p50 **6,929 s**, p95 **8,878 s** (nearest-rank, n=5; não estimativa robusta de cauda). Attempt inválida: **11,698 s**, contabilizada à parte.

| Usage | Válidas | Inválida | Total |
| --- | ---: | ---: | ---: |
| Input tokens | 5775 | 1155 | 6930 |
| Output tokens, incluindo reasoning | 2692 | 718 | 3410 |
| Reasoning tokens, subconjunto do output | 1955 | 516 | 2471 |
| Cache-read tokens | 5760 | 0 | 5760 |
| Cache-write tokens | 0 | 1152 | 1152 |
| Custo estimado USD | 0,033486 | 0,011502 | **0,044988** |

Usage disponível para todas as requests; zero custo desconhecido. Reasoning não foi cobrado duas vezes. Estimativa com tabela Standard/short-context: input $2/M, cache-read $0,20/M, cache-write $2,50/M, output $12/M; não representa auditoria de fatura. Fontes consultadas: [modelo Terra](https://developers.openai.com/api/docs/models/gpt-5.6-terra) e [preços oficiais](https://developers.openai.com/api/docs/pricing).

## Evidência e proveniência experimental

- `assertion-extraction-attempts-20260910-002129-445b524834104c71bcc2aedb3fcab3cb.jsonl`: attempt 1 inválida, preservada.
- `assertion-extraction-attempts-20260910-002209-0fda747fd12f47b1a67392fbb20b8cd0.jsonl`: continuação explícita, cinco válidas; outputs completos.
- `assertion-extraction-review.json`: revisão por attempt, critério de parada e métricas.
- Fingerprint corpus + prompt + schema: `7EA87814C25F78D8002E7E4F664CA527015FA38DCB58B5D9737CAAB3A2D0EE4A`.
- SHA256 corpus: `1337EE9CC3FD96E13F81A071024416C33DE4E3452002C5F745DADBF2226350D1`.

O teste live passou no sentido **estrutural/operacional** após a retomada. Isso não é aprovação semântica: o gate semântico falhou, e a análise está explicitamente separada da suíte fake.

Verificação local: 19 testes de Assertion (incluindo minimização de inputs e schema), mais oito regressões focadas do harness anterior; 27 passaram, um live opt-in ficou skip na verificação offline. O projeto de testes e suas dependências compilaram nessa execução. Formatação restrita aos três arquivos novos, sem mudar o prompt; diff check limpo. Corpus e `workspace/DREAMS.md` conservaram seus hashes. Não foi alegada execução da suíte completa de produção.

## Recomendação

**Não promover Assertion ao domínio/runtime ainda.** A decomposição permitiu representar corretamente a implicatura, mas não eliminou a dependência de o modelo reconhecê-la: o gargalo migrou para a extração pragmática. O erro de span foi rejeitado corretamente e não explica os quatro misses, cujos spans eram válidos.

Não recomendo implementar `Assertion[] → Boundary` nem uma primitive pública com esses dados. É necessário decidir separadamente um próximo desenho/experimento para inferência pragmática contextual antes de consumers. Não há evidência para dizer que subject binding geral, negação ou advice estejam resolvidos, pois o gate impediu chegar a esses casos. Não houve retorno automático a mais validators.

Possibilidades futuras, somente rationale: `Candidate → Assertion[] → análise específica de policy`; `Message/News → Assertions → evidência validada → Memory`. Não existem consumers, escrita de memória, cache, nodes ou persistência de assertions no projeto neste spike.
