# Cortex: nome e fronteira da fundação compartilhada

**Status:** especificação aprovada em conversa; plano de implementação escrito para revisão.
**Base:** `main`, checkpoint `17ad29a`. Esta entrega não implementa a migração.
**Objetivo:** nomear como Cortex a infraestrutura reutilizável por Filomena,
Damma e outros consumidores, mantendo Filomena como aplicação/personagem.

## 1. Relação com o trabalho existente

Esta é uma especificação de nomenclatura, dependências e compatibilidade, não
uma nova plataforma nem substituição dos planos em andamento:

- [Evolução para plataforma](2026-09-27-intelligence-platform-evolution-design.md).
- [Ownership de memória](2026-09-28-memory-ownership-boundary-design.md).
- [Plano de leitura host-bound](../plans/2026-09-28-host-bound-memory-read.md).
- [Plano do workbench](../plans/2026-09-26-evaluation-workbench.md).
- [Acompanhamento da migração](../../platform-migration-progress.md).

A renomeação não conclui ownership, aceitação PostgreSQL, API pública ou produto
Damma. Esses gates continuam separados. Não introduzir uma segunda authority,
outro workbench ou nova camada de execução só para acomodar o nome Cortex.

## 2. Evidência atual e limites

Inspeção estática de arquivos; nenhum build, teste ou serviço executado nesta
entrega. O catálogo codebase-memory consultado na exploração anterior não continha
Filomena; não se atribui cobertura de grafo a esta inspeção.

| Evidência | Consequência |
|---|---|
| `Filomena.slnx` inclui App, Core, Llm, Memory, OpenClaw, Evaluation e Evaluation.Cli | Já existe separação em assemblies; conservar um monólito modular |
| `src/Filomena.Core/Filomena.Core.csproj` não referencia adapters; Memory/Llm/OpenClaw referenciam Core | Preservar direção das dependências, sem transporte obrigatório no core |
| `src/Filomena.Core/Inference/HostInferenceContracts.cs`: `InferenceScope`, `InferenceCorrelation`, `HostInferenceProfile`; `HostInferenceAuthority.cs` | Inferência host-bound existente será preservada, não recriada |
| `src/Filomena.Llm/Agent/FilomenaAgentOptions.cs` inclui `SystemPrompt` específico da Filomena | Llm é misto; não renomear integralmente para uma biblioteca pretensamente genérica |
| `src/Filomena.Llm/ProviderFlowCompletion.cs`, `OpenAi/OpenAiFlowCompletion.cs`, `SystemOne/SystemOneClient.cs`, `LmStudio/LmStudioClient.cs` | Candidatos existentes à camada de adapters `Cortex.Inference`; verificar dependências transitivas no plano |
| `src/Filomena.Core/Identity/FilomenaIdentity.cs`: `AgentUuid` incorpora `agent:filomena`; namespace de conversa preserva formato legado | Identidade da aplicação e formato persistido não são o nome da plataforma |
| `src/Filomena.Evaluation/Storage/MigrationCatalog.cs::Read` e `.csproj` usam `Filomena.Evaluation.Storage.Migrations.*` | Renomear assembly não autoriza perder recursos SQL ou mudar checksums |
| `src/Filomena.App/Filomena.App.csproj` compõe Core/Llm/Memory/OpenClaw | App permanece consumidor e host da Filomena; não vira `Cortex.App` automaticamente |

Este inventário fundamenta o recorte, não é uma auditoria exaustiva de todos os
símbolos. O plano deverá fechar o inventário de consumidores, reflexão, recursos,
scripts e formatos persistidos antes de qualquer movimento de arquivos.

## 3. Decisão proposta

Manter um repositório e a pasta física `C:\dev\filomena` nesta migração.
Separar nomenclatura da fundação e identidade dos produtos por responsabilidade.

