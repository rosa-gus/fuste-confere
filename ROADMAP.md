# Roadmap

Checklist para consolidar e publicar o MVP do Fuste. Os próximos passos priorizam a preparação para distribuição e as validações manuais pendentes.

## Base disponível

- [x] Cadastro de empresas representadas e acordos com vigência.
- [x] Conferência de comissão sobre vendas brutas.
- [x] Importação CSV com mapeamento, prévia e perfis reutilizáveis.
- [x] Histórico em SQLite e filtros de resultados.
- [x] Separação entre domínio, aplicação, infraestrutura e API.
- [x] Verificações automatizadas das regras e da persistência.
- [x] Mensagens da API em pt-BR, exibidas diretamente pela interface.
- [x] Rótulos de contexto acima do título em todos os modais, usando o componente do Carbon.

## Validação do núcleo

- [x] Automatizar o fluxo HTTP de cadastro, acordos, importação, conferência e histórico com banco isolado.
- [x] Verificar os resultados financeiros do exemplo mensal e de um layout personalizado.
- [x] Verificar perfis de importação e preservação do histórico após reiniciar a API.
- [x] Validar na interface a rejeição de identificadores repetidos, valores inválidos e datas inválidas.
- [x] Validar na interface a importação com colunas e formatos personalizados.
- [ ] Completar o roteiro manual de filtros, paginação, limite de arquivos, separação de empresas, reinício e acessibilidade.
- [ ] Homologar o fluxo com exportações reais anonimizadas das origens pretendidas.

O [guia de validação ponta a ponta](tests/e2e/README.md) explica como executar os testes HTTP com banco isolado, usar os arquivos de referência e verificar a interface manualmente.

## Reimportações

- [x] Identificar arquivos já processados por empresa pelo conteúdo exato e pela configuração de importação.
- [x] Persistir a identificação das importações, mantendo compatibilidade com o histórico existente.
- [x] Avisar sobre repetições e permitir abrir a conferência anterior ou executar novamente de forma explícita.
- [x] Preservar as conferências anteriores quando arquivos forem reprocessados, inclusive após mudanças nos acordos.
- [x] Evitar duplicações ao repetir a mesma tentativa após uma falha de conexão.
- [x] Verificar automaticamente repetições, mudanças de configuração e acordos, isolamento entre empresas e envios simultâneos.
- [x] Aplicar o padrão transacional do Carbon ao diálogo, com duas ações no rodapé e consulta anterior no corpo.
- [ ] Validar manualmente o diálogo de reimportação, o foco e a recuperação após falhas de rede conforme o roteiro E2E.

## Exportação de resultados

- [x] Definir as colunas e o formato do CSV para a exportação completa.
- [x] Permitir exportar uma conferência salva com identificadores, valores, percentuais, diferenças e situações.
- [x] Verificar automaticamente a fidelidade dos valores exportados e a distinção entre zero e ausência de valor.
- [x] Validar no navegador o download a partir dos resultados e do histórico (UI-21 e UI-22).
- [ ] Adicionar a exportação das linhas que atendem aos filtros ativos.

## Instalação e operação

- [ ] Validar instalação limpa e gerar pacotes de distribuição.
- [ ] Validar e documentar atualização com preservação dos dados.
- [ ] Validar backup e restauração.
- [ ] Definir uma forma de controlar acesso para instalações em rede.

## Extensibilidade

- [ ] Revisar contratos de entrada de dados e pontos de extensão do cálculo.
- [ ] Documentar o desenvolvimento de extensões.
- [ ] Incluir um exemplo mínimo de extensão.
- [ ] Definir compatibilidade dos contratos entre versões.

Integrações com ERP, bancos e outros serviços ficam a cargo de quem utilizar ou adaptar o projeto.

## Marca e divulgação

- [x] Definir Fuste como nome do produto e `fuste-confere` como identificador.
- [ ] Definir identidade visual, logotipo e ícone.
- [ ] Escrever uma apresentação curta do projeto.
- [ ] Criar um showcase com telas e um fluxo de demonstração.
- [ ] Criar banners para o repositório e divulgação.
- [ ] Aplicar a marca na interface e na documentação.

[Voltar à documentação](README.md)
