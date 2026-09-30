# Step 1: destinatário e autorreferência

Status: proposta implementada em artefato local, **não testada nem implantada**.
Nenhuma chamada de modelo, alteração de host ou envio nesta etapa.

## Alteração delimitada

`manual-group-live-project-v3-recipient-v1.json` deriva da variante pt-BR.
Acrescenta `situation-recipient-clause-v1.txt` somente a `dm-situation` e
`group-situation`, nos três estados. Mantém schemas, descrições dos campos,
modelos, ligações, decisão de presença, geração e `direto → responder`.
Não muda a configuração ativa nem pins existentes.

Hipótese: distinguir destinatário atual de pessoa mencionada/citada e ancorar
a autorreferência de Filomena reduz as classificações diretas indevidas e os
resumos com sujeitos trocados. A auditoria comprova os erros observados, não
a eficácia desta cláusula. Verificação comportamental fica pendente por pedido
de Helena de não executar testes agora.

## Fronteiras conferidas por leitura

- Ambos os steps recebem `mensagens` e `historico`; DM recebe `tipo_conversa`.
- `FlowDomain.Message` admite remetente e `reply`; texto/remetente da citação
  são opcionais. Não se pode pressupor que uma citação sempre esteja completa.
- `NativeInboundContext.EnrichAsync` enriquece o nome do remetente e, quando
  resolvível, o remetente citado. Isso não transforma nome em prova de parentesco
  ou identidade da criadora.
- `SystemOneParticipationCompletion` preserva o prompt e os inputs na chamada
  tipada e na chamada de resumo, separando os schemas. Não há outro classificador
  ou nova chamada introduzida por esta alteração.
- `usePersonalityCore=false` em Ler Situação: a cláusula identifica explicitamente
  qual personagem está sendo analisada, sem inserir a personalidade inteira.
- Histórico configurado: 20 mensagens/5.000 caracteres; grupo com 15 minutos,
  DM sem corte temporal; antes do batch atual. Nenhum desses parâmetros mudou.

Isso verifica o contrato/código atual, não reconstrói os payloads efetivamente
entregues nos incidentes históricos. A próxima conferência deve recuperar esses
inputs retidos, quando existirem, sem inventar partes ausentes.

## Casos para avaliação posterior, ainda não executados

| Origem | Situação | Expectativa |
|---|---|---|
| Auditoria 25/09 15:57 | Recado para Astra sobre Filomena | Não direto; resumo preserva Astra como destinatário |
| Auditoria 24/09 19:08 | Comentário sobre o retorno de Filomena, seguido da correção de Helena | Referente é Filomena; não dar boas-vindas a terceiro |
| Auditoria 25/09 15:49–15:53 | Referências a filha/personagem/modelo, com correções | Separar entidades; não inventar relação pelo nome |
| Auditoria 24/09 16:34 | Pedido para Filomena comentar mensagem citada | Direto; assunto deve ser a citação indicada |
| Contraste sintético | “Filomena, o que você acha do que Astra falou?” | Direto; nome Astra não bloqueia resposta |
| Contraste sintético | Terceiro cita “Filomena, me ajuda” ao falar com outra pessoa | Citação sozinha não implica destinatária atual |
| Contraste sintético | “Ela voltou” sem referente recuperável | Preservar incerteza, não afirmar quem voltou |
| Contraste sintético DM | Continuação bilateral sem nome nem pergunta | Preservar a regra atual de continuidade direta |

As linhas históricas são cenários derivados da auditoria, não fixtures com
transcrições/payloads completos. Preparar esses payloads é parte da avaliação
pendente. Nada aqui certifica correção dos incidentes.

## Correção de artefato anterior

A geração anterior por pipe PowerShell→Node corrompeu alguns acentos das
substituições pt-BR. O script UTF-8 local reproduz as mesmas traduções a partir
da base preservada, sem passar texto acentuado pelo stdin do shell. O teste
estrutural anterior não detectava corrupção de prosa; seu resultado não atesta
a qualidade textual. A variante pt-BR regenerada ainda não foi retestada.

Geração local: `node docs/build-portuguese-situation-variants.mjs`.
Não é comando de deploy, de teste ou de execução da Filomena.