| Atual | Destino proposto | Condição |
|---|---|---|
| `Filomena.Core` | `Cortex.Core` | Runtime, flows, contratos, authority e mecanismos reutilizáveis; sem impor persona Filomena |
| Adapters genéricos de `Filomena.Llm` | `Cortex.Inference` | Mover apenas código independente da aplicação; conservar protocolos e configurações de modelos |
| `Filomena.Memory` | `Cortex.Memory` | Adapters/validação/proveniência; preservar compatibilidade de identidade legada explicitamente |
| `Filomena.OpenClaw` | `Cortex.OpenClaw` | Integração opcional, dependente de Core; Core não passa a depender dela |
| `Filomena.Evaluation` | `Cortex.Evaluation` | Oficina existente; sem nova persistência ou promoção automática |
| `Filomena.Evaluation.Cli` | `Cortex.Evaluation.Cli` | Ferramenta da oficina, não host da personagem |
| `Filomena.App` | `Filomena.App` | Executável, composição operacional e comportamento da aplicação |
| Comportamento específico em `Filomena.Llm` | Permanece na camada Filomena | Pode conservar temporariamente o assembly residual, consumindo Cortex.Inference |
| Testes da infraestrutura | Prefixo `Cortex.*.Tests` correspondente | Testes específicos da aplicação continuam identificados como Filomena |

Alvo de dependência: aplicação Filomena → módulos Cortex; adapters Cortex →
Cortex.Core. Cortex.Core não referencia Filomena.App nem o assembly residual
específico da personagem. Damma será consumidora futura, não um projeto vazio
criado nesta renomeação. Nenhum consumidor será obrigado a carregar WhatsApp,
participação social ou uma persona para usar inferência.

`Filomena.slnx` pode continuar como solução agregadora durante a transição.
Sua eventual renomeação para `Cortex.slnx` exige atualizar os callers de build no
mesmo passo; não é necessária para estabelecer a fronteira entre assemblies.
Não criar agora uma segunda solução que duplique uma lista de projetos sem uso.

### Código específico ainda em bibliotecas compartilhadas

Não resolver acoplamento apenas trocando o namespace de um tipo. Classificar os
callers de `FilomenaIdentity` e dos defaults da personagem antes da extração:

- helpers puramente determinísticos podem ser compartilhados;
- o discriminador `agent:filomena` continua pertencendo à identidade Filomena;
- consumidores atuais mantêm exatamente os mesmos resultados de identidade;
- qualquer novo consumidor sem binding autorizado deve falhar, não herdar a
  identidade ou namespace da Filomena;
- não adicionar um framework de personas/identidades para fazer essa separação.

Se mover uma classe específica exigir dependência circular, interromper aquele
movimento e definir a menor passagem de dados/política pelo host no plano. Não
afirmar que a fundação está desacoplada enquanto esse problema persistir.

## 4. Compatibilidade obrigatória

1. **Código e binários:** atualizar `.csproj`, referências, namespaces, testes,
   `InternalsVisibleTo`, recursos e callers locais como conjunto coerente.
   Não prometer compatibilidade binária com consumidores externos não inventariados.
   Não manter dois runtimes com tipos concorrentes para simular compatibilidade.
2. **Filomena:** preservar persona, prompts, atribuição de modelos, flows aprovados,
   direct involvement, FSM, grounding/context fidelity e Boundary enforcement.
   Preservar bytes dos artefatos de projeto aprovados e seus hashes.
3. **Identidade e persistência:** nenhuma mudança de UUIDs, chaves de dedupe,
   namespaces Graphiti/SpiceDB, session IDs, referências, nomes de journals,
   cursors, receipts ou conteúdo serializado só por causa de namespaces C#.
   Formatos que carreguem nomes de tipos/assemblies exigem estratégia explícita
   de leitura compatível antes da renomeação correspondente.
4. **SQL:** manter bytes, versões e checksums de migrations. Na primeira passagem,
   conservar os `LogicalName` legados e o lookup de recursos correspondente;
   nomes internos de recursos não precisam acompanhar o branding. Não migrar banco.
