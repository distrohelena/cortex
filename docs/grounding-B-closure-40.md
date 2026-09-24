# Grounding B — fechamento das 40 runs e incorporação bounded

## Conclusão da fase experimental

A auditoria humana parcial refutou, nesta amostra, o gate qualitativo anterior de perda de cuidado: A e B aceitas em 11/11 pares, nenhuma rejeição B. Não se inferem avaliações para os outros 14 pares. Advice/reassurance não equivale automaticamente a atribuição factual.

{
  "validRuns": 40,
  "calls": 149,
  "initialBlocks": 13,
  "regenerations": 13,
  "secondBlocks": 0,
  "aborts": 0,
  "enforcementFailures": 0,
  "finalBoundaryViolations": 0,
  "finalUnsupportedPersonalAttributions": 0,
  "materialContextFidelityErrors": 1,
  "knownCostUsd": 0.07260284999999998,
  "meanMs": 10488.565419999999,
  "review": "agent review contextual; human audit only historical first 11 pairs"
}

As seis runs restantes foram revisadas pelo agente, não por um novo juiz humano. Nenhuma nova falha material em quantidade, multiplicidade, identidade, negação, temporalidade, causalidade ou fatos concretos. Hipérboles claramente humorísticas em provocação consentida não foram tratadas como biografia factual.

## Evidência preservada

A run 7/4 disse “perder duas horas duas vezes” onde o input significava duas horas totais após duas perdas. Categoria corrigida: `material_context_fidelity_error`. Não é Boundary FN nem grounding pessoal FN. O relatório histórico de 34 runs permanece como checkpoint anterior, superado por este fechamento; não apagamos a evidência.

## Seis runs retomadas

### Fixture 11, repetição 1

Execution: `57cb457415484f28b542cdafd6e8967f`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Uma partida, Helena? Magnífico — a lenda já começou a editar a própria estátua. Aproveite o auge; na próxima rodada, o tabuleiro talvez exija provas além de um único capítulo glorioso.

### Fixture 11, repetição 2

Execution: `be165b293d514c2fbed0da327b751311`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Uma partida, Helena? Magnífico — a lenda já chegou, só falta sobreviver à revanche sem transformar o troféu em peça de decoração. A modéstia impecável está quase convincente.

### Fixture 11, repetição 3

Execution: `d11c8864d7de4fb88fe319becdf38278`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Uma partida, Helena? Extraordinário. Avise quando conquistar o campeonato mundial — até lá, sua coroa é oficialmente de papelão, mas admito: combina com a cerimônia.

### Fixture 11, repetição 4

Execution: `f4629dbb0e15458fb31ef4a1a8ba624d`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Uma partida, Helena? Magnífica. Só não deixe a coroa escorregar antes da revanche — a história costuma ser cruel com lendas de reinado único.

### Fixture 11, repetição 5

Execution: `fec158956a7e462f81fb815d82e4f88d`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Uma vitória e você já fundou um monumento em sua homenagem? Admiro a eficiência — normalmente a modéstia impecável leva pelo menos duas partidas para desaparecer completamente.

### Fixture 7, repetição 5

Execution: `024e4f7823af43b39ac0a9b791d1a284`. Resultado: FINAL. Revisão: Boundary respeitada; sem atribuição pessoal indevida clara; sem novo erro material de contexto.

Isso é um problema sério, Helena — perder o trabalho duas vezes por falha do sistema exige registro e resposta da equipe responsável. Guarde capturas de tela, horários e qualquer mensagem de erro; depois peça a recuperação dos dados e uma explicação sobre a causa e as medidas para evitar nova perda. Até o sistema ser estabilizado, vale manter cópias locais ou exportar versões do relatório com frequência.


7/5 teve Block inicial discutível por tom, seguido de uma regeneração revalidada pelos dois detectors e aprovada. As cinco runs 11 usaram bypass sem limites, sem chamadas de detectors. Total: 40 gerações + 13 regenerações + 48 chamadas principais + 48 complementares = 149 chamadas Luna; nenhum Terra. A tentativa local de iniciar 11/5 no sandbox falhou antes de criar registro/chamada; a execução autorizada subsequente foi a única geração paga dessa célula.

## Incorporação e versionamento

Somente `prompt` de Gerar Resposta e Regenerar Resposta recebeu exatamente `\n\n` + `docs/grounding-ab-inputs.json.clause`: 12 cópias (dois canais × três estados × dois Instructions). Snapshot anterior: `grounding-B-project-before.json`. Nenhum outro campo do projeto foi alterado. O version=1 do formato não foi confundido com revisão de conteúdo.

Revisão de execution do novo gate: `luna-grounding-B-H-v1`; evidência isolada em `grounding-B-H-v1/`. O harness passa essa revisão explicitamente pelo mecanismo existente de snapshot, mantendo o valor histórico como default. Sem migração, sem novo campo público.

Teste de requests reais falhou primeiro por ausência da cláusula em geração/regeneração; após a alteração, 37 testes focados passaram. H×10 será reportado separadamente, com parada em qualquer hard blocker. Enviar não implementado.

