# Registro de validação

Data: 12/11/2026.

Este registro descreve uma execução dos testes. Os comandos para repetir as verificações e os resultados esperados estão no [guia de validação ponta a ponta](README.md).

## Verificações executadas

- Build da API .NET 10: aprovado, sem erros ou avisos.
- Build da interface: aprovado; checagem Svelte sem erros ou avisos.
- `python3 tests/e2e/run.py --report /tmp/conferidor-e2e-report.json`: aprovado, cinco grupos de cenários HTTP com SQLite real.
- Verificações de regras, importação, migrações e persistência SQLite: aprovadas (`dotnet run --no-build --project tests/ProjectConferidor.Persistence.Checks`). O comando reutilizou uma compilação existente; para verificar alterações nesses testes, compile novamente conforme o guia.
- Testes do frontend: aprovados (`npm --prefix web run web:test`, três arquivos, nenhuma falha).

A suíte iniciou uma API própria, usou banco temporário, reiniciou o processo e removeu os dados ao terminar. O relatório JSON foi gerado localmente em `/tmp/conferidor-e2e-report.json` e não está versionado no repositório.

## Verificações não executadas

- Executar e registrar UI-01 a UI-15 do [roteiro manual](README.md#roteiro-manual-da-interface), incluindo navegador, tamanho de tela e eventuais evidências.
- Homologar arquivos anonimizados das origens que serão utilizadas na operação.

Os resultados automatizados acima cobrem a API e a persistência. A validação completa do fluxo permanece pendente até incluir as verificações manuais e os arquivos das origens pretendidas.
