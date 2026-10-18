# Diagnóstico de falhas de execução

## Investigação e escopo

O caminho investigado foi `TypedFlowRunner` → `FlowExecutionKernel` → `IFlowCompletion`, incluindo `HostBoundFlowCompletion`, validadores definidos pelo host e adapters de transporte.

Antes desta alteração:

- `HostBoundFlowCompletion` substituía exceções do adapter por HTTP/timeout/InvalidOperationException com códigos sanitizados, sem conservar o objeto original. A sanitização era intencional: mensagens e inner exceptions podem conter prompts, endpoints e credenciais.
- O validador host-owned também era substituído por `InvalidDataException("schema_invalid")`, descartando sua causa. Cancelamentos perdiam a exceção original.
- O kernel mantinha os outputs publicados e os steps anteriores, mas o resultado de falha continha apenas mensagem/tipo; duas conversões de validação também descartavam inner exceptions.
- `IFlowCompletion.CompleteAsync` retorna somente uma string. A observação de inferência tinha tokens/custo/modelo observado sempre nulos. OpenAI e LM Studio descartavam o usage da resposta; SystemOne retornava usage apenas depois de validar todas as respostas.
- Não havia captura opt-in de respostas para o consumidor do grafo.

A ocorrência original com Sonnet 5.5 **não foi atribuída a um defeito do Cortex**. O ponto de integração aceita um adapter da aplicação, que pode chamar o provedor, consumir tokens e depois falhar em parsing, schema ou validação de evidências antes de retornar. O Cortex também podia descartar a causa nesse caminho. Sem a exceção/resposta original preservada, não é possível determinar retroativamente qual validação falhou. Não foram reexecutados modelos pagos nem alterada a aplicação consumidora.

## Contratos aditivos

As assinaturas existentes, os constructors/deconstructors dos records públicos, a interface `IFlowCompletion`, os tipos das exceções sanitizadas e a política de retries permanecem disponíveis. Não há novo runner, scheduler, fallback ou engine. Nodes concluídos mantêm seus outputs mesmo quando outro node falha.

- `TypedStep.Diagnostic` e `TypedRunResult.Failure` identificam node, port quando conhecido, categoria, origem, código, tipos das exceções e status HTTP quando disponível.
- `FlowFailureDiagnostic.Exception` conserva o objeto original em memória: mensagem, inner exceptions, stack e Data continuam acessíveis ao código confiável. `ThrowOriginal()` relança preservando o stack. O objeto não participa do JSON via System.Text.Json nem do ToString do diagnóstico.
- `FlowDiagnostics.GetFailure(exception)` recupera a causa associada a uma exceção sanitizada. As associações são process-local e usam ConditionalWeakTable; não alteram Exception.Data nem acrescentam segredos a InnerException/ToString da exceção pública.
- Exceções que antes escapavam e cancelamentos continuam escapando. `FlowDiagnostics.GetRunResult(exception)` expõe o resultado parcial, com steps concluídos, node interrompido, outputs e tentativas de inferência disponíveis.
- `TypedRunResult.InferenceAttempts` inclui uso e resultado de cada tentativa, inclusive correções inválidas antes de uma resposta válida. `AttemptId` coincide com o emitido pela observação host-bound, sem duplicar chamadas no router.
- `InferenceAttemptObservation` conserva os campos anteriores e passa a incluir Usage e Failure. ForRuntime continua emitindo `AdapterReturnedPendingValidation`: a validação posterior aparece no resultado do runner, correlacionado pelo AttemptId. O evento anterior não é reescrito como sucesso.

As categorias são Provider, Parsing, Validation, Runtime, Cancellation e Unknown. A origem distingue Runtime, HostNode, Adapter e HostValidation. Uma InvalidOperationException arbitrária do adapter permanece Unknown/Adapter: não é evidência de defeito do Cortex ou do provedor. A classificação não faz diagnóstico causal além da informação disponível.

Exemplo de consumo:

