# Task 2 — relatório

## Resultado

Implementado somente o editor frontend do fluxo DM:

- fios tipados com `sourcePort` e `targetPort`;
- portas técnicas `message`/`from` na DM, `message` entrada/saída na Instrução e `message`/`to` no `send`;
- uma conexão por porta de entrada tipada e fanout permitido na saída;
- normalização conservadora de fios legados sem portas;
- node terminal `send` / “Enviar mensagem”, preservando `Saída`, `Exit`, `Branch` e `Acumular` como desenhos existentes;
- controle live mínimo, oculto até `GET /api/flow/live` retornar `available:true`;
- ativação com `JSON.parse(saveProject(project))`, desativação com `{}`, header `X-Filomena-Playground: 1` e sem ativação automática.
- `liveBusy` é independente da simulação local: `Desativar` continua disponível durante uma geração local; após reload, um ativo sem snapshot local é mostrado como “snapshot não confirmado”.
- O primeiro node adicionado usa ID estável `send`; a simulação local informa indisponibilidade de `Enviar mensagem` sem classificá-lo como protótipo visual.

Edições no desenho após ativação são marcadas como rascunho alterado; o snapshot ativo não acompanha o rascunho.

## TDD / testes

RED inicial:

```text
node --test tests/frontend/flow-model.test.mjs tests/frontend/flow-project.test.mjs tests/frontend/flow-live.test.mjs
18 testes: 14 pass, 4 fail — falhas esperadas por portas/send/status ainda ausentes.
```

GREEN final:

```text
node --test tests/frontend/flow-*.test.mjs
35 testes: 35 pass, 0 fail

node --check src/Filomena.App/Playground/flow.js
exit code 0
```

Após o polish de revisão, o teste de contrato live foi reexecutado: `3 pass, 0 fail`.

Coberturas adicionadas: coexistência de `dm.from → send.to` com mensagem, rewire por porta sem apagar outra, fanout, rejeição Pessoa→mensagem, round-trip de portas/send com Branch/Acumular, normalização legada ambígua/conservadora e contrato de status live.

## Arquivos alterados

- `src/Filomena.App/Playground/flow-model.mjs`
- `src/Filomena.App/Playground/flow.js`
- `src/Filomena.App/Playground/flow.html`
- `src/Filomena.App/Playground/flow.css`
- `tests/frontend/flow-model.test.mjs`
- `tests/frontend/flow-project.test.mjs`
- `tests/frontend/flow-live.test.mjs`

Nenhum arquivo C# foi alterado. Não houve ativação live, envio real, alteração de Gateway/Graphiti/DREAMS, commit ou staging.

## Handoff para o compilador C#

O schema de fios agora pode carregar `{source,target,sourcePort,targetPort}`. Para o recorte executável, os IDs estáveis são `dm.message`, `dm.from`, `prompt.message` e `send.message`/`send.to`; `from`/`to` são ActorRef e não são compatíveis com `message`. Fios antigos sem portas só recebem `message → message` quando a inferência é inequívoca; casos ambíguos permanecem sem portas para preservação visual/conservadora.

Backend já integrado pelo controlador (`docs/dm-flow-canary.md` e suíte fake bridge→worker); este Task 2 não duplicou nem modificou essa implementação.
