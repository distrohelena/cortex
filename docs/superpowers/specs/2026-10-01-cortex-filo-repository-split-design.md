# Cortex e Filo: separação de repositórios

Status: especificação aprovada em conversa em 2026-10-01; plano de execução em revisão.
Base inspecionada: commit `557eab0`, branch `main`, pasta `C:\dev\filomena`.
Esta entrega documenta; não move pastas, inicializa Git, altera serviços ou executa modelos.

## 1. Objetivo e relação com o trabalho anterior

Separar a fundação Cortex da aplicação Filomena em dois repositórios locais:

```text
C:\dev\cortex    Git atual; fundação compartilhada
C:\dev\filo      Git independente; aplicação que consome Cortex
```

Esta decisão substitui SOMENTE a decisão de manter uma pasta/repositório da seção 3
de `2026-09-30-cortex-foundation-design.md`. Preserva as fronteiras, compatibilidade,
hashes e limitações registradas em `docs/cortex-migration-status.md`. Não conclui
ownership multi-project, aceitação PostgreSQL nem a evolução geral da plataforma.

## 2. Evidências e limites

- `Filomena.slnx` agrega seis projetos Cortex, App/Llm da Filomena e cinco projetos de testes.
- `src/Filomena.App/Filomena.App.csproj` referencia Core, Inference, Memory, OpenClaw
  e Filomena.Llm; o build integra assets pelo script `scripts/build-flow-graph.mjs`.
- `src/Filomena.Llm/Filomena.Llm.csproj` depende somente de Core e Inference.
- Os `.csproj` de produção Cortex não referenciam Filomena.App ou Filomena.Llm.
- `tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj` ainda referencia Filomena.Llm.
  A busca por `using Filomena.Llm` não encontrou consumidor; isso não basta para
  provar que a referência é dispensável. A retirada precisa de build/teste isolado.
- `tests/Cortex.OpenClaw.Tests` referencia App. InboundBridgeHttpServerTests,
  PhaseBCompositionTests, PhaseCPipelineTests, ProbeOptionsTests e TimedLmStudioClientTests
  importam Filomena.App e devem ser tratados como testes da aplicação.
- `packages/flow-graph` é reutilizável; o script de integração copia seus assets para App.
- O teste `tests/architecture/cortex-foundation.test.mjs` assume um único root e
  `Filomena.slnx`; o manifesto de 53 hashes mistura artefatos das duas responsabilidades.
- O Git está limpo salvo `workspace/DREAMS.md` não rastreado. Preservá-lo sem estágio.
- Cortex/Filomena não aparecem no catálogo codebase-memory consultado; evidência
  obtida por leitura de fontes e referências, sem alegar cobertura do índice.

Os resultados de 2.398 testes do checkpoint anterior são baseline histórica;
a divisão ainda não foi executada nem validada. Estado operacional deve ser
reconsultado imediatamente antes de qualquer movimento físico.

## 3. Ownership proposto

| Material atual | Destino | Regra |
| --- | --- | --- |
| `src/Cortex.*` | Cortex | Preservar assemblies, APIs e compatibilidade de identidade |
| `src/Filomena.App`, `src/Filomena.Llm` | Filo | Preservar nomes .NET nesta fatia; não misturar outra renomeação |
| Testes de Core, Inference, Evaluation e transporte puro | Cortex | Nenhuma dependência obrigatória de Filo |
| Testes de composição/App, comportamento e pipelines da Filomena | Filo | Podem referenciar Cortex localmente |
| `packages/flow-graph` | Cortex | Manter nome do pacote nesta fatia; Filo consome build local |
| Integração de assets do App | Filo | Script resolve CortexRoot, sem copiar fonte do pacote |
| Infraestrutura genérica em `evaluation` e `runtime/evaluation` | Cortex | Separar os casos/perfis específicos da Filomena por conteúdo |
| Corpus, projetos aprovados, personalidade e ferramentas operacionais da Filomena | Filo | Preservar bytes de artefatos aprovados e ownership privado |
| Plugins e guards específicos da Filomena | Filo | Não generalizar comportamento durante a separação |
| `plugins/openclaw-memory-graphiti` | Filo inicialmente | Integração operacional existente; extração genérica é trabalho separado |
| Documentos históricos | Histórico preservado; cópias arquivadas identificadas | Não reescrever relatos antigos para simular caminhos novos |
| Documentação canônica corrente | Repositório responsável | Referências cruzadas explícitas, sem duas fontes correntes divergentes |
| Segredos, dados, logs, bancos, caches e publish ativo | Fora dos commits | Não migrar operação nem apagar ignorados automaticamente |

O plano deve registrar um manifesto de ownership por arquivo rastreado, incluindo
fixtures compartilhadas e scripts, antes de extrair. Diretórios mistos não serão
transferidos cegamente. Arquivo sem classificação impede completar a extração,
não justifica descarte. A exceção `Filomena.Core.Identity.FilomenaIdentity` permanece
em Cortex para não alterar identidades persistidas; ela não torna o Core dependente de Filo.

