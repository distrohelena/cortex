# Plano de edição do vídeo atual

Proposta ilustrativa para “A esquerda quer liberar as drogas?”, em formato vertical 9:16 e com alvo de 45 s. As decisões abaixo mostram o que o novo planejador deverá entregar antes da revisão humana. Elas foram preparadas para discutir o desenho; o novo node ainda não foi implementado.

As durações vêm do roteiro atual e são estimativas. A imagem original do post já foi enviada. As referências do gráfico e da cartela existem; gravações e alinhamentos de fala permanecem sujeitos ao fluxo de materiais.

| Cena | Duração prevista | Decisão de edição proposta | Justificativa | Pendência |
|---|---:|---|---|---|
| Abertura | 3 s | Post completo como apoio central. Entrada por fade curto, pausa para contexto, zoom suave para “Ela luta pela libertação das drogas”. Overlay “Tá. O que isso quer dizer?” no espaço superior. | Apresentar a origem da afirmação e direcionar a atenção ao trecho analisado. | Resolver o retângulo da frase no upload e revisar o recorte final. |
| Introdução | 5 s | Helena em plano médio fechado. Corte direto, câmera estável, legenda no espaço inferior. | Estabelecer a pessoa que apresenta a análise. | Selecionar ou gravar a fala de introdução. |
| Três conceitos | 7 s | Helena com os três termos autorais apresentados como apoio, com composição que preserve espaço para a pessoa e a legenda. | Organizar as distinções que serão explicadas na próxima cena. | Revisar os gatilhos: “legalização”, “descriminalização” e “redução de danos” não aparecem na fala atualmente escrita para esta cena. Não há alinhamento comprovado desses termos. |
| Diferença | 12 s | Gráfico em tela inteira, com imagem preservada por `contain`. Texto autoral “LEGALIZAR ≠ DESCRIMINALIZAR ≠ TRATAR” no espaço superior; fala legendada abaixo. Movimento da imagem desligado na proposta inicial. | Permitir comparar as três ideias com tempo para leitura. | Revisar a peça gráfica de referência e selecionar/gravar a explicação. |
| Pergunta | 4 s | Retomar o post completo sem zoom. Apresentar “QUAL LEI?”, “QUAL PROPOSTA?”, “QUAL VOTAÇÃO?” e “O POST NÃO DIZ.” em sequência planejada e editável. | Voltar ao material de origem e destacar a falta de especificidade que o roteiro questiona. | Confirmar a sequência de textos e seus tempos com a gravação real. |
| Conclusão | 11 s | Helena em plano médio fechado, imagem estável e legenda. Corte direto. | Dar espaço para a conclusão e preservar a ressalva presente no argumento. | Selecionar ou gravar a fala completa. |
| Encerramento | 3 s | Cartela ocupando o quadro com `cover`, espaço para “DEPOIS DO SLOGAN” e a fala final legendada. Entrada discreta e imagem estável. | Encerrar com a identidade do quadro e sua frase de fechamento. | Revisar a cartela e gravar/selecionar o texto exato solicitado no roteiro. |

## Movimento da abertura

| Intervalo local da cena | Ação |
|---|---|
| 0 a 0,25 s | A imagem aparece por fade. |
| 0,25 a 0,8 s | O post permanece inteiro e estável. |
| 0,8 a 2,2 s | A imagem aproxima suavemente o alvo, com escala proposta de 1 para 1,35 e curva `ease_in_out`. |
| 2,2 a 3 s | O enquadramento final é mantido até o corte. |

O zoom afeta apenas a camada da imagem. O overlay conserva seu tamanho. A frase-alvo é a que já consta no `focus.target_text` do roteiro. A área concreta e a escala final devem ser revistas sobre o upload real; uma área ainda pendente não é apresentada como localização concluída.

Na interface, cada linha do plano terá sua justificativa e controles já preenchidos. Uma alteração de intensidade ou foco feita pela pessoa permanece registrada como humana e é preservada quando o Cortex recalcular as outras decisões.