```csharp
var result = await TypedFlowRunner.RunAsync(flow, message, completion, token);
if (!result.Success && result.Failure is { } failure)
{
    // Envelope estruturado; trate Exception como informação privada em memória.
    var node = failure.NodeId;
    var cause = failure.Exception;
    var attempts = result.InferenceAttempts;
}
// Nos caminhos que lançam exceção, use GetFailure(error) e GetRunResult(error).
```

## Usage antes de validar

O novo `request.Diagnostics` é um canal host-only, excluído do JSON do request. Adapters antigos continuam funcionando; valores desconhecidos continuam null, nunca zero presumido. O canal não retém prompt, inputs, schema ou request original.

O adapter deve informar contadores antes de executar parsing/validação da aplicação que pode falhar:

```csharp
request.Diagnostics?.ProtectSensitiveValue(apiKey);
request.Diagnostics?.CaptureResponse(responseBody); // só retém algo quando habilitado
request.Diagnostics?.ReportUsage(new CompletionUsage(
    InputTokens: inputTokens,
    OutputTokens: outputTokens,
    CostUsd: effectiveCostUsd, // apenas se realmente conhecido pelo host
    ObservedModel: returnedModel,
    ObservedProvider: "anthropic"));
// Só depois: parsing do perfil, validação de schema/evidências etc.
```

`ReportUsage` recebe snapshots cumulativos de uma invocação. Atualizações parciais se combinam; reportar o mesmo total duas vezes não o soma duas vezes. Se um adapter realiza vários requests internamente, deve reportar o total acumulado ou expor as chamadas separadamente. O Cortex não inventa cobrança, tarifa, modelo efetivo ou contagem extraindo tokens de textos arbitrários.

Os adapters de flow OpenAI, SystemOne/Jev/Laya e LM Studio foram instrumentados para informar o usage disponível antes de validar status/conteúdo/answers. OpenAI e SystemOne protegem também a credencial conhecida contra eco no corpo capturado. Nenhum preço de provedor foi adicionado: custo em USD vem do adapter/host e deve usar a tarifa efetivamente contratada. JSON irrecuperável ou uma desconexão antes do usage continuam com métricas desconhecidas.

## Captura opcional e privacidade

Padrão: desligada. Para grafos, use o overload com opções:

```csharp
var options = new FlowDiagnosticOptions
{
    CaptureRawResponses = true,
    MaxResponseCharacters = 4096,
    RedactResponse = body => RedactApplicationSensitiveData(body)
};
var result = await TypedFlowRunner.RunAsync(flow, message, completion, options, token);
```

Sem função de redação, a configuração é rejeitada antes da execução. A função é definida pelo host, deve remover dados pessoais/segredos específicos do domínio e pode retornar null para descartar o corpo inteiro. Uma falha da função descarta a captura e registra `redaction_failed`, sem mudar o resultado da inferência ou iniciar retry. Em chamadas host-bound avulsas, `HostInferenceProfile.DiagnosticOptions` configura o mesmo mecanismo; em grafos prevalece a configuração da execução.

Além da função do host, há remoção de valores registrados por `ProtectSensitiveValue`, padrões Bearer/Basic, chaves sk-* e campos JSON comuns de credenciais. Isso é defesa adicional, não detecção universal de dados pessoais ou segredos. Adapters customizados devem registrar suas credenciais e aplicar a política de dados do produto.

A captura conserva no máximo o limite configurado (1–65.536 caracteres, padrão 4.096), depois da redação, e indica captured/truncated/discarded/redaction_failed/not_available/disabled. Retém a primeira resposta informada por invocação, priorizando o corpo de transporte quando instrumentado; caso contrário, dispõe apenas da string retornada pelo adapter. Erros HTTP também podem ter o corpo capturado no opt-in. Não são capturados headers nem requests, não há arquivo de log automático e nenhuma chamada é refeita para obter diagnóstico.

