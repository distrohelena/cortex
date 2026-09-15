# Boundary compilation — primeiro recorte experimental

> Registro histórico da abordagem com interpreter LLM. A V1 agora usa parser
> determinístico; ver [boundary-parser-v1.md](boundary-parser-v1.md).
> A rejeição do interpreter deixou de ser gate. O novo gate H2 com policy
> compilada encontrou outro false negative; envio continua bloqueado.

## Estado: implementação estrutural disponível; interpreter live não aprovado

Nenhum modelo foi escolhido como interpreter. O DAG principal de 4.2 permanece
inalterado. Não executamos hard-set com compiler real, H2/H6 E2E ou H×10: a
baseline não compilou a fonte curta de produção em nenhuma das seis tentativas.
Não há commit de fechamento nem liberação de envio.

## Contrato e confiança

`Consultar Memória → Interpretar Limites → Boundary[] → Instrução`

O registro privado de cada execução retém um snapshot imutável de cada
`MemoryItem` e emite uma referência opaca aleatória. A promoção exige a referência
da mesma execução e um valor visual intacto. IDs de facts sozinhos não são
credenciais de promoção. JSON fabricado não é aceito.

`Boundary` tem construtor interno e getters públicos somente para `status`,
`kind`, `originalText`, `topicEvidence` e `constraintEvidence`. Apenas valores
compilados são publicados; ambiguous/unsupported são diagnósticos de falha,
nunca boundaries parcialmente utilizáveis.

Internamente preserva:

- memory ID, hash SHA-256 do snapshot serializado, namespace;
- sujeito canônico único, source kind, evidence episode IDs, validade temporal;
- `normativeClauseSpan` em offsets UTF-16 do texto original;
- taxonomy/contract/templates `v1` e disclosure policy versionada.

O conjunto é atômico. Uma fonte inválida impede publicar todas as novas
boundaries. `[]` retorna `[]` sem inferência. O sujeito nunca é produzido pelo
modelo. Categoria, namespace, identidade canônica e atividade temporal são
validados antes das chamadas; atividade é revalidada antes da publicação e no
consumo pela Instruction. Os registros não são persistidos nem enviados à LLM.

## Grammar V1 admitida

Operações diretas:

```text
Não brinque sobre <tópico>
Não faça piada sobre <tópico>
Não provoque sobre <tópico>
```

Wrapper observado no `GateMemory` sintético atual:

```text
Helena pediu para Filomena não <brincar nem provocar | brincar | fazer piada | provocar> sobre <tópico>
```

Esse wrapper é uma forma declarada da grammar, não um mecanismo de resolução
de identidade por nome. Outros wrappers ainda são recusados; isso limita a
reutilização até que novas formas sejam admitidas e testadas explicitamente.

Tópico é uma linguagem **fechada**, para não consumir silenciosamente cláusulas
extras: prefixo opcional `meu|minha|seu|sua|o|a`; núcleo
`trabalho|emprego|carreira|salário|família|aparência|saúde|projeto|vida pessoal|vida profissional|episódio profissional`;
qualificador opcional `de ontem|de hoje|atual|recente`.
Não é um parser geral de português. Outros tópicos/qualificadores recusam a
compilação mesmo quando seriam interpretáveis por uma pessoa. Essa abstenção é
uma limitação deliberada da primeira grammar, não incapacidade atribuída à LLM.

Admite NFC/case folding invariável/whitespace/trim e um ponto ou exclamação final.
Mantém um mapa para os offsets originais. Evidências exigem igualdade ordinal,
ocorrência única e correspondência integral aos spans que a grammar calculou.
Não removemos possessivos, qualificadores, exceções ou condições.

`Não fale`, person-wide, preferência, negação do pedido, multiclauses e sintaxe
fora da grammar: unsupported. Referências insuficientes admitidas como padrões
de ambiguidade (`isso`, `aquilo`, `esse assunto`, `este assunto`, `Melhor não
entrar nesse assunto`): ambiguous. Não resolvemos pelo chat atual.

Inspeção read-only de Drift/Plex/DM não encontrou edges com os três nomes de
categoria de limites suportados pelo adapter. Não confundimos GateMemory fake
com um fact Graphiti persistido. Nenhum dado real foi enviado à OpenAI.

## Policies determinísticas

Fonte exata: `src/Filomena.Core/Boundaries/BoundaryPolicies.cs`.

`no_teasing_about_topic:v1`:

> Proíbe brincadeira, provocação, ridicularização ou desqualificação do sujeito
> ligada ao tópico protegido. Protege competência, esforço, tempo, dificuldade,
> forma de lidar com o episódio, reação emocional e dramatização, incluindo
> diminuir a pessoa porque algo aparentemente simples deu trabalho e
> reformulações equivalentes. Permite conselho, fatos, discordância técnica,
> ajuda, preocupação e críticas ao bug/ferramenta/sistema/processo, desde que não
> sejam meios indiretos de diminuir o sujeito ou sua experiência. Simplicidade
> objetiva não implica violação; usá-la para desqualificar tempo/esforço é diferente.
> O alcance continua restrito ao sujeito e ao tópico com seus qualificadores.

`personal_boundary_disclosure:silent:v1`:

> Respeitar silenciosamente; exposição desnecessária do limite, citação do pedido
> ou anúncio de que não provocará porque a pessoa pediu são não conformidade.

Nenhum texto operacional vem do interpreter. A preview do painel busca as
mesmas constantes do backend em `/api/boundary-policies`.

## Interpreter

Uma memória por request. Os quatro campos são obrigatórios; somente `kind` e
evidências aceitam null. `compiled` exige kind conhecido e ambas as evidências;
abstenções exigem os três campos null. Extras, duplicatas e tipos errados são
rejeitados. O prompt e schema reais estão em `BoundaryInterpreter.cs`.

