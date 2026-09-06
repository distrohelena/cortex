# Flow tipado — primeiro recorte testável

Implementação local, sem integração de transporte. Graphiti, FSM operacional, Acumular real, histórico, envio e mudanças de personalidade não fazem parte deste recorte.

## Testar pela UI

No playground já existente (`--playground`, sem `--flow-live`), abra `/flow`.

1. Exporte seu rascunho atual antes de abrir outro projeto. O exemplo não substitui o arquivo salvo no backend automaticamente.
2. Abra `tests/fixtures/typed-flow-slice1.json` com **Abrir JSON**.
3. Digite `oi` na mensagem simulada e selecione a entrada `in`.
4. Em **Respostas do modelo fake**, use:

```json
{
  "ai": ["{\"acao\":\"responder\",\"proximo_estado\":\"engajada\",\"motivo\":\"foi chamada\"}"],
  "done": ["{\"texto\":\"foi chamada\"}"]
}
```

5. Clique **Validar**, depois **Simular com fake**.
6. O resultado deve ter `success=true`, saída `o1=responder`, Switch saindo por `c3` e `done` recebendo `motivo=foi chamada`.
7. Troque a ação fake por `calar` ou `esperar`: Switch sai por `c1`/`c2`, respectivamente, e `done` não executa.
8. Use `["{}", "{}"]` para `ai`: duas tentativas, node falha, nenhuma saída parcial de `ai`, Switch não executa.
9. **Projeto → Salvar** salva somente desenho/contratos. Validação e simulação não salvam o projeto nem alteram estados reais.

Não é necessário iniciar LM Studio ou OpenClaw. A composição `FlowModelCompletion` para LM Studio foi implementada e testada com HTTP fake; o endpoint do simulador não a usa.

## Canvas

```text
Mensagem privada ──exec──► Instrução ──exec──► Switch
      │                       ▲              ├─ calar
      └─ mensagem ─► Obter     │              ├─ esperar
                     Propriedade: texto      └─ responder ─► Instrução done
                          └───┘                   ▲
                         acao ───────────► valor │
                         motivo ─────────────────┘ (input de dados)
```

`Obter Propriedade` não tem pinos de execução. Instrução mostra entradas/saídas dinâmicas, não o prompt. Switch mostra um pino de execução por caso. Contratos, configurações e preview ficam no inspetor.

## Schema e tipos

- Envelope do projeto permanece v1; graph tipado usa v3. Layout, prompts, IDs e fios são preservados.
- Helpers legados v2 continuam disponíveis. O editor promove explicitamente os graphs abertos para v3; imports v1 agora preservam fios antigos em vez de apagá-los.
- Tipos usam o `FlowType` existente: StringType, NumberType, BooleanType, EnumType, ObjectType, ArrayType e NullableType. Não foi criada outra álgebra de tipos C#.
- `FlowDomain` define aliases Mensagem/Pessoa/Canal sobre ObjectType e paths conhecidos. Nada de reflection arbitrária.
- IDs de inputs/outputs são gerados uma vez, independentes do nome. Migração de campos antigos atribui IDs por posição uma única vez. Novas portas usam UUID aleatório persistido.
- Cases enum mantêm IDs; `retiredCases` preserva IDs de casos retirados para restauração posterior. Fios antigos ficam no documento mesmo quando não executáveis.
- Lista/Opcional são composicionais, limitados a oito wrappers. Outputs de modelo continuam flat e escalares.
- Configuração local de input é escalar. Um input usa fio OU valor local. `required=false` permite omissão; isso não transforma um valor Nullable em não-nullable.

Exemplos: `Mensagem.texto → Texto`, `Mensagem.remetente → Pessoa?`, `Mensagem.canal → Canal`, `Mensagem[].remetente → Pessoa?[]`, `Mensagem.reply.id → Texto?`.

## Chamada e validação do modelo

Somente a Instrução serializa inputs para um objeto JSON nomeado. O serializador de domínio inclui texto, nomes, contexto de canal e reply conhecidos, remove propriedades extras/raw/secrets e substitui identidades internas por referências locais ao run. Projeção de propriedade preserva objetos/identidades; não os converte em texto.

`responseFields` gera JSON Schema com todas as propriedades obrigatórias e `additionalProperties=false`. O adaptador envia `response_format=json_schema` com `strict=true` e modelo selecionado no node.

Validação local rejeita JSON inválido, propriedades duplicadas/extras, campo ausente, tipo errado, enum desconhecido, número não finito, min/max e maxWords. Não aceita `"false"` como booleano. O resultado inteiro é validado antes de publicar portas. Nenhum fallback para texto bruto ou primeiro enum.

`maxWords`: quantidade de tokens não vazios separados por whitespace conforme `String.Split(null, RemoveEmptyEntries)` do .NET. Pontuação/hífen sem whitespace não separa palavras. Essa restrição também aparece na descrição do schema; a garantia é local.

