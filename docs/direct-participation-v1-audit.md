# Direct participation V1 — audit

Controlled participation evidence; no generation in this section. Derived decisions below are runtime switch trace interpretations, not invented LLM outputs.

## direct-participation-v1-case-01-r1.json

Helena: Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.

Situation: {"ambiente":"serio","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena pede ajuda para organizar o dia com entrega e reunião."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 6540.98 ms.

## direct-participation-v1-case-01-r2.json

Helena: Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.

Situation: {"ambiente":"casual","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"forte","resumo":"Helena pede ajuda a Filomena para organizar sua agenda diária com entregas e reuniões."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 6557.88 ms.

## direct-participation-v1-case-01-r3.json

Helena: Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.

Situation: {"ambiente":"serio","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena pede a Filomena ajuda para organizar seu dia cheio de compromissos e tarefas."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 6343.20 ms.

## direct-participation-v1-case-01-r4.json

Helena: Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena pede ajuda a Filomena para organizar o dia devido a entregas e reuniões."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 7258.02 ms.

## direct-participation-v1-case-01-r5.json

Helena: Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.

Situation: {"ambiente":"serio","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena pede ajuda a Filomena para organizar seu dia, mencionando entregas e uma reunião."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 6010.28 ms.

## direct-participation-v1-case-02-r1.json

Helena: Filomena, pode me ajudar a revisar esta ideia de uma fila única?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena solicita ajuda de Filomena para revisar uma ideia conceitual sobre uma fila fânica."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-attentive-v1; calls: 1; 7033.36 ms.

## direct-participation-v1-case-02-r2.json

Helena: Filomena, pode me ajudar a revisar esta ideia de uma fila única?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena pede ajuda a Filomena para revisar uma ideia sobre fila fânica em DM."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-attentive-v1; calls: 1; 5704.96 ms.

## direct-participation-v1-case-02-r3.json

Helena: Filomena, pode me ajudar a revisar esta ideia de uma fila única?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"forte","resumo":"Helena pede ajuda a Filomena para revisar uma ideia sobre fila fânica em mensagem direta."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-attentive-v1; calls: 1; 6060.84 ms.

## direct-participation-v1-case-02-r4.json

Helena: Filomena, pode me ajudar a revisar esta ideia de uma fila única?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"forte","resumo":"Helena solicita ajuda de Filomena para revisar uma ideia sobre fila fânica."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-attentive-v1; calls: 1; 6679.02 ms.

## direct-participation-v1-case-02-r5.json

Helena: Filomena, pode me ajudar a revisar esta ideia de uma fila única?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena pede ajuda a Filomena para revisar uma ideia técnica sobre fila fânica."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-attentive-v1; calls: 1; 5840.48 ms.

## direct-participation-v1-case-03-r1.json

Helena: Filomena, qual a diferença entre timeout e cancelamento nesse código?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena pergunta a Filomena sobre a diferença técnica entre timeout e cancelamento em um código."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 5871.17 ms.

## direct-participation-v1-case-03-r2.json

Helena: Filomena, qual a diferença entre timeout e cancelamento nesse código?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena pergunta a Filomena sobre a diferença técnica entre timeout e cancelamento em um código."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 6151.20 ms.

## direct-participation-v1-case-03-r3.json

Helena: Filomena, qual a diferença entre timeout e cancelamento nesse código?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena pergunta diretamente a Filomena sobre a diferença técnica entre timeout e cancelamento em um código."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 6022.24 ms.

## direct-participation-v1-case-03-r4.json

Helena: Filomena, qual a diferença entre timeout e cancelamento nesse código?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena pergunta a Filomena sobre a diferença técnica entre timeout e cancelamento em um código."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 6807.45 ms.

## direct-participation-v1-case-03-r5.json

Helena: Filomena, qual a diferença entre timeout e cancelamento nesse código?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena questiona Filomena sobre a diferença técnica entre timeout e cancelamento no código."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 6281.17 ms.

## direct-participation-v1-case-04-r1.json

Helena: Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?

Situation: {"ambiente":"tecnico","envolvimento":"indireto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena questiona se a fila única proposta por Filomena pode bloquear todas as tarefas."}

Presence: {"acao":"responder","continuar_acompanhando":true,"motivo":"continuidade"}

Direct decision: null

Action: responder; state: participation-engaged-v1; calls: 2; 11206.51 ms.

## direct-participation-v1-case-04-r2.json

Helena: Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"fraca","resumo":"Helena questiona se a fila proposta por Filomena pode bloquear tarefas em caso de falha."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 4945.13 ms.

## direct-participation-v1-case-04-r3.json

Helena: Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena questiona se a fila única proposta por Filomena pode causar bloqueio total em caso de falha inicial."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 5950.10 ms.

## direct-participation-v1-case-04-r4.json

Helena: Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?

Situation: {"ambiente":"tecnico","envolvimento":"indireto","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena questiona se a fila proposta por Filomena pode bloquear todas as tarefas em caso de falha."}

Presence: {"acao":"responder","continuar_acompanhando":true,"motivo":"continuidade"}

Direct decision: null

Action: responder; state: participation-engaged-v1; calls: 2; 11832.61 ms.

## direct-participation-v1-case-04-r5.json

Helena: Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?

Situation: {"ambiente":"tecnico","envolvimento":"direto","ritmo":"acontecendo","necessita_cuidado":true,"oportunidade_social":"forte","resumo":"Helena questiona se a fila proposta pode bloquear todas as tarefas caso a primeira trave."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-engaged-v1; calls: 1; 6180.63 ms.

## direct-participation-v1-case-05-r1.json

Helena: O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?

Bia: Escolhi uma comédia para nós duas.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena encerra o assunto técnico e muda para filmes, sendo respondida por Bia sobre a escolha de comédia."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_migrou"}

Direct decision: null

Action: calar; state: participation-attentive-v1; calls: 2; 11330.54 ms.

## direct-participation-v1-case-05-r2.json

Helena: O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?

Bia: Escolhi uma comédia para nós duas.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena muda o assunto de código para filmes e Bia responde que escolheu uma comédia."}

Presence: {"acao":"responder","continuar_acompanhando":true,"motivo":"oportunidade_social"}

Direct decision: null

Action: responder; state: participation-engaged-v1; calls: 2; 12625.10 ms.

## direct-participation-v1-case-05-r3.json

Helena: O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?

Bia: Escolhi uma comédia para nós duas.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena encerra o assunto de código e pergunta a Bia sobre o filme escolhido, que é uma comédia."}

Presence: {"acao":"esperar","continuar_acompanhando":true,"motivo":"conversa_em_andamento"}

Direct decision: null

Action: esperar; state: participation-attentive-v1; calls: 2; 13343.60 ms.

## direct-participation-v1-case-05-r4.json

Helena: O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?

Bia: Escolhi uma comédia para nós duas.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena muda o assunto de código para filmes e Bia responde sobre a escolha da comédia."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-attentive-v1; calls: 2; 12963.72 ms.

## direct-participation-v1-case-05-r5.json

Helena: O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?

Bia: Escolhi uma comédia para nós duas.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"O grupo mudou de um assunto técnico para filmes, com Helena perguntando e Bia respondendo sobre uma comédia."}

Presence: {"acao":"calar","continuar_acompanhando":true,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-attentive-v1; calls: 2; 11672.90 ms.

## direct-participation-v1-case-06-r1.json

Helena: Bia, o pão está na mesa.

Bia: Obrigada, já pego.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena avisa que o pão está na mesa; Bia agradece e confirma que pegará."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 9023.92 ms.

## direct-participation-v1-case-06-r2.json

Helena: Bia, o pão está na mesa.

Bia: Obrigada, já pego.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"encerrando","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Helena informa que o pão está na mesa; Bia agradece e confirma que irá pegá-lo."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 8985.84 ms.

## direct-participation-v1-case-06-r3.json

Helena: Bia, o pão está na mesa.

Bia: Obrigada, já pego.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"nenhuma","resumo":"Helena avisa que o pão está na mesa. Bia agradece e confirma que irá pegar."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 8998.38 ms.

## direct-participation-v1-case-07-r1.json

Gabriel: Bia, você prefere RAII ou coleta de lixo?

Bia: Para esse projeto em C++ prefiro RAII.

Situation: {"ambiente":"tecnico","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Gabriel pergunta sobre RAII e Bia responde preferindo essa abordagem para C++."}

Presence: {"acao":"calar","continuar_acompanhando":true,"motivo":"interesse_no_assunto"}

Direct decision: null

Action: calar; state: participation-attentive-v1; calls: 2; 11147.84 ms.

## direct-participation-v1-case-07-r2.json

Gabriel: Bia, você prefere RAII ou coleta de lixo?

Bia: Para esse projeto em C++ prefiro RAII.

Situation: {"ambiente":"tecnico","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"nenhuma","resumo":"Gabriel pergunta a Bia sobre RAII ou coleta de lixo em C++. Bia responde preferindo RAII."}

Presence: {"acao":"calar","continuar_acompanhando":true,"motivo":"interesse_no_assunto"}

Direct decision: null

Action: calar; state: participation-attentive-v1; calls: 2; 10897.24 ms.

## direct-participation-v1-case-07-r3.json

Gabriel: Bia, você prefere RAII ou coleta de lixo?

Bia: Para esse projeto em C++ prefiro RAII.

Situation: {"ambiente":"tecnico","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Gabriel pergunta a Bia sobre RAII ou coleta de lixo em C++. Bia prefere RAII."}

Presence: {"acao":"esperar","continuar_acompanhando":true,"motivo":"interesse_no_assunto"}

Direct decision: null

Action: esperar; state: participation-attentive-v1; calls: 2; 9991.90 ms.

## direct-participation-v1-case-08-r1.json

Gabriel: Bia, chegou a camiseta com GPU escrito.

Bia: Deixa no sofá.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"encerrando","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Gabriel informa que a camiseta chegou e Bia pede para deixá-la no sofá."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 9555.27 ms.

## direct-participation-v1-case-08-r2.json

Gabriel: Bia, chegou a camiseta com GPU escrito.

Bia: Deixa no sofá.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Gabriel informa a chegada da camiseta e Bia pede para deixá-la no sofá."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 10233.64 ms.

## direct-participation-v1-case-08-r3.json

Gabriel: Bia, chegou a camiseta com GPU escrito.

Bia: Deixa no sofá.

Situation: {"ambiente":"casual","envolvimento":"nenhum","ritmo":"acontecendo","necessita_cuidado":false,"oportunidade_social":"fraca","resumo":"Gabriel avisa Bia sobre uma camiseta e ela indica onde deixá-la no sofá."}

Presence: {"acao":"calar","continuar_acompanhando":false,"motivo":"conversa_entre_outros"}

Direct decision: null

Action: calar; state: participation-inactive-v1; calls: 2; 9321.43 ms.

## direct-participation-v1-case-09-r1.json

Helena: Filomena, só te avisando; não precisa responder.

Situation: {"ambiente":"casual","envolvimento":"direto","ritmo":"encerrando","necessita_cuidado":false,"oportunidade_social":"nenhuma","resumo":"Helena avisa Filomena por DM que ela não precisa responder."}

Presence: null

Direct decision: {"participationDecisionSource":"direct_involvement_rule","acao":"responder","continuar_acompanhando":true,"motivo":"chamada_direta"}

Action: responder; state: participation-inactive-v1; calls: 1; 6341.47 ms.
