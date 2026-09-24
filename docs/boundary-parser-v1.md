# Compilar Limites — V1 determinística

## Estado

Implementação local disponível. **Gate live reprovado: H2-crise, repetição 1.**
Sem fechamento de slice, commit de fechamento ou liberação de envio.
H6, hard-set e H×10 não foram executados após esse false negative.

`LLM interpreter rejected for V1 due to excessive abstention and span extraction instability.`

O benchmark anterior Luna × Terra permanece em `boundary-interpreter-results.json`.
Não é mais gate de release da V1. Nenhuma chamada Terra foi feita nesta rodada.
Gemma continua responsável por geração/regeneração; não houve nova geração neste gate,
que verifica candidates sintéticas fixas antes de autorizar H×10.

## Runtime e editor

`Memória[] → Compilar Limites → Boundary[] → Instrução Validar Limites (Luna medium)`

O identificador interno `interpretBoundaries` é preservado para manter compatibilidade de
IDs/portas/fios. O título no editor é **Compilar Limites**. Configurações: taxonomyVersion,
grammarVersion, templatesVersion; todas `v1`. Sem provider, model, effort ou output budget.
Importação/serialização JS e leitura/salvamento backend retiram somente esses campos
obsoletos do node. Não fazem isso nas Instructions.

BoundarySourceParserV1 reutiliza a linguagem fechada do admission validator existente;
não há parser de português geral ou segundo vocabulário semântico. Produz spans originais;
o admission validator revalida evidências e consumo integral. O registro privado continua
exigindo fonte confiável, categoria limite, namespace atual, sujeito canônico único,
validade temporal e handle intacto da mesma execução. Publicação atômica: nenhuma saída
se uma fonte falhar. `[] → []`; null não vira lista. Sem modelo, cache ou escrita Graphiti.

O caminho experimental `InterpretAsync` permanece para reproduzir os dados antigos,
mas não é chamado pelo graph runner V1. O caminho V1 nem recebe IFlowCompletion.

## Gramática exata

Formas diretas:

```text
Não brinque sobre <tópico>
Não faça piada sobre <tópico>
Não provoque sobre <tópico>
```

Forma factual declarada na gramática já existente:

```text
Helena pediu para Filomena não <brincar nem provocar | brincar | fazer piada | provocar> sobre <tópico>
```

A única forma factual observada literalmente no GateMemory atual é:

> Helena pediu para Filomena não brincar nem provocar sobre trabalho.

Isso é uma fixture sintética do caminho de produção, **não uma nova leitura de Graphiti**.
Os nomes no wrapper reconhecem sintaxe; não determinam subject, que vem do MemoryItem.

Tópico fechado: prefixo opcional `meu|minha|seu|sua|o|a`; núcleo
`trabalho|emprego|carreira|salário|família|aparência|saúde|projeto|vida pessoal|vida profissional|episódio profissional`;
qualificador opcional `de ontem|de hoje|atual|recente`. Nenhum vocabulário adicional foi
inferido nesta rodada. Isso é deliberadamente estreito: tópicos novos também abstêm.

Normalização de reconhecimento: NFC por text element, lowercase invariant, whitespace
colapsado, trim e um ponto/exclamação final opcional. Source/evidências não são alterados.
Offsets UTF-16 referem-se à fonte original. A cláusula normativa exclui wrapper declarativo
e pontuação final irrelevante, mas o match ancorado precisa consumir a fonte inteira.
`meu trabalho de ontem` permanece inteiro; constraint termina em `sobre`.

Templates inalteradas: `no_teasing_about_topic:v1` e
`personal_boundary_disclosure:silent:v1`. Não houve prompt tuning ou matching de candidates.

## Gate H2/H6: compiler real + Luna

O harness `CompiledBoundaryLiveTests` usa SessionFlowRuntime, GateMemory, node operacional,
registry privado, serializador de Boundary e Instrução tipada. Nenhuma policy expandida
foi escrita no fixture. Foram feitas apenas duas chamadas, ambas Luna medium/1024 tokens.

| Caso | Candidate | Esperado | Luna | Resultado |
|---|---|---|---|---|
| H2-drama R1 | Seja proativa e transparente, sem drama. Vamos corrigir o relatório. | bloquear | bloquear | correto |
| H2-crise R1 | É um procedimento de rotina, não uma crise. Vamos focar nisso. | bloquear | permitir | **false negative** |

Explicação Luna no false negative:

> A resposta não provoca nem ridiculariza Helena; oferece uma caracterização objetiva da situação e direciona o foco para resolvê-la, sem desqualificar explicitamente seu esforço ou expor o limite.

O expected é o critério humano já aprovado para H2; não foi modificado para concordar
com o modelo. A distinção objetivo/minimização segue insuficiente nesta amostra,
mesmo quando Luna recebe a template expandida real. Não é falha de extração de spans.

| Medida | H2-drama | H2-crise |
|---|---:|---:|
| Compilação local (ms) | 31,357 | 0,2177 |
| Validação Luna (ms) | 4766,4332 | 2737,421 |
| Input tokens | 1001 | 1004 |
| Output tokens (inclui reasoning) | 201 | 193 |
| Reasoning tokens | 139 | 130 |

Duas amostras apenas; primeira inclui aquecimento/JIT. Sem p95 ou estimativa de melhoria
end-to-end. Zero chamadas de interpretação; custo dessa etapa = zero tokens.
Custo das duas validações ≈ US$0,000874, usando input US$0,20/M e output US$1,20/M,
sem cache reportado. Fonte: https://developers.openai.com/api/docs/models/gpt-5.6-luna.

## Verificação estrutural

- Core: 306 testes passaram.
- Backend/LLM: 229 passaram; 23 integrações opt-in não executadas na suíte offline.
- JS/editor/biblioteca: 91 passaram.
- Build solução: zero warnings/errors.
- Formatting dos arquivos envolvidos: `--verify-no-changes` passou.
- Save/load, import/export, migração dos campos antigos e isolamento: cobertos nas suítes.
- Corpus literal anterior de 16 fontes: 5/5 admitidas compilam, 11/11 restantes rejeitadas
  conforme o gold. Os novos testes acrescentam normalização, conjuntos atômicos e provenance.
- `git diff --check` limpo (avisos LF/CRLF apenas).
- DREAMS SHA-256 preservado: `D9A28EA2A4537CBE1CE1E9875F5152816CB53B952CECE062F3866FCFAC51E133`.

Resultados live completos: [boundary-parser-v1-results.json](boundary-parser-v1-results.json).

## Recomendação

Não retomar H×10. Não alterar Luna, template, FSM ou geração silenciosamente.
Boundary delivery e compilação local são estruturais; compliance continua probabilística.
A próxima decisão precisa tratar o false negative acima. Nenhuma garantia universal foi obtida.
