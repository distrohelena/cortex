# Cortex

<!-- impeccable:product-schema 1 -->

## Platform

web — editor servido por um host .NET local em loopback.

## Users

Helena e consumidores do Cortex que precisam compor, executar e inspecionar operações locais em graphs tipados. O primeiro consumidor de mídia é o Depois do Slogan.

## Product Purpose

Permitir que aplicações usem graphs genéricos, com operações oferecidas por plugins opcionais. O usuário abre um graph, configura seus nodes, executa e inspeciona valores e erros.

## Operating Context

Windows local, .NET 10, biblioteca Cortex.Core e componente de canvas TypeScript existentes. O editor tem settings JSON compartilhadas por usuário; consumidores abrem seus próprios graphs usando o editor e o perfil já configurados.

## Capabilities and Constraints

- DLLs de plugins desativadas não são carregadas por padrão. Mudanças de carregamento exigem reiniciar o editor neste recorte.
- O primeiro plugin fornece Video File → Transcribe → Transcript para um único vídeo, com transcrição local em português e GPU quando disponível.
- O canvas, tipos, compilador e runner existentes são reutilizados. A interface deriva nodes e propriedades de metadados.
- O plugin de vídeo também oferece Suggest Takes e Render Cuts: seleção textual pelo Jev e renderização local dos intervalos de um JSON editável para MP4 com áudio.
- Não há lotes, agentes, cache de transcrição, editor automático ou integração com editores externos neste recorte.
- Mídia, modelos, configurações e resultados locais permanecem privados e fora do Git.
- Controles devem ser acessíveis por teclado, estados de erro devem indicar recuperação e outputs são dados reais.

## Brand Commitments

Nome Cortex. Interface, nomes dos nodes, campos e metadados gerados em inglês. A transcrição preserva o idioma do vídeo. O canvas existente é a referência visual; não foi solicitada uma nova identidade visual.

## Evidence on Hand

O graph de vídeo já executado, transcrição de 23 segmentos de uma amostra pública de 100,22 segundos e confirmação de uso da RTX 4090. Código, testes e especificação aprovada estão neste repositório. Não há evidência de outros workflows de mídia.

## Product Principles

- Fazer uma operação real por vez e inspecionar o que o graph produziu.
- Manter plugins opcionais e iniciar ferramentas/modelos apenas quando necessários.
- Tornar explícitos dados, execução, dependências indisponíveis e erros.
- Permitir que consumidores reutilizem o mesmo editor e perfil local.
