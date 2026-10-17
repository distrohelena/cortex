# Flux Account Backend AUTO Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. Execution follows the two-agent arrangement already authorized; do not spawn extra agents or commit without authorization.

**Goal:** Run automaticamente no primeiro backend da conta que esteja disponível e livre, fixando a execução nessa instância depois da admissão.

**Architecture:** Frontend Flux permanece no domínio SweetSquare; cada pacote instalado contém só backend/Cortex e registra fluxstudio/cortex. Reutilizar broker/contratos e canal assinado existentes, com descoberta verificada da conta e admissão atômica rápida por instância. Backend pode estar em outro nó da pessoa; transporte não pode escolher outro executor após accepted ou resultado incerto.

**Tech Stack:** Vue/TypeScript/Vitest; SweetSquare C# ApplicationManager; Node stdio-jsonrpc; ML-DSA/Blake2 contratos existentes; helper .NET empacotado.

**Spec:** ../specs/2026-10-07-flux-local-runtime-decision.md

## Global Constraints

- AUTO/primeiro elegível e livre já aprovado; nenhum seletor/afinidade/preferência por vídeo ou global.
- Publisher-install trust, source-of-truth account-backend authorization e isolamento global ainda precisam de feedback. Tarefas nessas fronteiras não estão autorizadas para implementação pública.
- Wire: get_status, try_prepare_brief({requestId,request}), get_brief_state({requestId}); sem mapping escondido para prepare_brief.
- Apenas busy/unavailable com admitted:false permite próximo candidato; accepted/conflict/indeterminate mantém backend+requestId.
- requestId é UUID canônico para correlação/dedupe; operationId é ação. Nunca substituir conta por signing-key hash, display label ou nó.
- Primeiro resultado é inspeção brief/completedNodeId:brief/resumable:false, sem inferência, vault, media, API key ou continue.
- Não executar helper válido antes de autorização da correção do adaptador; capability indisponível enquanto houver bloqueio conhecido.
- Registrar backend não pode substituir doc service fluxstudio/fluxstudio; não publicar executor como provider genérico de outros usuários.
- Preservar todos os edits de outras pessoas; nenhum deployment ou commit nesta etapa.

## Review Focus

- Status idle envelhece entre discovery e Run: admissão atômica, não status, decide capacidade (Task 2/5).
- Resposta de admissão se perde depois de execução iniciar: reconciliar no mesmo backend/requestId, nunca replay (Task 1/5/6).
- Um usuário aparece com outra chave/dispositivo: descoberta usa conta verificada e grants atuais, não somente signer local (Task 3).
- Dois nós anunciam o mesmo app/pacote: referência completa distingue instâncias; nunca resolver por appFamilyId (Task 1/3/5).
- Backend reinicia e perde job Map volátil: state_lost permanece explícito e não autoriza retry automático (Task 2/6).

## Task 1: Pure automatic dispatcher — implemented and verified

**Files:** webhub/src/capabilities/account-backend-dispatcher.service.ts; account-backend-admission-client.ts; model/backend-instance-reference.ts; model/account-backend-candidate.ts; model/account-backend-dispatch.ts; __tests__/account-backend-dispatcher.service.spec.ts.

**Interfaces:** dispatchAsync(request:AccountBackendDispatchRequest,candidates:readonly AccountBackendCandidate[],admissionClient:AccountBackendAdmissionClient):Promise<AccountBackendDispatchResult>. Candidate data is already proof-verified by Task 3; this class is not an authorization verifier.

- [x] Write tests for first-ready-free, two nodes/same package, busy/unavailable refusal, foreign account/hash/interface exclusion, timeout correlation, malformed refusal and request conflict.
- [x] Observe missing implementation and later admission DTO failures.
- [x] Implement pure scheduling only, with no network/persistence/UI.
- [x] Run Vitest: 10 new + 5 existing resolver/binding tests passed. Standalone tsc for new files passed.
- [ ] Root review these files before connecting any production boundary.

Exact instance fields: nodeIdentity:string, installationId:string, packageId:string, packageVersion:number, appId:string, serviceId:string. Candidate fields: accountId:string, backend:BackendInstanceReference, status:string, availability:free|busy|unavailable, busy:boolean, interfaces:[interface,contractHash,operationIds]. Request fields: accountId,requestId,operationId,interface,contractHash,input. Admission uses admission/admitted/requestId; result uses status/backend/requestId/error.

Run: npm run test:unit -- src/capabilities/__tests__/account-backend-dispatcher.service.spec.ts src/capabilities/__tests__/provider-resolver.service.spec.ts src/capabilities/__tests__/provider-binding-store.spec.ts

## Task 2: Per-instance admission — backend worker owns

**Files:** C:/dev/depois-do-slogan/src/FluxStudio.Device/service/main.mjs and its focused JS tests/staging scripts; do not edit from SweetSquare agent.