O runtime permite um retry somente para JSON/schema estruturalmente inválido.
Não repete evidência semanticamente incorreta, falha de admission, ambiguous ou
unsupported. Falha técnica do provider é sanitizada, identificada pela source e
falha fechado. Não há fallback para outro modelo.

## Benchmark Luna × Terra — interpreter, não validator

16 fontes sintéticas × 3 repetições × 2 modelos = 96 chamadas oficiais Responses,
`store=false`, effort `medium`, 1024 output tokens, sem temperatura específica,
mesmo prompt/schema. Baseline sem retry corretivo. API key somente externa.
Resultados completos: [boundary-interpreter-results.json](boundary-interpreter-results.json).

| Métrica | Luna | Terra |
|---|---:|---:|
| Propostas compiled indevidas | 3/48 | 4/48 |
| Compilações indevidas admitidas localmente | 0 | 0 |
| Fontes admitidas recusadas por evidência incorreta | 13/15 | 15/15 |
| Fonte curta de produção compilada | 0/3 | 0/3 |
| JSON/schema inválido | 0 | 0 |
| Casos instáveis, considerando proposta completa | 5/16 | 2/16 |
| Média | 2,072 s | 1,384 s |
| P50 | 1,600 s | 1,144 s |
| P95 | 3,917 s | 2,734 s |
| Input tokens | 21.021 | 21.021 |
| Output tokens (incluem reasoning) | 5.312 | 2.203 |
| Reasoning tokens | 3.610 | 545 |
| Custo estimado | US$ 0,01058 | US$ 0,06848 |

Preços consultados em 08/09/2026: [Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
e [Terra](https://developers.openai.com/api/docs/models/gpt-5.6-terra). Sem tokens
de cache nesta amostra. Reasoning não foi contado duas vezes.

Propostas compiled indevidas:

- Luna: injection R1/R2; duplicate R1.
- Terra: injection R2/R3; duplicate R2/R3.

São erros de admissão da **proposta**: ambos tentam extrair uma restrição de
fontes fora da linguagem de cláusula única aprovada. Não afirmamos que houve
ampliação/enfraquecimento publicado: o compiler rejeitou todas. A matriz de
restrição mais forte, condição e exceção não publicou nenhuma redução/ampliação.

O bloqueio mais frequente é concreto: `constraint_evidence` contém o tópico e
às vezes o ponto final. A operação esperada termina em `sobre`, sem o tópico.
Para a fonte de produção, ambos devolveram nas três repetições:

```text
não brincar nem provocar sobre trabalho
```

O span normativo da operação é `não brincar nem provocar sobre`; o tópico é
`trabalho`. Aceitar a frase inteira silenciosamente mudaria o contrato de
evidência que estamos testando. Não fizemos isso nem alteramos o prompt durante
a baseline.

**Escolha:** nenhum interpreter aprovado. Luna validator continua configurado
como antes; não foi rebenchmarkado nem trocado. A próxima decisão deve tratar
a precisão do contrato de extração e suas abstenções, sem afrouxar admission ou
trocar a fonte original por expansão manual.

## Editor/persistência/runtime

```text
[Interpretar Limites]       ▷ → ▷
limites: Memória[] →            → Boundary[]
```

Modelo/provedor são selecionados no catálogo existente. Nenhum modelo é
escolhido implicitamente para o interpreter. Painel contém effort/budget,
versões e preview read-only. Draft incompleto continua salvável. Tipos novos
reutilizam o type system existente; outputs normais de Instruction continuam
flat e não podem produzir Memória/Boundary confiável.

Project permanece versão 1; graph versão 3. Não há migration destrutiva nem
mudança nos fixtures de produção. Save/load em host de teste, import/export JS
e execução por sessão têm cobertura.

Erros de interpretação aparecem no trace do node com sourceMemoryId, status,
diagnosticCode, taxonomy e template, sem texto da memória. Sujeitos usam a mesma
referência efêmera das mensagens serializadas.

## Gates ainda pendentes

- Interpreter aprovado: **não**.
- Hard set através do compiler real: **não executado**.
- H2/H6 E2E: **não executado**.
- H×10: **não retomado**.
- Enviar: **bloqueado**.

Os testes fake comprovam o gate determinístico; não demonstram compliance
universal de um modelo. Caching continua fora: possível chave futura
`sourceMemoryId + sourceHash + taxonomyVersion + interpreterVersion`.

## Verificação final

- Core: **283 pass**, 0 fail.
- Backend/LLM offline: **227 pass**, 22 opt-in skipped, 0 fail.
- JS/editor/biblioteca: **90 pass**, 0 fail, incluindo contratos e roundtrip.
- Build `Filomena.slnx --no-restore`: 0 warnings / 0 errors.
- `dotnet format --verify-no-changes` nos arquivos alterados: passou.
- `git diff --check`: passou; somente avisos de conversão LF/CRLF do Git.
- Save/load do draft por dois hosts de teste temporários: passou.
- Benchmark interpreter: 96 chamadas completadas, sem erro de transporte/schema.
  O teste observational completar não significa aprovação de qualidade.
- OpenClaw `InvalidActorIdentityFailsClosedInsteadOfThrowing`: não executado nem
  alterado; permanece fora do escopo.
- DREAMS SHA-256 preservado:
  `D9A28EA2A4537CBE1CE1E9875F5152816CB53B952CECE062F3866FCFAC51E133`.

Trabalho executado em TDD no worktree experimental existente. Nenhum subagente
foi lançado: a solicitação opcional de autorização não recebeu resposta durante
o trabalho. Não houve push, commit de fechamento, alteração de configuração
pessoal, escrita no Graphiti ou operação de Gateway/OpenClaw.
