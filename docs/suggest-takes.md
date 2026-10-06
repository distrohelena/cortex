# Suggest Takes

O plugin opcional `Cortex.Video` oferece o node `suggestTakes`, exibido como **Suggest Takes** no editor. Ele usa os mesmos tipos, catálogo, compilador e runner dos demais nodes. A DLL e sua dependência `Cortex.Inference` pertencem ao pacote opcional; o host e os consumidores não referenciam o plugin estaticamente.

```text
Video File → Transcribe → Transcript → Suggest Takes
```

O graph original de três nodes continua funcionando. Para acrescentar a análise no Depois do Slogan:

```powershell
cd C:/dev/depois-do-slogan
./tools/open-editor.ps1 -Video 'C:/caminho/video.mp4' -Goal 'Ensinar o procedimento de forma clara e direta' -CredentialFile 'C:/caminho/protegido/jev.txt'
```

O objetivo pode ser editado nas propriedades do node. **Export JSON**, no inspector de qualquer output, salva seu valor tipado. O CLI do consumidor também grava `edit-plan.json` junto dos demais artefatos.

## Entrada, análise e output

A entrada `transcript` segue `{ text, segments: [{ start, end, text }] }`. O parâmetro obrigatório `goal` descreve o objetivo editorial. O output `editPlan` contém:

| Campo | Significado |
|---|---|
| `version`, `status` | Versão 1 e estado `draft`; não representa aprovação humana. |
| `timebase`, `assessmentBasis` | Tempos de origem em segundos e avaliação somente da transcrição. |
| `goal`, `sourceTranscriptHash` | Objetivo usado e SHA-256 da transcrição original. |
| `provider`, `model` | Jev e `jev-1.13.0`. |
| `candidates` | IDs, intervalos de origem, IDs dos segmentos, texto, grupo, avaliações, `keep` e motivo. |
| `groups` | Candidatos considerados tentativas alternativas da mesma fala e ID sugerido. |
| `repeatEvidence` | Pares consultados, decisão de repetição aceita e probabilidade de `same`. |
| `proposedTimeline` | Proposta sequencial dos candidatos mantidos, com tempos de origem e tempos na edição sugerida. |

O Jev recebe IDs e texto, sem poder definir intervalos. Os tempos no JSON são copiados dos segmentos originais. Há duas avaliações de 0 a 3: `deliveryScore` mede clareza/completude **pelo texto**; `contentScore` mede contribuição ao objetivo. Não há avaliação de áudio, imagem ou atuação.

Chunks contíguos são reunidos até pontuação final, pausa maior que 0,7 s ou limite de 30 s para a união. Segmentos contendo somente marcações como `[Música]` são ignorados. Cada candidato conserva os índices dos segmentos que o formaram. Um segmento individual não é dividido.

Repetições textuais exatas são agrupadas localmente. Para outras falas, pelo menos três tokens compartilhados e similaridade Jaccard de 0,65 selecionam pares para consulta ao Jev. A resposta `same` só é aceita com probabilidade e confiança de pelo menos 0,8. Todos os pares de um grupo precisam de evidência compatível; uma cadeia A–B–C não basta para agrupar A e C. Essa busca conservadora pode deixar paráfrases em grupos separados.

Dentro de cada grupo, a maior avaliação textual escolhe a tentativa sugerida; empate favorece a primeira no vídeo. Conteúdo com nota de pelo menos 2 mantém essa tentativa na proposta. Os candidatos mantidos preservam a ordem original. Esses critérios são sugestões editáveis, não decisões finais de corte.

`proposedTimeline` descreve a proposta original. Editar manualmente `keep` ou intervalos não recalcula esse campo; um futuro consumidor deve validar e reconstruir a timeline ao usar o JSON modificado.

## Configuração e limites

O manifesto expõe `jevEndpoint` (default `https://api.typesafe.ai/v1/systemone`) e o **nome** da variável de credencial (`jevCredentialVariable`, default `TYPESAFE_API_KEY`). Nunca informe o valor da chave nas settings.

`-CredentialFile` usa o formato protegido pelo Windows do launcher da Filo: `ConvertFrom-SecureString`, recuperável pela mesma usuária. O helper restaura a variável anterior ao encerrar e libera o buffer de recuperação. A chave só é lida quando necessário. Abrir o editor ou usar apenas os três nodes locais não exige credencial Jev.

A transcrição e o objetivo são enviados ao endpoint configurado quando o node executa. Vídeo e áudio não são enviados. Há uma chamada nativa em lote, com perguntas `score` e `choice`, usando `SystemOneClient` existente. Falhas não viram um plano vazio nem causam repetição automática da chamada.

Este recorte aceita até 512 segmentos, 32 candidatos e 128 pares semelhantes. Exceder um limite gera erro explícito; o vídeo não é truncado silenciosamente. Transcrições sem fala produzem rascunho vazio.

O JSON editorial não é o schema de legendas do Helworks VFX. Este node não corta, renderiza, solicita aprovação humana, destaca palavras ou cria legendas.
