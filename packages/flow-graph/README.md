# Flow Graph

Editor DOM em TypeScript, sem dependências de runtime, Vue, Filomena, canais ou LLM.
Inclui câmera (pan direito/meio, zoom no cursor), arraste, seleção, portas e fios de dados/execução.
Não executa o grafo nem conhece prompts, estados, modelos ou persistência.

## Build

Neste repositório: `node scripts/build-flow-graph.mjs` compila e copia os assets para a aplicação.
`dotnet build` também executa esse passo; o executável publicado embute os assets e não precisa de Node para servi-los.
O script prefere o TypeScript instalado neste package e aceita o compilador 5.9.3 já instalado no repositório.
Para usar isoladamente: instale as devDependencies deste diretório e rode `npm run build`.
Testes: `node --test packages/flow-graph/tests/*.test.mjs` após compilar.

## Contrato

Importe `createFlowGraphEditor` de `dist/index.js` (declarações em `dist/index.d.ts`).
Forneça quatro elementos: viewport, canvas dentro dele, SVG wires e div nodes dentro do canvas.
`getGraph()` retorna nodes e edges; `describeNode()` define título, portas, texto opcional e classes.
`onConnect`/`onDisconnect` alteram o modelo do host; validação de tipos/ciclos pertence ao host.
`onMove` recebe a posição proposta; o host atualiza o node. `onSelect` atualiza sua seleção.
Após mudanças externas, chame `render()`. Ao desmontar, chame `destroy()` para soltar listeners/captura.
Instâncias não compartilham câmera nem seleção. O exemplo `example.html` usa nodes genéricos.

O CSS `dist/editor.css` é opcional: a Filomena mantém seu tema existente. Nenhum stylesheet global é injetado.
`controlEdges` são fios de execução; `edges` são fios de dados. Um tipo nunca se conecta ao outro.
O editor não faz requests, não salva JSON e não chama callbacks de execução.

## Limite desta extração

A UI externa de propriedades e a definição dos tipos de node continuam no host.
Este é um package local privado, ainda não publicado. Não contém um motor de execução.
