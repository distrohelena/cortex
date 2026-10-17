# Flux Studio: frontend na rede e múltiplos backends da conta

Status: proposta revisada para feedback da Helena, não autorização de implementação pública. Esta revisão substitui a premissa anterior de execução obrigatória no dispositivo que renderiza a UI. Comportamento aprovado: uma pessoa tem vários backends; Run usa automaticamente o primeiro backend autorizado, alcançável e livre. Não existe seletor, preferência global ou afinidade por vídeo. Status é somente indicação: uma admissão atômica decide se o backend está livre.

## Resultado solicitado

O frontend editorial vem do domínio Flux Studio na rede SweetSquare. Cada backend instalado contém somente o backend com Cortex. A biblioteca privada continua no serviço da rede `fluxstudio/fluxstudio`; cada executor instalado registra `fluxstudio/cortex`. Um backend pode estar em outro nó autorizado pela pessoa. O primeiro comando prepara apenas o brief inline e devolve inspeção com `completedNodeId: brief` e `resumable: false`.

## Identidade da instância

Toda tentativa/admissão precisa identificar `nodeIdentity`, `installationId`, `packageId`, `packageVersion`, `appId: fluxstudio` e `serviceId: cortex`. `appFamilyId` ou nome de domínio não distinguem duas instalações do mesmo app. Uma atualização muda versão/hash; não deve ser tratada como outra instalação sem definição explícita do lifecycle.

Descoberta apresenta somente instâncias autorizadas para a conta selecionada, com publisher/interface verificados, operações/capacidades e status observado. O frontend não envia URL, comando, caminho de executável, conta ou identidade de consumidor. Ele pede uma operação pelo broker; o shell faz despacho automático. Não há preferência persistida. Cada novo Run considera a ordem dos candidatos verificados naquele momento.

## Canal proposto para aprovação

1. Interface proposta: `{ contractDomain: 'fluxstudio', interfaceId: 'cortex.brief', majorVersion: 1 }`. O frontend usa `ssn.invokeCapability({ interface, operationId: 'try_prepare_brief', input })`.
2. `FrontendSandboxBridgeService.handleMessage` confere a janela; o shell atribui `DomainPageHostViewModel.capabilityFrameId`. `HomeViewController.handleCapabilityInvocation` deriva consumidor da `DomainServiceAuthority`, conta selecionada e chave ativa.
3. O broker verifica consumed/provided operation e envelope com `ServiceInterfaceContractVerifier.parseAndVerifyAsync`: Blake2/32, ML-DSA e dono atual do domínio. A operação usa execução instalada no backend autorizado admitido automaticamente.
4. O broker percorre os backends elegíveis da conta em ordem e chama try_prepare_brief({requestId,request}). Apenas busy/unavailable com admitted:false permite tentar o próximo. Accepted fixa a referência exata de nó/instalação e requestId; conflict, timeout ou resposta incerta também ficam presos à instância tentada, sem replay silencioso.
5. O transporte existente envia à identidade exata do nó escolhido. Routers podem transportar até esse destino, mas não escolher outro executor. O campo `RoutedServiceInteractionRequestPacket.DestinationAddress` já expressa destino exato. `RoutedServiceInteractionQueryClient.TryQueryPeer` não deve ser usado diretamente para essa chamada de usuário: reorigina como nó e remove provas do caller herdadas.
6. No backend que admitiu a chamada, ingresso valida vínculo da conta àquela instalação, publisher/interface e a chamada autenticada. `ApplicationManager.ExecuteServiceInteraction` verifica enrollment vigente da conta/dispositivo; `RequesterAccount` fica disponível ao runtime.
7. Instalação reutiliza `ApplicationManager.InstallPackage` → `JavaScriptServiceRuntimeInstallAdapter.Install` → registerServices → services.register → `JavaScriptHostedRuntimeService`. Execução try_prepare_brief vira tryPrepareBrief com arguments_json: [{requestId,request:input}]. O método autentica/valida, reserva o slot de forma atômica e retorna rapidamente; helper detached captura conta/input e não chama host APIs. Get_brief_state({requestId}) consulta somente esse job no backend admitido. JavaScriptHostedServiceHandle mantém a serialização e limpeza de contexto.
8. O helper fixo conclui brief; o broker verifica schema/tamanho; o shell entrega somente ao mesmo frame/conta/seleção de backend que iniciaram a chamada. O frontend salva a inspeção na biblioteca criptografada da rede.

