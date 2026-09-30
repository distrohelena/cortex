# Instruções em português: variante local v1

Artefato: `manual-group-live-project-v3-pt-br-v1.json`.
Base preservada: `manual-group-live-project-v3-natural-v1.json`.

## Escopo

Tradução de prosa em 30 campos `prompt`, nos três estados, incluindo os
ramos DM e grupo. Exemplos: mention → menção; reply → resposta vinculada;
input → entrada; state → estado; validator → validador;
GROUNDING DE PESSOAS → FUNDAMENTAÇÃO SOBRE PESSOAS.

Identificadores de contrato continuam literais: `personalityCore`, `replyTo`,
`reply`, `ref`, `true`, `false`, `null`. Não são instruções em outro idioma.
Modelos, parâmetros, personalidade, schemas, IDs, ligações, FSM, regras,
limites, memória e mecanismos de validação não foram alterados.

O projeto mantém o ID e a versão de schema existentes; é uma variante
distinta pelo arquivo e seu SHA-256, não uma migração operacional automática.
Nenhum pin/config de instalação foi atualizado e nenhum processo foi iniciado.

## Limite desta limpeza

Isto **não** significa que o prompt final composto pela engine seja inteiramente
português. `InstructionContext.Compose` ainda acrescenta os cabeçalhos genéricos
`[Personality core]`, `[State participation guidance]` e `[Interest profile]`.
Templates dos validadores e instruções dos adapters não foram modificados.
Essa distinção deve permanecer visível numa futura comparação de prompts completos.

Também permanece uma contradição anterior no ramo de geração de grupo:
“Retorne apenas texto, não JSON” é seguido pela instrução de retornar
`texto` e `replyTo` em JSON. Corrigir isso é outra alteração semântica;
não foi misturada à tradução.

## Validação local

`PortuguesePromptArtifactTests` carrega a variante pelo compilador/pinning real.
Restaurar somente seus campos de prompt precisa recuperar integralmente a base,
protegendo contratos, modelos, roteamento e toda configuração não textual.
O teste falhou primeiro pela ausência da variante e passou depois da geração.

Suite `Filomena.Llm.Tests`, configuração `NativeMemory`: 805 aprovados,
47 pulados, zero falhas. Variáveis de serviços/credenciais removidas apenas
do processo filho dos testes; nenhuma inferência ou envio live.

Não é avaliação A/B de qualidade. Não há evidência nova de que idioma misto
cause participação excessiva, repetição ou confusão de identidade.
Esses problemas continuam no relatório da auditoria de 48 horas.

Baseline geral já conhecido, fora desta mudança: Core tem 19 falhas nos testes
locais de `FlowBindingValidatorTests` (cinco casos) e `FlowCompilerTests`
(`MalformedDirectObjectsReturnSafeIssues`, 13 casos; e
`OversizedImportIsRejectedBeforeAllocatingItsUtf8Payload`). A suite OpenClaw
tem erros de compilação nos arquivos locais `FrontendBuildTests.cs` e
`SettingsStoreTests.cs`, referentes a namespaces ausentes. Não foi declarado
que a solução inteira está verde, nem essas alterações alheias foram corrigidas.

Sem deploy, commit, push, escrita de memória ou reativação da Filomena.