**Interfaces:** existing Node runtime calls tryPrepareBrief({requestId,request}) and getBriefState({requestId}). Admission response {requestId,admission:accepted|busy|unavailable|conflict,admitted:boolean,state:running|completed|failed|null,duplicate:boolean}. Poll {requestId,state:running|completed|failed|state_lost,result,error,volatile:true}.

- [ ] Test two requests racing an idle slot: one accepted, one busy/admitted:false; no helper double-start.
- [ ] Test same account+requestId+normalized input returns existing job without execution; different input yields conflict; another account cannot inspect it.
- [ ] Capture verified userId and validated input under invocationLock; set slot without an await between check/set; detach helper and return immediately. Background never calls host API.
- [ ] Test status/poll stay fast during injected runner work, slot releases on completion/failure, and state_lost after volatile restart is explicit.
- [ ] Keep real runner capability unavailable until adapter correction approved. Do not execute valid helper as part of this task yet.

JavaScriptHostedServiceHandle.InvokeMethodAsJson holds invocationLock across the entire awaited method. Do not relax that lock or start ambient host calls from detached work.

## Task 3: Verified account-backend discovery — architecture gate

**Proposed files:** capabilities/account-backend-directory.service.ts, model/account-backend-evidence.ts, __tests__/account-backend-directory.service.spec.ts; backend account-link verifier/store files only after root approves proof/source-of-truth.

**Interfaces:** AccountBackendDirectoryService.listAuthorizedAsync(accountId:string):Promise<readonly AccountBackendCandidate[]>; verified evidence must link the exact BackendInstanceReference to account and current publisher/package/contract identities. No untrusted authorized:true boolean constitutes proof.

- [ ] Before code, approve the source-of-truth and issuance/revocation mechanism for account→backend linking. Existing DomainServiceAuthorizationArtifact is domain-owner→node/service, not user→installation; runner claims are not account authorization.
- [ ] Specify proof record fields for approval: accountId, nodeIdentity, installationId, packageId, packageVersion, packageHash, appId, serviceId, interface/operation scope, issuerDeviceKeyId, issuedAt, expiresAt, signature; keep publisher proof separate. Storage location is not silently chosen here.
- [ ] Tests reject foreign account, expired/revoked account link, wrong node/install, modified descriptor, mismatched publisher and unsupported operation.
- [ ] Tests expose two distinct nodes and two installation IDs without collapsing same appFamilyId. A different enrolled device of the same account can discover the same authorized instances.
- [ ] Emit ready/busy/capabilities observed metadata as advisory snapshots; preserve candidate order supplied by verified discovery rather than inventing ranking.

ProviderBindingStore currently keys by device signing-key hash+consumer/interface and ProviderResolver finds appFamilyId/identity. Do not use those persisted choices as AUTO backend affinity. Existing domain/interactive bindings remain unaffected.

## Task 4: Verified install and per-instance registry — architecture gate

**Proposed files:** backend/ssn.application/application/VerifiedBackendInstallBinding.cs; AccountBackendInstanceRegistry.cs; changes limited to ApplicationManager.cs and JavaScriptServiceRuntimeInstallAdapter.cs registration gate; backend/nucleus.network.tests/AccountBackendInstanceInstallationTests.cs. Bootstrap persistence integration in ssn.node only after binding/source approved.

**Interfaces:** ApplicationManager.InstallVerifiedBackendPackage(manifest:RuntimeInstallPackageManifest,binding:VerifiedBackendInstallBinding):RuntimeInstallRecord; registry stores the exact node/installation/package/version/service reference and authenticated publication/account-link evidence. RuntimeInstallPackageManifest remains backend-only.

- [ ] Approve publisher-install proof tied to actual package bytes. PackageHash currently only names a cache; interface signature alone does not sign executables.
- [ ] Tests corrupt one byte/hash or change publisher/app/service and assert failure before process start/registration. Require approved source evidence before InstallPackage.
- [ ] Validate each published registration against binding BEFORE RegisterOrReplaceService. Require fluxstudio/cortex and preserve fluxstudio/fluxstudio/core services.
- [ ] Test status/install persistence and activation failures with causes; no false ready or partially registered installation.
- [ ] Test private backend descriptors are discoverable to the authorized account but do not become arbitrary provider candidates for other accounts. Define advertisement/runner policy from approved account-link design, not from generic non-core listing.

Existing actual-runtime harness already proves backend-only install/get_status and separate doc registry; it does not prove these new trust boundaries.

## Task 5: Exact-instance transport and broker integration — architecture gate

**Proposed files:** capabilities/account-backend-admission-client.service.ts; capability-broker.service.ts dependency extension; controllers/home-view.controller.ts authority context wiring; relevant node ingress handler and account-backend transport tests. Generated C#/TypeScript protocol files are changed only through their source/converter when necessary.