O significado de `localExecutionRequired` precisa ser aprovado para este contrato: execução no runtime instalado do backend que admitiu a chamada, que pode estar em outro nó da pessoa; não equivaler silenciosamente a execução no dispositivo da UI. As operações interativas existentes mantêm seu comportamento.

## Provas diferentes

- Conta: `RequesterAccount` resulta da cadeia certificada e enrollment do dispositivo. Display label, signing-key hash e node fingerprint não são a conta.
- Publisher: assinatura do contrato e prova da publicação ligam domínio/app/service aos bytes/versão do pacote instalado. Hash sem assinatura/proveniência só prova integridade.
- Vínculo do backend: prova da conta autoriza aquela referência de nó/instalação/operação. `DomainServiceAuthorizationArtifactVerifier.Verify` autoriza owner de domínio → serviço/nó; não prova usuário → instalação. Runner claims também não fornecem esse vínculo.
- Consumidor: autoridade da rota identifica frontend Flux montado, enquanto isolamento dos guests mantém o shell como autoridade de origem. Campos consumidor enviados pelo guest não são prova.

Não foi encontrado registry de backends da conta nesta base. A implementação precisa de um mecanismo aprovado de vínculo, descoberta/status e revogação. Um registro node-signed da instalação e uma autorização account-signed são a proposta mínima, sujeita a feedback; local de armazenamento e emissão ainda precisam ser escolhidos conscientemente.

## Compatibilidade observada

`ProviderBindingStore` chaveia por signing-key hash + consumidor + interface, não por conta/instância. `ProviderBinding` guarda provider identity/appFamilyId e não guarda nó/install. `ProviderResolver.resolveBoundProvider` usa find por appFamilyId/identity; dois backends Flux idênticos em nós diferentes seriam confundidos. Um binding antigo incompleto não pode selecionar o primeiro backend novo.

O broker atual descobre somente provedores de domínio para consumidor de domínio; public execution rejeita instalado e instalado interativo exige frontend. Estender o mesmo broker com provedores de backend autorizado, sem segundo bridge específico do Flux e sem frontend instalado. O AUTO não usa ProviderBindingStore como afinidade de backend. Bindings existentes de outros serviços permanecem separados; uma admissão guarda somente o destino daquela execução.

RuntimeManifest do nó é backend-only; o desktop Musicshare usa outro contrato que exige UI, declarations SHA-256 e interactiveFrontendEntryPath. Não converter SHA-256 em Blake2 por troca de marcador. O caminho backend usa o runtime do nó e o contrato assinado Blake2 do webhub.

`RuntimeInstallPackageManifest.PackageHash` só nomeia cache; não verifica arquivos/publisher. A instalação precisa conferir publicação e bytes antes de `InstallPackage` e limitar registrations a `fluxstudio/cortex`, sem substituir `fluxstudio/fluxstudio` ou `core/identity`. Persistir somente instalação verificada/ativada e revalidar ao reativar.

`BackendProgram.BuildHostedAppServiceScopes` anuncia todos os serviços não-core. Instâncias privadas da conta não devem se tornar provedores genéricos de qualquer domínio. Descoberta/account directory e transporte ao nó escolhido precisam distinguir a instalação autorizada de publicidade genérica e impedir fallback de executor. Isso não proíbe transporte pela rede até um backend remoto autorizado.

