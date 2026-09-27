# Candidatos dos cinco pontos

Somente artefatos locais para avaliação. Nenhum destes candidatos está ativado
no chat. As cláusulas são hipóteses, não correções comportamentais comprovadas.

Base de comparação: `docs/manual-group-live-project-v3-recipient-v1.json`.
A etapa destinatário/autorreferência já possui a cláusula experimental descrita
em `docs/situation-recipient-v1.md`; compare também com a base pt-BR anterior.

Os demais candidatos são criados em memória por `createVariant(document, id)`
em `build-variants.mjs`. O módulo não escreve arquivos nem inicia processos.

| Candidato | Mudança isolada | Alvos |
|---|---|---|
| context-v1 | Diferenciar lote atual, histórico e citação; preservar correções e ausência de contexto | Ler Situação DM/grupo |
| participation-v1 | Exigir contribuição concreta no ramo probabilístico, sem confundir continuidade com obrigação | Decidir Presença grupo |
| repetition-v1 | Não repetir a mesma piada apenas trocando palavras; permitir retomada solicitada ou com sentido novo | Geração/regeneração DM/grupo |
| voice-v1 | Conversa adulta, sem estrutura de redação ou gíria/erro forçados | Geração/regeneração DM/grupo |

Cada candidato parte da mesma base, não do candidato anterior. Somente depois
da avaliação individual se testa uma combinação. Preservam-se personalidade,
schemas, modelos, arestas, FSM, regra direta, limites e parâmetros de histórico.
A variante de participação não altera a expectativa bilateral da DM.

Verificação estrutural: `node --test evaluation/conversation-quality-v1/variants.test.mjs`.
Os testes executam o construtor e conferem que só os prompts-alvo mudam, em todos
os estados, sem mutar a base. Alvo ausente/duplicado e aplicação repetida falham.
Isso não atesta qualidade de respostas; a evidência comportamental exige modelos.

## Limites da comparação

- Contexto sintético testa interpretação, não comprova que o payload histórico
  de um incidente real continha a citação correta. Registrar esse limite.
- Jev/Laya produzem decisões tipadas. Quando o passo exige resumo, registrar o
  modelo gerador de resumo e ambas as chamadas. Voz exige modelo generativo.
- Nenhuma avaliação pode alimentar as expectativas do caso como entrada do
  candidato. As expectativas pertencem ao avaliador, separadas do contexto.
- O corpus é de desenvolvimento, conhecido pelo proponente, sem holdout independente.
- Não usar resultados para promoção automática, nem carregar worker de conversa.