## 4. Contrato das referências locais

- Duas soluções: `Cortex.slnx` e `Filo.slnx`.
- Filo define uma propriedade MSBuild `CortexRoot`, com default normalizado relativo
  ao próprio repositório: `../cortex`. Aceita override explícito no build.
- `ProjectReference` usa `$(CortexRoot)/src/Cortex...`; scripts de assets recebem
  o mesmo root resolvido. Não depender de cwd, nome de usuária ou caminho absoluto.
- Cortex compila e testa sem o checkout Filo. Filo exige um checkout Cortex válido
  e retorna diagnóstico acionável quando estiver ausente.
- Registrar em Filo a revisão Cortex testada e atualizar esse registro somente
  após validação explícita. Uma verificação distingue revisão validada de alterações
  locais de desenvolvimento; não muda checkout, baixa código ou promove versões.
- Sem NuGet privado, submodule, subrepo, junction ou cópia das bibliotecas nesta fatia.
  Pacotes versionados podem substituir a referência local futuramente.

## 5. Git e histórico

O repositório atual conserva `.git`, branch e histórico em Cortex, com origin
`git@github.com:distrohelena/cortex.git`. Não reescrever commits publicados.

Filo nasce de uma cópia Git local independente do checkpoint aprovado, sem alternates
ou vínculo de worktree. Isso preserva a proveniência do código e o histórico original;
os dois repositórios compartilham ancestralidade, mas possuem working tree, index,
refs e novos commits independentes. A separação ocorre em commits normais posteriores.
O histórico antigo conterá os dois componentes; não alegar histórico filtrado.

Remover o origin herdado de Filo antes de qualquer possibilidade de push; nenhum
remoto GitHub novo será criado ou inferido. Publicação futura requer destino explícito.
Não clonar arquivos ignorados nem adicionar segredos, dados privados ou DREAMS.
Commits de extração devem registrar o checkpoint de origem e a revisão compatível.

## 6. Caminhos e operação

Renomear a pasta física é uma operação distinta de mudar o runtime instalado.
Antes dela, verificar referências ao caminho antigo em launchers, scripts e
configurações conhecidas, sem expor argumentos/credenciais. Um processo não usar o
caminho antigo na linha de comando não prova ausência de dependências em arquivos.

Não reiniciar, parar, relinkar ou reconfigurar serviços como parte implícita do split.
Se algum caminho ativo depender da pasta antiga, preparar o código e reportar a
mudança operacional concreta necessária antes do corte físico. Não introduzir um
alias silencioso para esconder referências quebradas.

Inventariar arquivos ignorados por categoria/localização sem publicar seu conteúdo.
Manter ACLs e bytes dos arquivos privados. Não fazer limpeza de bin/obj, caches,
publicações, bancos ou backups para facilitar o movimento. Antes do rename, validar
origem/destino absolutos, destino ausente e possibilidade de restauração.

## 7. Aceite e recuperação

- Cortex constrói/testa sem Filo; nenhum ProjectReference aponta para a aplicação.
- Filo constrói/testa com CortexRoot default e override para um checkout controlado.
- Ausência de Cortex e incompatibilidade registrada produzem diagnóstico claro;
  nenhuma busca remota ou troca automática de revisão.
- Cada teste executável atual tem destino explícito. Nenhuma falha é ocultada pela
  divisão; os dois drafts históricos continuam históricos, não testes aprovados.
- Preservar os 53 valores de hash e checksums SQL. Ajustar somente a resolução de
  localização dos artefatos; Cortex verifica sua parte sem exigir arquivos de Filo,
  e Filo verifica os seus e a composição autorizada. Não recalcular expected hashes.
- Preservar contratos de identidade, Shadow/Enabled, inferência, memória e outbound.
- Usar testes offline/fakes; serviços reais, modelos, Graphiti e WhatsApp não são
  necessários para aceitar esta separação de código.
- Publicar relatório por repositório com builds, testes, exclusões e revisão cruzada.
- Preservar checkpoint e manifesto de movimentos. Antes do corte operacional,
  recuperação consiste em restaurar caminhos e referências a partir desses registros,
  sem reset destrutivo ou perda de arquivos não rastreados. Nenhum efeito externo
  exige reconciliação porque esta fatia não o executa.

## 8. Alternativas e próximos gates

Referências locais foram escolhidas pela usuária para desenvolvimento conjunto.
Pacotes NuGet adicionariam publicação/versionamento desnecessários agora. Submodules
fixariam fonte, mas acrescentariam outro workflow Git não solicitado.

Próximo gate: revisão do plano `../plans/2026-10-01-cortex-filo-repository-split.md`
e confirmação da forma de execução; a aprovação da especificação não executa a separação.
