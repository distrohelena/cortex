# Graphiti write gap: diagnóstico local

Status: causa localizada e correção implementada somente no checkout isolado; sem deploy ou reativação. A evidência inicial abaixo descreve o estado anterior à correção.

## Evidência

- A composição em `src/Filomena.App/NativeShadowCommand.cs` passa `GraphitiMemoryReader` tanto a `ObservedShadowRun` quanto a `ObservedLiveRun`.
- `src/Filomena.Memory/GraphitiReadClient.cs` expõe somente `get_entity_neighborhood` e `search_memory_facts`. `src/Filomena.Core/Memory/MemoryContracts.cs` define `IMemoryReader`, não um writer de ingestão. Os nomes de efeitos de escrita no validador do graph não constituem implementação de gravação.
- No plugin, `index.ts` liga o recorder aos hooks `message_received` e `message_sent`. `message-lifecycle.ts` contém normalização de proveniência, identidade determinística, autorização SpiceDB e chamada Graphiti `add_memory`.
- O caminho de inbound protegido em OpenClaw `extensions/whatsapp/src/inbound/message-delivery.ts` termina antes de hooks/dispatch legados. Isso é necessário para isolamento; remover esse bloqueio não é uma correção aceitável de memória.
- `autoCapture=true` configura o plugin, mas não conecta eventos da observação nativa ao recorder.
- A auditoria read-only anterior do banco encontrou 54 episódios/55 relações no namespace do grupo, último episódio em 2026-09-04, nenhum episódio/fato desde 24/09. Não são testes de extração novos.
- O recorder exige SpiceDB. Configuração aponta para localhost:50051; inspeção de listeners não encontrou essa porta. É uma indisponibilidade atual constatada, não prova da causa histórica de toda captura ausente.
- `addEpisode` é uma admissão assíncrona: retornar queued não significa que a memória já foi extraída/persistida/recallable. O recorder atual registra captura após admissão e tem autorização de fragmento posterior; não reaproveitar esses logs como recibo de commit.

## Causa principal

A migração para native observation preservou recall, mas não compôs a escrita fora do lifecycle legado isolado. O reader .NET e o recorder legado são caminhos separados. Não há fundamento para dizer que reativar uma flag resolve.

## Verificação executada

- Vitest local: `message-lifecycle.test.ts`, `passive-capture.test.ts`, `outbound-capture.test.ts`: 18/18 passaram. Fakes/unidades, sem Graphiti live.
- .NET contra binários existentes (`--no-build --no-restore`), filtros GraphitiMemoryTests, GraphitiContractDiagnosticTests, ShadowGraphitiProbeTests: 55 passaram, 2 pulados, 0 falhas. Não representa rebuild nem cobertura de uma escrita inexistente.
- Nenhuma chamada de modelo, ingestão de histórico, mudança de credencial, restart ou escrita live nesta investigação. Código de produto inalterado.

## Limite de escopo descoberto

Não é possível simplesmente injetar um writer existente no runtime: esse contrato não está implementado no host novo. É necessário aprovar o contrato mínimo de ingestão, autorização, dedupe e resultado antes de modificar a fronteira. O desenho proposto está em `superpowers/specs/2026-09-25-native-memory-write-design.md`.

## Correção local após aprovação

- Host nativo faz staging durável antes de participação/cursor, incluindo futuros resultados de silêncio. Não reabre hooks legados.
- Surface HTTP host-owned, opt-in, com autenticação Gateway e capability separada. Shadow sem writer authority.
- Recorder existente reaproveitado com autorização explícita, sem criar membership automaticamente no caminho novo.
- Receipts diferenciam pending/queued/persisted/failed; observação do episódio e autorização do fragmento são necessárias para persisted.
- Dedupe e claim SQLite sobrevivem a saída de processo; retry de autorização antes de escrita é seguro; submissão incerta nunca dispara re-extração automática.
- Round-trip .NET → rota/plugin/recorder → episódio → reader real passou com Graphiti/SpiceDB sintéticos. Isso prova plumbing, não qualidade de extração por modelo nem ingestão live.
- Sem escrita Graphiti, modelo, outbound ou reativação nesta implementação. SpiceDB e uma janela de escrita/recall ainda precisam ser verificados/autorizados operacionalmente.

Operação, limites e rollback: [native-memory-ingestion.md](native-memory-ingestion.md).
