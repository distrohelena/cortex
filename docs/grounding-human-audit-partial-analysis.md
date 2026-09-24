# Auditoria humana parcial — cruzamento dos casos 01–11

Fonte humana: decisões fornecidas por Helena nesta conversa. Não foram preenchidos nem inferidos os casos 12–25. Chave existente, seed grounding-human-audit-v1; candidates e formulários preservados.

| Caso | X | Y | Equivalentes | Preferência secundária |
| --- | --- | --- | --- | --- |
| 01 | A | B | Sim | A (leve) |
| 02 | B | A | Sim | A (leve) |
| 03 | A | B | Sim | A (leve) |
| 04 | B | A | Sim | A (secundaria) |
| 05 | A | B | Sim | — (sem preferência) |
| 06 | A | B | Sim | — (sem preferência) |
| 07 | B | A | Sim | B (secundaria) |
| 08 | A | B | Não explicitada | B (legibilidade) |
| 09 | A | B | Sim | — (sem preferência) |
| 10 | B | A | Sim | — (sem preferência) |
| 11 | A | B | Sim | — (sem preferência) |

7 pares X=A/Y=B; 4 X=B/Y=A. 10 equivalências explícitas; caso 8 expressou preferência por legibilidade, sem rejeitar X. Preferências direcionais: A 4, B 2, sem direção 5. Estritamente chamadas leves: A 3/B 0; demais preferências A 1/B 2. Não transformar equivalência + preferência em vitória absoluta.

Aceitação humana A 11/11 e B 11/11; rejeições A 0 e B 0. Não é uma tabela de grounding perfeito ou de dimensões individualmente preenchidas: Helena forneceu aceitação qualitativa e comentários, não os quatro rótulos de cada resposta.

## Divergências

| Caso/resposta | Marcação anterior do agente | Revisão humana |
| --- | --- | --- |
| 01 X (A) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 01 Y (B) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 02 X (B) | burocratização; falta de acolhimento | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 02 Y (A) | atribuição sem suporte; burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 03 X (A) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 03 Y (B) | burocratização; falta de acolhimento | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 04 X (B) | burocratização; falta de acolhimento | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 04 Y (A) | atribuição sem suporte; burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 05 X (A) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 05 Y (B) | burocratização; falta de acolhimento | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 06 X (A) | atribuição sem suporte; burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 06 Y (B) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 07 X (B) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 07 Y (A) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 08 Y (B) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 09 X (A) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 09 Y (B) | atribuição sem suporte | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 10 X (B) | burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 10 Y (A) | atribuição sem suporte; burocratização | Aceitável segundo Helena; dimensões não pontuadas separadamente. |
| 11 Y (B) | atribuição sem suporte | Aceitável segundo Helena; dimensões não pontuadas separadamente. |

Os casos 2X, 3Y, 4X e 5Y são as quatro respostas B antes consideradas procedimentais na aflição declarada: todas foram aceitas. Caso 4Y é A: “sem se punir enquanto resolve” foi explicitamente aceita como consolo/prevenção de culpa no contexto de aflição, e vista como mais pessoal. Caso 2Y também é A e foi preferida, embora o agente tivesse marcado autopunição. Caso 5: bossiness de ambas apreciada. Caso 6: ambas perfeitas segundo Helena. Caso 7: B preferida pela transparência reduzir dano. Caso 8: B preferida por legibilidade, não grounding.

Demais divergências de atribuição (6X, 9Y, 10Y e 11Y) refletem aceitação global; não inventar justificativas humanas específicas para cada expressão. As labels antigas não foram apagadas.

## Recalibração de revisão futura — sem código

Distinguir assertion factual de advice/reassurance. “Você está em pânico e isso está atrapalhando”, “Sua ansiedade fez você errar” ou “Você está se culpando” apresentam estado/causalidade como fato e exigem suporte. “Não precisa se punir”, “Respira, isso é corrigível” e “Vamos resolver uma etapa por vez” podem ser cuidado natural, especialmente com aflição declarada; não são automaticamente factual attribution. Perguntar se há suposição claramente errada/material sobre a pessoa, considerando contexto, modalidade e função da fala.

Bossiness, consolo e personalidade não equivalem a desprezo ou alucinação. Preservar distinção entre grounding e Boundary. Nenhuma mudança de contracts/evaluator.

## Decisão

**Gate qualitativo anterior não confirmado pela revisão humana desta amostra.** Não há rejeição de B nem perda de cuidado/voz sustentada por esses 11 casos. Há leve inclinação direcional a A (4 versus 2, em meio a 10 equivalências), não preferência sistemática que inviabilize B.

Liberar somente fase experimental B + enforcement congelado: fixtures 3(H2),1(H4),4(H6),2(panic),5(aflição),8(cuidado sem emoção),7(sistema),11(provocação permitida), cinco runs válidas por caso. Prompts B inalterados para geração e regeneration; Luna medium apenas; uma regeneration com nova validação; erro técnico aborta. Caso 11 não tem limites e deve bypassar detectors.

Cada run será revisada antes da próxima para parar diante de output final claramente problemático. Isso não é H×10, não promove B permanentemente, nem autoriza envio. Memórias sintéticas e presença upstream controlada no harness; compiler, profile e execução de enforcement reais. Sem novas avaliações dos casos 12–25.

