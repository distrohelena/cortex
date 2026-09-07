# Filomena Launcher — V1

## Objetivo aprovado

Uma aplicação Windows Forms, com janela e ícone próprios, para controlar os
serviços da Filomena sem espalhar terminais entre Codex e Claude.

**Filomena nesta V1 significa somente o editor.** O launcher executa o backend
com `--playground --live-reload`. Nunca acrescenta `--flow-live` e nunca inicia
o modo de respostas da Filomena. Controle de respostas será um recurso separado.

OpenClaw está incluído no controle de serviços por escolha explícita de Helena.
Não alterar sua configuração, seus guards, login ou credenciais. Iniciar o
Gateway não é o mesmo que ativar o runtime de respostas C#; o launcher não
pode prometer que configurações externas de outros agentes estejam desativadas.

## Escolha técnica

Usar um executável WinForms .NET 10 (`net10.0-windows`, `WinExe`), com um
coordenador pequeno e comandos de serviços isolados da UI. Reutilizar o SDK
10.0.400 registrado no projeto. Não embutir o backend no processo da interface.

Alternativas consideradas:

- Scripts com atalhos: menores, mas não resolvem adequadamente status e logs
  numa janela única.
- Windows Service: adequado para operação sem usuário, mas acrescenta instalação,
  permissões e manutenção desnecessárias agora.
- WinForms supervisor: escolhido; atende Start/Stop e mantém os processos atuais.

## Janela

- Título e ícone próprios: Filomena Launcher.
- Botões: **Iniciar tudo**, **Parar tudo**, **Abrir editor**.
- Uma linha por serviço: Docker, LM Studio, FalkorDB, Graphiti, OpenClaw, Editor.
- Status: parado, iniciando, disponível, indisponível ou erro. Detectar serviços
  existentes antes de iniciar; não criar uma segunda instância.
- Área de logs limitada, com horário e serviço. Sem terminais extras para
  comandos de console. Não registrar credenciais, ambiente completo ou payloads.
- Operações assíncronas: a janela continua responsiva. Cliques repetidos não
  iniciam operações concorrentes. Parar durante startup cancela a inicialização.
- Fechar a janela com serviços ativos exige escolher entre cancelar o fechamento,
  deixar os serviços ativos ou executar a parada. Não matar silenciosamente.
- Sem tray, autostart no Windows, editor de prompts ou painel avançado nesta V1.

## Inicialização

1. Detectar Docker Engine; abrir Docker Desktop somente se necessário e esperar
   o Engine, com timeout e erro visível.
2. Detectar LM Studio e seu servidor HTTP; reutilizar a instância existente.
   Usar a CLI instalada para gerenciamento do servidor. Não carregar outra Gemma,
   baixar modelos ou trocar os modelos já carregados automaticamente.
3. Reutilizar os serviços `falkordb` e `graphiti-mcp` do compose existente
   `runtime/docker-compose.yml`, projeto `filomena`, preservando os volumes
   externos atuais. Validar identidade dos containers/volumes antes de agir.
   Nunca executar `down -v`, recriar volumes ou iniciar outros projetos Docker.
4. OpenClaw: usar o comando de Gateway existente, em processo oculto, após
   verificar se já existe Gateway disponível. Não usar `--force` contra uma
   instância que já esteja funcionando; não fazer login ou relink.
5. Iniciar somente o backend do editor e aguardar seu endpoint local
   `http://127.0.0.1:18793/flow`. Abrir a página no navegador pelo botão.

Executar em ordem previsível. Erros interrompem o restante da inicialização e
mostram o serviço responsável; os serviços já iniciados ficam identificados e
podem ser parados pelo botão. Não ocultar falha como sucesso parcial.

O compose também contém PostgreSQL/SpiceDB. Esta V1 não inicia serviços extras
cegamente: eles não são dependências do editor nem do caminho direto de leitura
Graphiti C#. Captura passiva preexistente pode depender deles; seu estado deve
ser informado sem ampliar o launcher para migrações de autorização.

## Prontidão e modelos

Separar processo acessível de dependências operacionais. O `/health` atual do
Graphiti só comprova que o processo HTTP responde. Verificar também FalkorDB e
o endpoint LM Studio; informar se o modelo de embeddings necessário está ausente.
Não chamar Gemma nem escrever memória como health check.

O Graphiti em Docker usa `host.docker.internal:1234`, portanto acesso somente em
`127.0.0.1` no host pode ser insuficiente. Preservar configuração já funcional;
se precisar mudar bind/firewall, informar a necessidade em vez de alterar regras
de firewall silenciosamente. Não mostrar Graphiti como plenamente operacional
se sua conexão com embeddings falhar.

## Parada

Ordem: editor, OpenClaw, Graphiti, FalkorDB, LM Studio. Docker Desktop permanece
aberto, pois existem containers de outros projetos.

Helena autorizou parar LM Studio mesmo sendo uma instância preexistente dedicada
à Filomena. Encerrar servidor/daemon/UI por mecanismos suportados, sem matar
processos genericamente por nomes como `node`, `dotnet` ou `python`.

Registrar quais processos foram iniciados ou reconhecidos pelo launcher.
Processos preexistentes precisam de identidade verificável antes da parada.
Usar parada graciosa e timeout; qualquer encerramento forçado deve exigir
confirmação específica. Falha ao parar um serviço não impede tentar os demais.
Containers próprios são parados, não removidos. Dados e configurações permanecem.

## Configuração e arquivos previstos

- `src/Filomena.Launcher/`: janela, coordenador, configuração de caminhos,
  execução de comandos, detecção dos serviços e ícone.
- `tests/Filomena.Launcher.Tests/`: testes do coordenador com processos e probes
  falsos; nenhuma dependência live obrigatória.
- `Filomena.slnx`: inclusão dos projetos.
- `docs/filomena-launcher.md`: build, abertura e comportamento operacional.

Configuração local separada de prompts/projeto/runtime: caminhos dos executáveis,
repositório e timeouts. Não copiar secrets nem alterar `openclaw.json`,
`appsettings.json` pessoal, Graphiti Core ou `workspace/DREAMS.md`.

Não incluir alterações preexistentes não relacionadas no commit. A pasta do
repositório já contém trabalho pendente que deve ser preservado integralmente.

## Verificação

Testes falsos: ordem Start/Stop; serviço já ativo; clique duplo; falha/timeout;
cancelamento durante startup; falha parcial de parada; somente editor nos
argumentos; nenhum carregamento automático de modelo; nenhum stop do Docker
Desktop; nenhuma remoção de volume/container; rejeição de processo desconhecido.

Build e formatting do escopo novo; `git diff --check`; teste visual da janela
sem iniciar serviços. Teste live Start/Stop fica para Helena, evitando interromper
uma sessão atualmente aberta durante a implementação.

## Fora do escopo

Respostas de chat, novos flows/nodes, recuperação de memória, correções Graphiti,
instalação de SDK/modelos, autostart, serviço Windows, login/relink de canais,
alterações de personalidade e publicação/push.