Guests legados com allow-same-origin acessam o shell. Não expor novo canal antes de aprovar isolamento e compatibilidade das pontes existentes. Args.Source ou isolamento apenas do Flux não comprovam que outra tela não iniciou a chamada.

## Testes mínimos após aprovação

| Fronteira | Evidência exigida |
|---|---|
| Instalação real | Pacote sem UI registra cortex, status ready e doc service intacto. |
| Identidade instalada | Bytes alterados/publisher diferente/registration fora do binding falham antes de ativação. |
| Duas instâncias iguais | Dois nós com mesmo appFamilyId/pacote/service aparecem separados; A ocupado sem admissão permite B, e accepted B nunca executa A. |
| Mesma conta em outro dispositivo | Lista backends da conta, não somente instalações do dispositivo que renderiza UI. |
| Vínculo inválido/revogado | Instância de outra conta ou grant revogado nunca chega ao helper. |
| Dois Runs concorrentes | Reserva de slot é atômica; apenas um request distinto é admitido no mesmo backend livre. |
| Recusa antes de executar | Busy/unavailable com admitted:false pula candidato; sem esse marcador não tenta outro. |
| Roteamento | DestinationAddress permanece do backend que admitiu a chamada; prova da conta original chega ao host terminal. |
| Contexto | Conta/app/frame/seleção não atravessam erro, concorrência, troca de conta ou navegação. |
| Isolamento/compatibilidade | Outro guest não acessa shell/forja Flux; Musicshare usa somente bridges autorizadas existentes. |
| Primeiro brief | Após aprovação do adaptador: brief concluído, resumable false, zero inferência. |

## Verificação já executada

Pacote real de revisão: 356 arquivos sem UI/wwwroot, instalado por ApplicationManager em temp próprio, `fluxstudio/cortex.get_status` ready e doc sentinel preservado. Chamada node-only sem conta a prepare_brief foi recusada antes do helper. Nenhum brief válido/modelo/signer de produção. Evidência: `C:/dev/cortex/.local/sweetsquare-backend-install-review/run-73d3a91ee79646449ccb4cc3a954cd59/result.json`.

Essa verificação não comprova vínculo de conta, descoberta multi-backend, transporte remoto escolhido nem isolamento da UI. Esses pontos continuam sem implementação.

## Feedback ainda necessário

AUTO/primeiro disponível e livre já foi definido; falta aprovar mecanismo de vínculo conta → backend e proveniência de publisher; confirmar fronteira do canal assinado preservando conta/origem até a instância exata; aprovar isolamento dos guests e correção separada do adaptador de grafo. O frontend continua na rede, backend continua instalado, sem app Cortex separado, sem UI local e sem publicação/inferência/chaves reais nesta revisão.

Groundwork puro já implementado: AccountBackendDispatcherService filtra conta/interface/hash/operação/ready/free, usa admissão atômica, exige admitted:false para skip e fixa accepted/conflict/indeterminate. Dez testes novos e cinco testes existentes passaram; typecheck dos novos tipos/serviço passou. Não há discovery, transporte, grant/publisher verifier ou handler/UI de produção conectado.


Artifact de admissão mais recente também foi verificado: 357 arquivos, métodos get_status/try_prepare_brief/get_brief_state, guarda sem conta, unavailable/admitted:false e state_lost sob contexto de teste sintético (não prova cadeia real). Evidence: C:/dev/cortex/.local/sweetsquare-backend-install-review/run-87873d32980e47719d15f091491cd323/result.json. Estado ready só indica runtime carregado; eligibility requer availability:free, busy:false e operação disponível.


Admissão detached também comprovada pelo runtime real com runner fixture: accepted, status busy em 2 ms, poll running/completed, duplicate sem nova execução e state_lost para outra conta. EntryPoint extra existe somente no harness; artifact/package real e helper não mudaram. Evidência .local/sweetsquare-backend-admission-fixture/run-f758d3ccfcdc41ceb6d560df821af447/result.json; prova de conta continua sintética, sem enrollment real.
