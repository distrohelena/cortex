# Escrita de memória nativa: desenho mínimo proposto

Status: desenho e plano aprovados; implementação validada somente no checkout isolado. Não aprovado/aplicado para deploy. A investigação encontrou um contrato de escrita ausente, não apenas wiring quebrado. Resultado e limites: `../../native-memory-ingestion.md`.

## Objetivo e limites

Fazer mensagens confiáveis do runtime novo chegar à ingestão Graphiti com namespace, autoria e proveniência corretos, sem reabrir dispatch legado. Não modificar geração/FSM/participação nem concluir o experimento de sarcasmo nesta fatia. Filomena permanece desligada. Sem backfill automático, dados reais em testes, novos serviços live ou escrita no banco ativo.

## Alternativas

1. Reabrir hooks legados para capturar: rejeitado, rompe a prova de isolamento.
2. Novo writer .NET implementando também toda a autorização e extração legadas: possível, mas duplica política e aumenta a divergência entre os caminhos.
3. Reutilizar o recorder/contrato de identidade existente por uma surface host-owned estreita, chamada pelo host novo: recomendada. A surface admite só ingestão de mensagens estruturadas e consulta de estado; não chama agentes, replies, commands ou plugin dispatch genérico. Custo: precisa implementar e testar essa fronteira, ainda inexistente. Não é adicionar permissão genérica ao projeto/modelo.

## Contrato proposto

- O host fornece account/conversa/namespace e identidade canônica já verificados. O corpo da mensagem nunca autoriza esses campos. Validar concordância no receptor.
- A ingestão acompanha cada inbound elegível admitido, independentemente de a Filomena escolher responder. Não confundir silence/ignored no flow com mensagem inelegível para memória.
- Histórico de snapshot não é reenviado automaticamente. A ativação futura define explicitamente o início da janela; backfill exige aprovação própria.
- Usar proveniência nativa e IDs determinísticos compatíveis com o contrato existente. Conta/conversa fazem parte da validação de colisão. Repetição do mesmo ID/conteúdo é idempotente; mesmo ID com conteúdo diferente falha, não sobrescreve.
- Registrar pendência durável de memória antes de considerar a mensagem integralmente tratada para fins de ingestão. Dedupe da execução conversacional e dedupe da escrita são separados: retry de memória não reexecuta brain nem reenvia resposta.
- Estados observáveis distinguem pending, queued, persisted, rejected e failed. Persisted exige evidência do episódio correspondente, não somente resposta queued. Read-after-write prova a disponibilidade do fato selecionado quando aplicável; episódio sem fatos pode ser resultado válido e deve ser distinguido de erro.
- Falha de autorização/backend aparece no estado e na evidência operacional. Nunca exibir “memorizou” em queued/failed. Nada de fallback que trate outage como memória vazia.
- O isolamento permanece armado para os paths legados conforme seu contrato atual. Nenhuma nova chamada aos hooks genéricos de plugins.

## Autorização

Preservar SpiceDB/namespace authorization do recorder existente. Não substituir indisponibilidade de SpiceDB por allow. Capability de escrita específica do host, não project JSON, prompt ou LLM. Shadow/manual probes permanecem sem capability de escrita. Credenciais não são alteradas nesta etapa.

## Seleção e atribuição

Reutilizar a política de extração social existente como baseline, sem alegar que instrução textual garante correctness. Episódio/proveniência e fato extraído são coisas diferentes. Fatos precisam manter vínculo de evidência e sujeito canônico; alegação de terceiro não vira preferência do sujeito, brincadeira não vira fato biográfico e fala da Filomena não confirma automaticamente fato humano.

Testar localmente: preferência própria explícita; brincadeira; alegação de identidade; informação sobre terceiro; correção posterior; conteúdo banal que não produz fato. Incerteza não resolvida deve poder resultar em nenhum fato aceito. Não implementar perfis de sarcasmo nem generalizações pessoais.

Primeira fatia cobre inbound humano. Captura de outbound permanece separada e precisa de auditoria para não duplicar episódios nem promover respostas do modelo a evidência humana. Não declarar aprendizado completo de inbound+outbound até ambos serem verificados.

## Provas locais antes de qualquer deploy

1. Inbound nativo sintético → fronteira de memória → backend isolado/fake → episódio com account, namespace, pessoa e message ID exatos.
2. Replay/restart/timeout de memória não duplica episódio nem execução/envio.
3. Wrong account, DM/group mismatch, identity missing, auth denied e resposta queued são fail-closed quanto a declarar persistência.
4. Fact aceito → recall usando o reader real no namespace/sujeito corretos; outra pessoa/conversa não o recebe.
5. Filtros acima com extração controlada; qualquer teste com modelo real fica em banco isolado e requer escopo explícito. Fakes de extração provam plumbing, não qualidade semântica.
6. Nenhum lifecycle legado, modelo ou transporte disparado pela ingestão/status por si só.
7. Falhas e pendências visíveis; serviço sem dependência de escrita saudável não é apresentado como aprendizado operacional pronto.

## Próxima aprovação

Revisar este desenho e decidir se a fronteira host-owned que reaproveita o recorder é o caminho desejado. Depois elaborar o plano de implementação local. Escrita live/ativação/backfill continuam fora desta aprovação.