5. **Host:** manter variáveis de ambiente, nomes de seção de configuração,
   endpoints e contratos wire atuais, salvo mudança versionada e separadamente
   aprovada. Nenhuma credencial ou permissão passa a vir de project JSON.
6. **Operação:** não substituir DLLs em uso, mover a instalação, reiniciar a
   Filomena ou alterar scripts/configs privados ativos como parte do refactor local.
   Um artefato novo exige publicação completa e janela de deploy separada.
7. **Documentação:** atualizar referências atuais afetadas; preservar relatórios
   históricos com seus nomes/revisões originais. Não reescrever evidência antiga
   como se Cortex já fosse o nome do sistema naquele momento.

Compartilhar assemblies não concede compartilhamento de dados. O escopo confiável,
revogação e isolamento entre produtos continuam requisitos, não benefícios
automaticamente obtidos pela renomeação.

## 5. Alternativas consideradas

- **Somente branding documental:** risco mínimo, mas deixa consumidores novos
  dependentes de assemblies chamados Filomena e não resolve a fronteira desejada.
- **Renomeação seletiva com extração mínima — recomendada:** aproveita os projetos
  existentes, preserva a aplicação e explicita o código realmente reutilizável.
- **Replace global ou novo repositório/framework:** pode mudar identidade/dados,
  esconder acoplamento e ampliar a migração. Não adotado.

## 6. Sequência e aceite da futura implementação

Isto é ordem de desenho, não o plano executável ainda não aprovado:

1. Fixar baseline e mapear referências/identidades/serialização; caracterizar os
   contratos que a renomeação não pode alterar.
2. Renomear a menor fatia coerente da fundação, ajustando consumidores locais;
   preservar adapters específicos e compatibilidade de identidade. Recomenda-se
   começar pelo Core e seus consumidores somente após fechar o mapa de identidade.
3. Extrair adapters genéricos para Cortex.Inference e renomear Memory, OpenClaw
   e oficina em passos verificáveis, sem completar outros produtos no caminho.
4. Publicar evidência de build/testes locais e inventário de compatibilidade.
   Deploy e revalidação live ficam fora desta autorização.

Critérios de aceite que o plano deve concretizar:

- build dos consumidores existentes e ferramentas, incluindo projetos não cobertos
  por uma única solução; fixtures offline sem modelos/serviços;
- testes de referência impedem dependência do Core na aplicação e nos transports;
- testes de inferência host-bound continuam negando escopo/revisão/revogação inválidos;
- fixtures de UUID/namespace, journals e serialização mantêm resultados anteriores;
- catálogo SQL carrega todas as migrations e mantém checksums após a mudança;
- comportamento de Shadow, entrega incerta, ingestão independente de resposta,
  participação direta e Boundary/regeneração permanece coberto;
- hashes de projetos/persona/prompts aprovados permanecem iguais;
- aceitação PostgreSQL e integração live continuam explicitamente pendentes se
  não forem executadas; testes offline não substituem essas evidências.

Reversão local deve desfazer somente o refactor em commits delimitados. Não há
rollback de dados porque esta fatia não os migra. Uma publicação futura deve
preservar o conjunto anterior de binários/configs; misturar DLLs antigas e novas
não é uma estratégia de rollback.

## 7. Fora do escopo e próxima aprovação

Sem repositório novo, pacotes publicados, novos serviços, banco novo, alteração
Graphiti, implementação Damma/Dota, tuning, chamadas pagas ou deploy. Nenhuma
decisão de tenant/ownership é substituída por um prefixo Cortex.

O nome, o recorte e esta especificação foram aprovados. O
[plano detalhado](../plans/2026-09-30-cortex-foundation.md) está escrito para revisão;
implementação só após aprovação desse plano e escolha da execução.
Questões de inventário serão resolvidas no código, não reapresentadas como novas
decisões de produto. Um consumidor binário externo ou formato persistido dependente
do nome de assembly, se encontrado, deverá constar explicitamente no plano.
