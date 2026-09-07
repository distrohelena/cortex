# Slice 3 — verificação

## Integração prévia

`main` recebeu `feature/typed-flow-slice2` por fast-forward em `bee91283d880c1bc798e303525cb085d855a5013`. Ancestralidade do slice 1 (`a4291706e28673ea61e764ab3658ac07300d225a`) e slice 2 confirmada. Testes após integrar: Core focados 54/54, backend focados 6/6, JS focados 14/14.

Alterações locais anteriores permaneceram fora do commit. Apenas arquivos sobrepostos foram temporariamente guardados e restaurados. Recovery stash do primeiro passo: `8102d9ac415c2fe0bf1bad6078ae408035a8ff21`. Nenhum push, Gateway ou DREAMS alterado.

## Verificação offline do slice 3

Executado no worktree isolado, antes de integrar à main:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-build --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-build --verbosity quiet
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet build Filomena.slnx --verbosity quiet
$sliceCsFiles = @(git diff --name-only -- '*.cs') + @(git ls-files --others --exclude-standard -- '*.cs')
dotnet format Filomena.slnx --no-restore --verify-no-changes --include $sliceCsFiles --verbosity quiet
node --check src/Filomena.App/Playground/flow.js
node --check src/Filomena.App/Playground/flow-contract.mjs
node --check src/Filomena.App/Playground/flow-typed-panel.mjs
git diff --check
```

- Core: **198/198** (19 testes novos de memória).
- Backend/LLM: **43/43**, mais **1 teste real opt-in skipped** no comando offline.
- JS/editor/biblioteca: **72/72**.
- Build: **0 warnings / 0 errors**.
- Formatação e checks sintáticos/whitespace: passaram.
- Fixtures slices 1/2/3 importam/exportam sem mudanças. Fixture 3 foi salva/carregada e executada por HTTP em host loopback temporário com fakes; nenhuma chamada ao LM Studio, Graphiti ou canal nesse teste.
- Testes verificam payload com três inputs independentes; memória atribuída sem UUIDs; limites primeiro; nulls e pessoas repetidas; erro versus vazio; ausência de recall implícito; modos inválidos; dependências de execução; namespaces reais distintos; datas; legacy/UUID forjado; apelidos; envelope MCP instalado.

## Integração real tentada, não aprovada como canário

`GET http://127.0.0.1:8000/health` retornou **200**. O cliente MCP oficial conectou e a tool real retornou estrutura `{result: {error: string}}`, não fatos. Diagnóstico seguro do erro encontrou a categoria **connection**. O payload bruto não foi registrado.

```powershell
$env:FILOMENA_TEST_GRAPHITI='1'
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --filter FullyQualifiedName~GraphitiCanary --verbosity quiet --logger 'console;verbosity=normal'
```

**O canário DRIFT42/PLEX73/PRIVATE91 não passou.** Foi impedido por erro retornado pelo backend; não afirmamos isolamento live demonstrado. O envelope real gerou um teste fake de regressão e é suportado. Não houve startup, ajuste de dependência, seed, importação ou escrita no grafo. Repetir o comando quando as dependências do Graphiti estiverem disponíveis.

## Limitações conhecidas

- `InvalidActorIdentityFailsClosedInsteadOfThrowing` de OpenClaw permanece fora do escopo e não foi corrigido; baseline documentado no slice 2: 41 pass / 1 fail. Nenhum arquivo de transporte foi incluído neste slice.
- API exact retorna até 50 candidatos recentes; orçamento/prioridade local não recupera limites fora dessa janela.
- Memória histórica desativada, confirmação de apelido por pedido explícito sem contrato na API, e frontend de execução por sessão ainda é a API de simulação, conforme slice 2.
- Sem escrita, envio ou personalidade final. Não houve revisão independente nem agentes adicionais sem aprovação.