`CapturedResponse` e Exception são excluídos do JSON System.Text.Json e dos ToString dos novos envelopes/observações. O acesso explícito em memória continua sendo privilegiado: não envie Exception.ToString, Exception.Data ou CapturedResponse diretamente a logs públicos. Campos legados como TypedStep.Error e outputs do grafo mantêm seu comportamento anterior e exigem a política de exposição já aplicável no consumidor.

## Durabilidade e limites

Snapshots passam a guardar tentativas/usage e envelopes seguros de falha. O decoder aceita snapshots anteriores sem esses campos. A extensão usa campos opcionais do formato existente; runtimes antigos com decoder estrito não devem ler snapshots produzidos pela versão nova sem atualização. Exceções originais e corpos capturados não atravessam serialização/restart; o status de captura descreve o ocorrido originalmente e não garante que o corpo esteja presente após recarregar.

Não existe ledger financeiro novo. Uma falha de processo antes de salvar o snapshot/observação pode perder os últimos dados process-local. Cancellation/timeout após invocação não provam ausência de consumo. Adapters sem instrumentação não revelam métricas descartadas internamente, e respostas perdidas na execução antiga não podem ser reconstruídas. O consumidor precisa atualizar a referência do Cortex e instrumentar seu adapter Claude para aproveitar os contadores/custo; nenhuma modificação foi feita nele nesta tarefa.

## Validação

Os testes novos usam delegates em memória e HttpMessageHandler com respostas fixas, sem chamadas pagas. Cobrem provider/HTTP, parsing, schema, falha de adapter consumidor após uso, handler, cancelamento, outputs anteriores, correção limitada, snapshots antigos/novos, correlação sem duplicação, isolamento concorrente e captura/redação segura.

Validação executada com .NET SDK 10.0.402, sem warnings de compilação nos projetos testados:

| Conjunto | Resultado |
|---|---|
| Observabilidade nova + regressões HostBoundFlowCompletion | 48 passaram, 0 falharam |
| Cortex.Inference.Tests | 55 passaram, 0 falharam |
| Cortex.Core.Tests offline ampliado | 816 passaram, 3 falharam (819 casos) |
| Controle dos três casos no HEAD anterior `11315b2`, copiado para /private/tmp | As mesmas 3 falhas reproduzidas |
| git diff --check | Sem erros |

Falhas anteriores reproduzidas, não corrigidas nesta tarefa:

- `FileArtifactStoreTests.SecondWriterCannotOpen`: tenta iniciar `pwsh`, ausente no computador.
- `ShadowInboxTests.Exclusive_writer_prevents_a_second_bootstrap_from_racing_dedupe`: esperava IOException para segundo writer, mas não houve exceção neste ambiente.
- `ShadowOutboundTests.IncompleteJournalFailsClosedAndCannotHaveTwoWriters`: mesma expectativa de exclusão de writer, também sem IOException neste ambiente.

A primeira tentativa de compilar a cópia de controle dentro do sandbox falhou por acesso negado ao socket/pipe local do MSBuild; foi encerrada. O controle foi então executado com a mesma permissão local utilizada na suíte principal. Não foram instalados PowerShell, frameworks ou dependências novas no projeto. Restore usou os pacotes já declarados.

Comandos executados no repositório:

```sh
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~FailureObservabilityTests|FullyQualifiedName~NativeResponseObservabilityTests|FullyQualifiedName~HostBoundFlowCompletionTests'
dotnet test tests/Cortex.Inference.Tests/Cortex.Inference.Tests.csproj --no-restore
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName!~Live&FullyQualifiedName!~DetectionStrategies&FullyQualifiedName!~BoundarySemanticCorpusTests'
git diff --check
```

Relatórios desta execução em `/private/tmp/cortex-observability-results/`: `focused-final.trx`, `inference-final.trx`, `core-offline-final.trx`, `baseline-environment.trx`. A suíte offline filtrada não representa todos os testes da solução; não foram executados testes pagos/live ou integração com infraestrutura externa.