**Interfaces:** tryAdmitAsync(candidate:AccountBackendCandidate,request:AccountBackendDispatchRequest):Promise<AccountBackendAdmissionResponse>; getBriefStateAsync(backend:BackendInstanceReference,requestId:string,accountId:string):Promise<BackendBriefState>. BackendBriefState preserves worker DTO state/result/error/volatile. Broker stores only an in-flight execution binding, not user preference.

- [ ] Approve authenticated transport boundary for account and shell-attested consumer origin; arbitrary consumer payload fields are not trusted context.
- [ ] Test automatic candidate A busy/admitted:false → B accepted; B requestId/instance becomes immutable. Offline may be excluded before attempt; only certain non-admission permits retry after attempt.
- [ ] Use exact DestinationAddress for the chosen backend. Preserve original selected-account/device proof to terminal ApplicationManager.TryCreateInteractionContext; do not call TryQueryPeer as a user-originated request because it reoriginates as node and strips caller proof.
- [ ] Tests final host independently verifies enrolled account; foreign account/node labels/changed identity cannot execute. Distinguish original account device and network transport node.
- [ ] Disable generic executor failover: response loss, accepted/uncertain state, conflict, service removed after admission and state_lost never invoke another backend. Routing intermediaries may carry the request to the exact chosen node; they cannot choose another executor.
- [ ] Broker reuses parseAndVerifyAsync/schema validation and exact try_prepare_brief/get_brief_state signed operations. No provider iframe, frontend from install or hidden prepare_brief mapping.
- [ ] Test polling always targets admitted backend+requestId, including busy state; never pass get_brief_state through first-free scheduling.

## Task 6: Consumer isolation and current-frame authority — architecture gate

**Proposed files:** domain-page-host.service.ts; frontend-sandbox.ts; domain-page.component.vue; frontend-sandbox-bridge.service.ts; home-view.controller.ts; targeted browser/bridge tests. Root owns editorial frontend/doc integration; coordinate callback shape before edits.

- [ ] Approve opaque hosted-frame isolation with compatibility bridges. Do not switch all screens to restrictive private CSP/bootstrap as an incidental change.
- [ ] Test all network guests cannot access parent runtime/keys/frames; iframe source is checked and shell attributes frame/consumer identity.
- [ ] Preserve Musicshare typed/allowlisted local library operations: read/add/remove folder/scan/apply matches/apply publications/publish track. Rewrite active package identity and check approved capability; raw forwarded payload cannot manufacture new backend context.
- [ ] Preserve existing package install review route; installation only follows trusted review/consent and validated publisher binding, never arbitrary guest bytes/commands.
- [ ] Capture account/frame/signing identity before async discovery and suppress stale dispatch/delivery after navigation/account change. Preserve already admitted job binding for reconciliation without replay.
- [ ] Root Run flow displays automatic admission/busy/unavailable/indeterminate feedback and saves only validated completed brief inspection to studio_document on the network.

## Final first-brief integration gate

- [ ] After graph-adapter correction is explicitly approved, use owned test backends and synthetic account/publisher fixtures: one busy instance, one free instance, actual brief helper completes on the admitted backend only.
- [ ] Assert inspection completedNodeId brief/resumable false, no calls to model/vault/media APIs and network doc service unchanged.
- [ ] Run focused tests above plus existing broker/contract/bridge suites. Keep existing known failures visible and ask root before fixing unexpected product failures.
- [ ] Review source diffs; report admitted instance/requestId, uncertain retry behavior and volatile restart limitation. No publishing/commits without authorization.

## Current handoff

Task 1 is implemented and type-checked with 15 focused tests passed. Task 2 is authorized and owned by package worker. Tasks 3–6 are proposal interfaces/test scopes, blocked on the stated architecture choices; no production discovery/transports/grants/handlers/isolated screens have been connected. User AUTO feedback supersedes both pinned UI-device and manual-selector plans.


Verificação adicional do artifact admission-review: 357 files, get_status ready, try_prepare_brief e get_brief_state sem conta recusados; contexto trusted SINTÉTICO retorna unavailable/admitted:false e state_lost sem iniciar helper. Evidence realAccountProofVerified:false em C:/dev/cortex/.local/sweetsquare-backend-install-review/run-87873d32980e47719d15f091491cd323/result.json. A fixture textual requestId foi inicialmente rejeitada; corrigida para UUID canônico antes do PASS. Não houve accepted/run válido. Dispatcher também exige availability:free para não confundir ready/busy:false com capability bloqueada. Typecheck inclui agora o teste novo e passou.


Native concurrency fixture PASS: real ApplicationManager + packaged brief-service + TEST-only entrypoint/2 s synthetic runner; accepted, get_status busy in 2 ms, poll running→completed, duplicate reused result, foreign account state_lost. No real helper/graph/model or package capability change. Evidence: C:/dev/cortex/.local/sweetsquare-backend-admission-fixture/run-f758d3ccfcdc41ceb6d560df821af447/result.json. Existing registry only supports one implementation per app/service/node and replaces the prior binding; simultaneous cortex installations on one node require explicit instance routing design rather than being assumed from the pure identity model.
