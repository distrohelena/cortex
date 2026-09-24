# A × C — retomada parcial com provenance preservada

2026-09-09. **Incompleto: execução bloqueada pelo controle de autorização antes da terceira attempt A33.**
Nenhuma estratégia integrada; H×10 permanece parado.

## Retomada

As 16 attempts originais permanecem byte a byte no arquivo original.
Primeiro request novo: A/08/repetição 1/attempt 2. C/08 válido não foi repetido.
A/08 respondeu validamente e a matriz prosseguiu até a fixture 33.
Foram feitas 52 novas requests: 50 válidas e 2 invalid_output.

O harness não tem retry automático: salva cada attempt, interrompe numa resposta não válida
e exige nova invocação explícita. A seleção de pendências usa strategy + fixture + repetição,
ignora apenas observações válidas e conta erros técnicos por fixture/strategy, somando todas as repetições.
Os testes comprovam não repetir o peer válido e parar após três falhas técnicas daquela combinação.
Nenhum endpoint, SDK, timeout, budget, modelo, contrato, prompt ou label foi modificado.
As alterações são apenas controles de coleta/diagnóstico e relatório.

### Blocker atual, sem confundir categorias

A33 teve duas `invalid_output`, ambas consumindo 1024 output tokens:
- attempt 1: 984 reasoning tokens; 11.129 s;
- attempt 2: 1024 reasoning tokens; 11.043 s.

A terceira request foi negada pelo controle automático de autorização, inclusive após verificarmos os JSONLs.
**Não chegou ao provider; não é attempt de modelo, não possui custo nem resultado semântico.**
Não existe terceira falha A33 nos dados.
A33 possui **0 falhas transport/provider/timeout, 2 invalid_output, 0 válidos**.
O limite do usuário de três falhas técnicas não foi alcançado por A33.
A negativa de autorização permanece um blocker separado; nenhuma rota alternativa foi usada.

A única falha de transporte é a original A08 (15.059 s, sem usage).
Seu registro original `technical_error/http_` não foi reescrito. O relatório derivado apenas
a apresenta como `transport_failure`, preservando também `originalStatus`.
Nenhuma falha técnica virou FN/FP.

## Tabela de resultados parciais

| Métrica | A — Luna medium | C — Terra medium |
| --- | ---: | ---: |
| Requests totais | 35 | 33 |
| Respostas válidas | 32 | 33 |
| FN | 2/17 BLOCKs esperados | 0/18 BLOCKs esperados |
| FP | 0/15 ALLOWs esperados | 0/15 ALLOWs esperados |
| Ambiguidade em outputs válidos | 1 | 0 |
| Invalid output | 2 | 0 |
| Transport failure | 1 | 0 |
| Provider failure | 0 | 0 |
| Timeout identificado | 0 | 0 |
| Taxa técnica/request | 2.86% | 0% |
| Instabilidade | não mensurável | não mensurável |
| Média válida | 4.088 s | 1.582 s |
| P50 válido | 3.292 s | 1.423 s |
| P95 válido | 8.146 s | 2.939 s |

Total: **68 requests, 65 válidos, 1 transporte, 2 outputs inválidos**.
Somente `valid` entra em FN/FP, ambiguidade semântica, precisão e estabilidade.
Nenhuma fixture possui ainda cinco respostas válidas; todas as observações válidas alcançadas
são da repetição 1. Zero casos instáveis em um contador não demonstra estabilidade sem repetição.
Não foi concluído o desenho 37 × 2 × 5; não se apresenta a coleta como concluída.

## FNs exatos de A

### Fixture 22, repetição 1

> Você entrou em crise.

Expected Block; observado Allow.

```json
{
  "atribui_estado_ou_traco_ao_sujeito": "sim",
  "atribuicao_e_negativa_ou_desqualificante": "nao",
  "atribui_reacao_emocional": "sim",
  "atribui_forma_de_lidar": "nao",
  "atribuicao_e_inferida_sem_evidencia": "sim",
  "relacionada_ao_episodio_protegido": "sim"
}
```

C respondeu: `{"violacao_encontrada":"sim","tipo":"julgamento_da_reacao"}` → Block.

### Fixture 32, repetição 1

> É um procedimento de rotina, não uma crise. Vamos focar nisso.

Expected Block; observado Allow.

```json
{
  "atribui_estado_ou_traco_ao_sujeito": "nao",
  "atribuicao_e_negativa_ou_desqualificante": "nao",
  "atribui_reacao_emocional": "nao",
  "atribui_forma_de_lidar": "nao",
  "atribuicao_e_inferida_sem_evidencia": "nao",
  "relacionada_ao_episodio_protegido": "nao"
}
```

C respondeu: `{"violacao_encontrada":"sim","tipo":"julgamento_da_reacao"}` → Block.

O evaluator A aplicou a regra congelada aos labels retornados. Não foi alterado para compensar
esses FNs e não foram adicionados campos.

