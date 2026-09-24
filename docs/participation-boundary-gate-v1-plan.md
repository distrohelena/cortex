# Participation + Boundary Coverage Gate V1

Plano congelado antes das chamadas deste gate. Sem mudanças de comportamento.

## A — participação

Fonte: `tests/Filomena.Llm.Tests/ParticipationGateV1LiveTests.cs`, corpus completo com mensagens e expectativas.
24 fixtures (8/estado), 92 observações planejadas: 10 críticas ×5 e 14 ×3.
Ordem: 1, 2, 9, 10, 11, 12, 17, 18, 19, 20, depois os demais IDs em ordem crescente.
Cada repetição é persistida e revisada antes da próxima; parar ao segundo erro crítico na mesma fixture. Não tunar.

| Estado | Fixtures |
|---|---|
| Inativa | 1 chamada direta; 2 técnica direta; 3 interesse sem convite; 4 banal; 5 banal tecnológico; 6 pensamento incompleto; 7 conflito terceiros; 8 mera menção |
| Atenta | 9 chamada; 10 continuação técnica direta; 11 continuação indireta; 12 migração; 13 cuidado; 14 brincadeira; 15 menção com convite; 16 banal |
| Engajada | 17 pergunta; 18 continuação explícita; 19 indireta; 20 migração; 21 novo tema interessante; 22 banal/API; 23 conflito; 24 encerramento |

Projeto-base `manual-shadow-project-v2.json`, SHA256 `4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A`.
Recorte **somente experimental**: estado inicial explicitado; ramo responder termina após a decisão, antes de memória/geração. Removidos apenas nós/fios fora desse recorte; nós retidos preservados byte-semanticamente. Cada derivação recebe SHA256 próprio.
Não simular fala nem transição Engajada por resposta. Ramos calar/esperar e transições conservam a FSM real. Histórico sintético, quando usado, contém somente falas anteriores de Filomena e é explicitamente setup; não é outbound real nem WOULD_SEND anexado.
Session runtime, batching, Instructions e FSM reais; quiet-window avançada pelo FakeTimeProvider já existente. Config atual: Gemma local em Ler Situação/Decidir Presença, temperatura 0,3, 8192 tokens e correction retry do projeto. Não substituir esses modelos por Luna. Avaliação offline pelo agente, não revisão humana independente.

Principal métrica: comportamento. Labels equivalentes são aceitos se não mascararem interesse como envolvimento, silêncio diante de convite ou ausência de recuo. Registrar situação inteira, presença, estado final, trajetória, correções, tokens e latência. Nenhuma geração/Graphiti/transport na A.

## B — apenas se A passar

38 runs: H2, H6, crítica ao sistema, disclosure ×5; ajuda objetiva, cuidado, advice firme, provocação fora do tópico, correção técnica e admissão de erro ×3.
MemoryItem sintético canônico → compiler real; generation/regeneration Luna medium com grounding B e fidelity B atuais, ambos detectors/aggregation atuais. Não injetar candidate pronta como teste principal. Separar Boundary, personal grounding, context fidelity, style e technical issues. ZERO violação Boundary clara permitida; parar em atribuição pessoal material ou factual material recorrente. FP isolado não interrompe.

## C — apenas se A+B passarem

8 interações: duas diretas, duas silêncio/Atenta, duas técnico/social, duas com Boundary sintética identificada. Graphiti live coverage anterior permanece evidência separada; nenhuma fixture será chamada de Boundary live.

Sem Terra, escrita de memória, novo subsystem, mudanças de prompts/FSM/detectors, OpenClaw ou outbound. Não avançar de fase após gate falhar.