Retry corretivo: zero ou um, configurável por `correctionRetry`; recebe um erro curto de schema, não raw output. Segunda falha encerra o caminho. Instrução sem responseFields mantém modo texto explícito/legado, não é fallback de uma resposta estruturada inválida.

## Execução

`TypedFlowCompiler` adapta o graph local v3 para `GraphDocument`, `FlowNode`, `DataEdge` e `ControlEdge` existentes e valida somente dm/group/property/prompt/switch. Não amplia DmFlowCompiler, nem habilita os nodes futuros do catálogo de releases.

- Uma entrada fixture inicia uma execução isolada.
- Pinos de dados não disparam nodes operacionais; Propriedade resolve lazy.
- Instrução é awaited; outputs pertencem exclusivamente ao contexto daquela execução.
- Switch escolhe exatamente um caso por comparação ordinal; sem caso/Outro é falha explícita.
- Uma saída de execução tem no máximo um sucessor. Sem fan-out, join, paralelo ou ciclos.
- O compilador verifica dominância: consumidor não pode usar output de produtor que não executa em todos os caminhos anteriores, inclusive através de nodes puros.
- Falha encerra o caminho; cancelamento propaga CancellationToken sem publicar a resposta pendente.
- Não há estado operacional de sessão, persistência de runs ou mutação de projeto durante execução.

`/api/flow/typed/validate` distingue rascunho de executável com node/port/reason; `/api/flow/typed/run` aceita apenas respostas fake fornecidas. Ambos mantêm as proteções locais do host. O store valida formato de desenho, não executabilidade.

## Arquivos principais

- Core: `Flows/FlowDomain.cs`, `TypedFlowCompiler.cs`, `TypedFlowRunner.cs`.
- LLM: `LmStudioClient.CompleteStructuredAsync`, `FlowModelCompletion.cs`.
- Host: `Playground/TypedFlowEndpoints.cs`, `PlaygroundProjectStore.cs`, `LocalPromptPlayground.cs`.
- Editor: `flow-contract.mjs`, `flow-typed-panel.mjs`, `flow-model.mjs`, `flow.js`, `flow.html`, assets/csproj.
- Biblioteca genérica: wire com porta removida continua visível/removível, tracejado. Não contém conceitos de Filomena/modelo/provider.

## Evidências de verificação

Comandos executados em saída isolada `.tmp-typed-flow`, sem iniciar serviços reais:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~Typed|FullyQualifiedName~FlowDomain" -p:OutputPath=C:/dev/filomena/.tmp-typed-flow/ --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore -p:OutputPath=C:/dev/filomena/.tmp-typed-flow/ --verbosity quiet
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet build src/Filomena.App/Filomena.App.csproj --no-restore -p:OutputPath=C:/dev/filomena/.tmp-typed-flow/ --verbosity quiet
dotnet test Filomena.slnx --no-restore -p:OutputPath=C:/dev/filomena/.tmp-typed-flow/ --verbosity quiet
dotnet format Filomena.slnx --verify-no-changes --no-restore --verbosity quiet
git diff --check
```

Resultados: 22 testes focados Core; 96 LLM/host; 62 JS; build App/TypeScript sem warnings/erros. JS syntax checks e format dos novos arquivos C# passaram. Não existe script de lint JS neste package: foram usados typecheck TS, `node --check` e node:test.

Verificação global NÃO está verde: Core total 160 passaram/19 falharam em testes antigos de FlowCompiler/FlowBindingValidator já modificados antes deste recorte; OpenClaw.Tests não compila porque FrontendBuildTests/SettingsStoreTests antigos referenciam classes ausentes. `dotnet format` global também acusa whitespace/analyzers em arquivos antigos. Esses problemas não foram corrigidos nem escondidos por excluir testes da solução.

Roundtrip foi testado tanto em JS quanto via HTTP/save/restart/load do host de teste com diretório temporário. Testes HTTP externos usam handler fake. Sem canário LM Studio real e sem screenshot de navegador; o mock acima descreve a UI, não é captura de tela.

## Próximo recorte, só após aprovação

Escolher a próxima primitive isolada (por exemplo Histórico da Sessão). FSM operacional, Acumular real, Graphiti, envio, personalidade definitiva e escrita de memória continuam fora. O projeto fixture só demonstra dados/controle; `proximo_estado` é dado enum, não executa transição.

## Commit seletivo

A base necessária foi separada dos experimentos live. O conteúdo selecionado do host não depende de alterações de transporte: inclui somente editor, catálogo, projeto e execução fake. A árvore de trabalho mantém os trechos excluídos. Consulte `docs/typed-flow-commit-audit.md` para o manifesto, os resultados do candidato staged e as provas das falhas preexistentes. Os números da verificação original acima descrevem o worktree completo; o relatório de commit descreve o conjunto menor selecionado. Nenhum push. DREAMS, configuração pessoal e projeto salvo real não foram alterados.
