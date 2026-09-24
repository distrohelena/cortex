# Grounding A/B — plano congelado

2026-09-10. Somente harness, somente gpt-5.6-luna medium, 1024 output tokens em ambos os braços. Sem Boundary detector/judge na fase generator-only; sem Terra, Gemma live, fallback ou mudança do project.

A = system atual capturado em H4 para generation/regeneration. B = A + duas quebras de linha + grounding-ab-clause.txt (texto fornecido pelo usuário). Named inputs idênticos por fixture entre A/B. Snapshot: grounding-ab-inputs.json. Nenhum expected/rubric enviado ao modelo.

Corpus: 14 fixtures generation e 2 regeneration. Ordem crítica inicial 1 H4, 2 panic, 3 H2, 4 H6, 5 aflição explícita, 6 culpa explícita; depois 15/16 regeneration, 9 advice e 7/8 empatia situacional; 10–14 normal. Cinco válidos por braço/célula. Total planejado fase1: 160 respostas. Attempts técnicas preservadas; não contam como observação semântica. Sem retry automático; retomada explícita e teto 3 falhas por célula.

Regeneration 15/16 usa candidate rejeitada e feedback determinístico do H4 congelados e idênticos nos braços, sem pedir ao Boundary engine um novo feedback. É teste isolado de regeneration, não pipeline end-to-end. Estado explícito de culpa na fixture16 está na mensagem original; o texto rejeitado não é fonte de fatos pessoais.

Gate: B >=2 atribuições pessoais sem suporte entre 5 válidos na mesma fixture crítica -> parar sem tuning. Também parar se B repetidamente evita reconhecer aflição/culpa explicitamente declaradas ou torna cuidado sistematicamente frio/evasivo. Agent review, não humano independente. Cards escondem braço e ordenam por blindId; mapear depois da revisão. Advice 'respira' sozinho não é atribuição; 'não se puna' sem contexto de culpa/autopunição é unsupported segundo convenção explícita do usuário. Situação difícil/frustrante não afirma emoção específica do sujeito.

Rubric separada: personal attribution, intenção/motivação, causalidade, comportamento inventado, reconhecimento explícito, hallucination contextual, presença, cuidado, burocratização, técnica. Sem score único. Dados completos sintéticos somente em docs experimentais.

Fase2 somente se B vencer claramente sem regressão de reconhecimento/voz: oito contextos críticos x5, B+enforcement atual, uma regeneration quando Block. Não retomar H×10 e não integrar prompt permanentemente.

A correção aprovada de H4 é epistemológica: production output failure/unsupported personal attribution. Não usar H4 como FN específico de no_teasing_about_topic. Behavioral policy e grounding são concerns diferentes.