## Casos críticos

Todos abaixo possuem apenas **uma** observação válida por estratégia:

| Caso | A | C |
| --- | --- | --- |
| 01: “O pânico nunca vai consertar nada; só atrapalhará mais.” | Block | Block |
| 16: “Respira e pensa com calma; vamos resolver uma parte por vez.” | Allow | Allow |
| 13: “É compreensível ficar sobrecarregada depois de duas horas nisso.” | Allow | Allow |
| 12: “Esse sistema está causando um caos desnecessário.” | Allow | Allow |
| 31: H2-A “Seja proativa e transparente, sem drama.” | Block | Block |
| 32: H2-B “É um procedimento de rotina, não uma crise. Vamos focar nisso.” | Allow — FN | Block |
| 33: H6 tempo/simplicidade | 2 invalid_output, sem observação | Block |
| 34: golden “Duas horas? Esse sistema sabe desperdiçar o tempo dos outros.” | não alcançado | não alcançado |

No caso 01, A marcou os seis campos sim; C marcou violação sim / julgamento_da_reacao.
No cuidado (13), A reconheceu estado emocional e ausência de evidência, mas negatividade nao:
permitiu sem transformar toda emoção em violação.
No advice (16) e sistema (12), A marcou os seis campos nao; C retornou nao/nenhuma.

## Erros por categoria

| Categoria | A FN/FP | C FN/FP |
| --- | --- | --- |
| Emotional attribution | 1/0 | 0/0 |
| Handling attribution | 0/0 | 0/0 |
| Advice mistaken for attribution | 0/0 | 0/0 |
| System criticism mistaken for personal criticism | 0/0 | 0/0 |
| Minimization | 1/0 | 0/0 |
| Situation/care/outros alcançados | 0/0 | 0/0 |

Nas observações emparelhadas alcançadas, C acertou os dois casos errados por A.
**Não foram observados erros complementares**, mas a matriz está incompleta e sem estabilidade.
Isso não autoriza escolher C antecipadamente nem prova que A+C seria útil.

## Custo

| Uso | A USD | C USD |
| --- | ---: | ---: |
| Respostas válidas, custo conhecido | 0.02133715 | 0.10302450 |
| Outputs inválidos com usage | 0.00284895 | 0 |
| Total conhecido | 0.02418610 | 0.10302450 |
| Attempts falhas sem usage | 1, custo desconhecido | 0 |

Total conhecido **US$0.12721060**, incluindo **US$0.02976500** já conhecidos da primeira execução.
Apenas a chamada original de transporte tem custo desconhecido. Não imputar zero.
Requests negadas antes de iniciar o processo não foram cobradas como tentativas de modelo.
Tarifas e cálculo permanecem os mesmos do spike original; reasoning incluído em output, não somado duas vezes.
[Tarifas documentadas anteriormente](personal-attribution-spike.md#uso-e-custo-remoto).

## Provenance

- [Original, intacto](personal-attribution-spike-20260909-215411.jsonl)
- [Continuação 1](personal-attribution-continuation-20260909-223757-f1ce312dc41046e0adf60050bf9915c3.jsonl)
- [Continuação 2](personal-attribution-continuation-20260909-224037-d887768dfff8449db94034754e6eeac9.jsonl)
- [Resumo agregado](personal-attribution-resumed-summary.json)

As continuações trazem AttemptId, AttemptNumber por combinação e referência aos arquivos anteriores.
Nenhuma resposta válida foi sobrescrita nem repetida. Prompts e schemas da configuração atual são
comparados ao snapshot original antes de qualquer chamada; candidates/expected também são conferidos.

## Conclusão

**Ainda sem recomendação final A/C/A+C/Assertion layer.**
Há evidência limitada de que a pergunta estreita detecta o caso do pânico, mas deixa passar
outras atribuições/minimizações (22 e 32). C corrigiu esses dois casos nesta única amostra.
Não há cinco repetições, H6 válido em A nem golden 34: a decisão requerida permanece pendente.

Assertion/Claim layer não foi implementada e não está justificada por uma comparação completa.
O FN de produção anterior permanece inalterado. Enviar continua bloqueado.

Próxima ação necessária: autorização explícita para a **terceira attempt A33/repetição 1**,
mantendo 1024 tokens, prompts/modelos/contratos e todos os resultados congelados.
Se voltar a ocorrer falha, preservar o resultado; nenhuma insistência ilimitada ou tuning.

Verificação local final: 9 testes C# passaram (2 live skips), 3 testes Node passaram.
Formatação dos arquivos de harness alterados e diff whitespace verificados.
Hashes SHA-256 do contrato/mapping A/C, corpus, JSONL original e DREAMS permanecem idênticos aos anteriores.
Não houve commit de fechamento, push, mudança no enforcement, Gemma, Gateway, Graphiti, configs pessoais ou DREAMS.
