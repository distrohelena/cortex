# Transport Live Preflight — tentativa de provisionamento

Resultado: **FAIL na preparação, antes de deploy/arm/probes de observação**.
Blocker: `validated_native_host_entrypoint_missing`.

## Evidência real coletada

- O comando instalado aponta para
  `C:/Users/Helena/AppData/Roaming/npm/node_modules/openclaw/openclaw.mjs`.
- Package instalado: OpenClaw **2026.8.1**.
- `dist/build-info.json`: commit
  `ea806575e6450e4d1efdfc72c19f04be982a1b9b`,
  buildId `2026.8.1-ea806575e645-2026-08-31T00-16-08.235Z`.
- Processo Gateway encontrado; sua linha de comando referencia essa instalação.
  A linha completa não foi registrada para evitar exposição de argumentos sensíveis.
- Checkout isolado: tag exata `v2026.8.1` na mesma base. Não foi encontrada
  divergência de versão que justificasse patch sobre versão incompatível.

Estas verificações leram arquivos/metadados de processo. Não são Dispatch Inspection
do estado efetivamente carregado e não são uma prova de health do Gateway.

## Blocker concreto

No checkout validado, `createNativeObservationReaderServer` existe em
`src/channels/message/native-observation-reader.ts`. Ela retorna um servidor
**não iniciado**. Os callers encontrados estão somente em testes.

`armExperimentalCapture` existe em `src/channels/message/native-observation.ts`;
suas invocações encontradas também estão somente em testes. Não existe entrypoint
operacional validado que componha journal, reader, arm e controles host-owned no
processo Gateway que possui o coordinator, config snapshot e plugin registry reais.

O monitor registra a lease de inspeção, mas isso não instancia o reader nem fornece
ao operador uma operação de arm. Copiar os módulos para a instalação não fecha essa
lacuna. Um processo Node separado teria outro owner/registry e não provaria o estado
efetivo do Gateway. Injetar um loader novo agora violaria a restrição de deployar
exclusivamente o código já validado.

Correção da avaliação anterior de prontidão: bibliotecas e contratos testados não
equivalem a um pacote operacional completamente provisionável. A composição do host
listada como pré-requisito no relatório anterior ainda exige implementação/teste,
não apenas configuração pessoal. Nenhuma evidência de fixture foi usada como live.

## Estado ao parar

| Item | Resultado |
|---|---|
| Versão/build | PASS — metadados da instalação e caminho do processo conferidos |
| Componentes provisionados | Nenhum |
| Arquivos/configs ativos substituídos | Nenhum |
| Backup de rollback | Não necessário nesta parada: nenhuma alteração ativa aplicada |
| Restart / relink | Nenhum |
| Capture | Não armado nesta rodada; estado in-process não consultado por API |
| Epoch / revision efetiva | Não obtidos; não inventados a partir de arquivos |
| Exact DM / self PN-LID | Não provisionados/verificados live |
| Dispatch Inspection | NOT RUN — não confundir com resultado `active` ou `unknown` da API |
| Health / Open / Poll / cursor | NOT RUN |
| Native ID observado live | Nenhum |
| Canary manual | Não solicitado; não enviar ainda |

Não há mudança para desfazer. A instalação/configuração anterior foi preservada.
Não se afirma que o lifecycle legado esteja isolado ou que não tenha atividade
independente em paralelo; ele não foi provocado por esta rodada.

## Menor próximo passo necessário

Implementar e testar **somente a composição operacional nativa** no checkout isolado:

1. Entry host-owned no mesmo processo dos owners reais, com reader autenticado
   loopback e binding exato da conta/DM; sem listener conversacional novo.
2. Startup OFF; operação explícita de arm/disarm, não restaurada após restart.
3. Inspeção disponível antes do arm e recusa de arm/probe quando isolamento não
   estiver comprovado; não usar o access-control normal como probe.
4. Manifesto/build de deploy mínimo e restauração dos arquivos/schema pertinentes,
   preservando mudanças ativas preexistentes; smoke test isolado OFF → inspect →
   arm → health/Open/Poll → disarm, sem conta real/model/agent/outbound.

Após essa composição ser validada, repetir o provisionamento autorizado com backup
seletivo **antes** de alterar a instalação. Nenhuma mudança em allowFrom, handlers
ou transportOnly foi preparada/aplicada nesta rodada: não existe ainda evidência
live de qual path precisaria ser isolado.

## Contadores e readiness

Chamadas desta rodada: `messages_send=0`, modelos=0, Graphiti=0, reader live=0.
Outbound provocado=0; automatic replies provocados=0; Shadow WOULD_SEND=0;
executions conversacionais=0; agent dispatch provocado=0; operações em grupos=0.

- **Transport Live Preflight: FAIL**, blocker de provisionamento acima; probes não executados.
- **Graphiti Live Preflight: NOT READY**, depende de transport PASS.
- **Shadow Run / DM Live: NOT AUTHORIZED, NOT STARTED**.

Sem push, commit, edição de Gateway/configs pessoais/DREAMS ou nova chamada de modelo.
