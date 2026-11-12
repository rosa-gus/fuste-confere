# Validação ponta a ponta

Os testes ponta a ponta verificam o caminho entre importar arquivos de vendas e comissões, calcular os resultados e consultar o histórico salvo. Esta pasta contém arquivos de referência, testes automatizados da API HTTP com SQLite e um roteiro para verificar a interface no navegador. Todos os dados dos exemplos são fictícios.

Os testes automatizados enviam requisições à aplicação e conferem as respostas e os dados persistidos. Também verificam se a API serve o HTML da interface compilada. Para conferir a aparência das telas, os filtros, a paginação e as mensagens em português, siga o [roteiro manual](#roteiro-manual-da-interface). Os testes de frontend complementam essas verificações.

## Executar a suíte HTTP

Prepare o ambiente e instale as dependências conforme o [guia de instalação](../../README.md#instalação-e-execução). Você também precisará de Python 3.10 ou superior, sem pacotes Python adicionais. Execute os comandos abaixo na raiz do repositório:

```sh
npm --prefix web run build
dotnet build src/ProjectConferidor.Api
python3 tests/e2e/run.py --report /tmp/conferidor-e2e-report.json
```

O script `run.py` usa a compilação **Debug/net10.0**, inicia uma instância da API acessível apenas neste computador, em uma porta escolhida pelo sistema, e cria uma pasta temporária para os dados. Ao terminar, encerra o processo e remove essa pasta. O banco de uma instalação existente é preservado.

Cada grupo de cenários aprovado imprime uma linha `PASS`. Se algum cenário falhar, o script imprime `FAIL`, mostra o log da API e termina com código 1. O relatório JSON lista os grupos aprovados e o erro, quando houver; `failure: null` indica que todos passaram. A opção `--report` é opcional e aceita outro caminho, desde que a pasta de destino exista. O caminho `/tmp` dos exemplos é adequado a Linux e macOS.

Execute também as verificações complementares:

```sh
dotnet run --project tests/ProjectConferidor.Persistence.Checks
npm --prefix web run web:test
npm --prefix web run web:typecheck
```

| Cenário automatizado | Resultado exigido |
| --- | --- |
| Inicialização | Banco vazio, frontend compilado servido, cadastro de duas empresas e dois acordos. |
| Exemplo mensal | 53 linhas, distribuição das cinco situações e quatro totais financeiros exatos conforme o [exemplo](../../example/README.md). |
| Valores específicos | Diferenças positiva, negativa e de um centavo; comissão zero continua sendo um registro. |
| Layout personalizado | Prévia válida sem salvar histórico; perfil salvo e recuperado; execução com o perfil recuperado. |
| Formatos independentes | Vendas com `;` e decimal com ponto; demonstrativo com `,` e decimal com vírgula entre aspas. |
| Identificadores | Zeros à esquerda preservados e duas comissões somadas para a venda `0001`. |
| Rejeições | Data impossível, valor incompatível, ID de venda repetido e coluna mapeada inexistente: rejeitar sem histórico parcial. |
| Separação de empresas | Histórico vazio na outra empresa; tentativa de excluir conferência alheia retorna 404 e preserva os dados. |
| Reinício | Histórico, valores e configurações das duas conferências e perfil recuperados sem alteração. |

## Arquivos e resultados do layout personalizado

Selecione [mapped-sales.csv](fixtures/mapped-sales.csv) e [mapped-statement.csv](fixtures/mapped-statement.csv). A configuração exata está em [configuration.json](fixtures/configuration.json).

Use `mapped-sales.csv` no campo de vendas e `mapped-statement.csv` no campo de demonstrativo, juntos na mesma conferência. Clique em **Configurar colunas** e ajuste as duas abas conforme a tabela abaixo antes de validar e conferir. Esses arquivos usam cabeçalhos personalizados: no formato padrão, a aplicação informa a ausência das colunas obrigatórias. O demonstrativo contém apenas identificador e comissão; as colunas de data e valor bruto pertencem ao arquivo de vendas.

| Arquivo | Separador | Formato numérico | Data | Mapeamento |
| --- | --- | --- | --- | --- |
| Vendas | Ponto e vírgula | Invariante, decimal com ponto | `dd/MM/yyyy` | ID: `Pedido`; data: `Emissao`; valor: `Faturado`. |
| Demonstrativo | Vírgula | Brasileiro, decimal com vírgula | Não se aplica | ID: `Pedido`; comissão: `Comissao`. |

Cadastre acordos de **5% desde 01/03/2026** e **5,5% desde 16/03/2026**, sem acordo anterior.

| Venda | Situação | Esperado | Informado | Diferença |
| --- | --- | ---: | ---: | ---: |
| `0001` | Conferido | R$ 50,00 | R$ 50,00 | R$ 0,00 |
| `0002` | Divergência | R$ 11,00 | R$ 10,00 | +R$ 1,00 |
| `0003` | Sem registro no demonstrativo | R$ 16,50 | Sem valor | Sem valor |
| `0004` | Sem acordo vigente | Sem valor | Sem valor | Sem valor |
| `0099` | Venda não localizada | Sem valor | R$ 7,00 | Sem valor |

Resumo esperado: **5 itens, 1 conferido, 1 divergência e 3 pendências**. Valores sem comparação devem aparecer como ausência de valor, sem virarem zero.

## Roteiro manual da interface

O roteiro percorre o cadastro, a importação, os resultados e o histórico pela interface existente. Execute os cenários na ordem indicada: eles reutilizam a empresa, os acordos e as conferências criados nas etapas anteriores.

Gere a interface e compile a API com os comandos da seção de execução. Em um terminal na raiz do repositório, use os comandos abaixo para iniciar a aplicação com uma pasta exclusiva para os testes. Este exemplo usa um shell de Linux ou macOS:

```sh
validation_data_dir=$(mktemp -d /tmp/conferidor-aceite-XXXXXX)
echo "$validation_data_dir"
PROJECT_CONFERIDOR_DATA_DIR="$validation_data_dir" dotnet run --no-launch-profile --project src/ProjectConferidor.Api -- --urls http://127.0.0.1:5082
```

Abra `http://127.0.0.1:5082`. Se você personalizou o endereço da API nas opções da aplicação, restaure o padrão para que a interface use a API desse endereço.

Anote o caminho impresso por `echo`. Para testar o reinício, pare a aplicação com `Ctrl+C` e repita apenas o último comando no mesmo terminal. Isso reutiliza a mesma pasta de dados. Em outro terminal, defina `PROJECT_CONFERIDOR_DATA_DIR` com o caminho anotado. A pasta do teste manual permanece disponível para inspeção após encerrar a aplicação.

Registre cada cenário como **aprovado**, **reprovado** ou **não executado**, incluindo navegador, tamanho de tela e evidência de falhas. Os CSVs personalizados e os arquivos inválidos estão em [fixtures/](fixtures/).

| ID | Ação | Aceite |
| --- | --- | --- |
| UI-01 | Cadastrar `Aceite Aurora`, selecioná-la e registrar os dois acordos acima. | Empresa selecionada e percentuais/datas corretos, inclusive 5,5%. |
| UI-02 | Importar os dois CSVs de `example/` no formato padrão e clicar em **Conferir arquivos**. | Resumo: 53 itens, 36 conferidos, 8 divergências, 9 pendências; execução no histórico. |
| UI-03 | Buscar `VEN-202603-0019`, `VEN-202603-0023` e `VEN-202603-0030`, uma por vez. | Zero informado, diferença +R$ 0,01 e diferença −R$ 17,25, respectivamente. |
| UI-04 | Limpar busca e selecionar cada situação. | 36 conferidos, 8 divergências, 4 sem demonstrativo, 3 vendas não localizadas, 2 sem acordo. |
| UI-05 | Selecionar todas as situações; testar páginas com 10 e 25 itens. | Navegação alcança os 53 registros; alterar filtro não deixa a tabela presa em página vazia. |
| UI-06 | Em **Filtros**, selecionar **Diferença**, mínimo `0,01`, máximo `25,00`; depois inverter mínimo e máximo. | Primeiro intervalo contém a venda `0023` e exclui `0030`; intervalo invertido apresenta validação; limpar restaura resultados. |
| UI-07 | Selecionar os CSVs personalizados, clicar em **Configurar colunas**, ajustar o mapeamento conforme a tabela e clicar em **Validar arquivos**. | Prévia válida; ainda existe apenas uma conferência no histórico. |
| UI-08 | Salvar perfil `Layout misto`, conferir e comparar as cinco linhas com a tabela acima. | IDs com zeros preservados; 5 itens, 1 conferido, 1 divergência e 3 pendências; segunda conferência no histórico. |
| UI-09 | Selecionar novamente os arquivos e o perfil salvo, validar; depois trocar um arquivo ou alterar um mapeamento. | Configuração recuperada; mudança invalida a validação anterior e exige nova validação. |
| UI-10 | Usar cada arquivo `duplicate-sales.csv`, `invalid-date-sales.csv`, `invalid-amount-sales.csv` com `fixtures/statement.csv`, selecionando o formato padrão. | Mensagem em português que explique a rejeição; nenhuma nova conferência salva. |
| UI-11 | No layout personalizado, mapear o valor para uma coluna errada e validar. | Erro identificável na prévia; execução não salva resultado inválido. |
| UI-12 | Tentar selecionar ou arrastar arquivos cujo tamanho combinado ultrapasse 9 MB; repetir substituindo um arquivo já selecionado. | Limite informado e arquivos excedentes não aceitos. |
| UI-13 | Cadastrar e selecionar uma segunda empresa; voltar à primeira. | Acordos, perfis e histórico correspondem à empresa selecionada. |
| UI-14 | Recarregar a página; parar a API e reiniciar com a mesma pasta de teste; abrir as duas conferências. | Valores e configurações históricos preservados; perfil continua disponível. |
| UI-15 | Em tela estreita e pelo teclado, percorrer cadastro, seleção de arquivos, configuração e resultados. | Ações acessíveis, foco visível e resultados consultáveis; registrar cortes ou bloqueios. |

## Cobertura e limitações

- Reimportar os mesmos arquivos cria uma nova conferência. A identificação de reimportações está prevista no [roadmap](../../ROADMAP.md).
- Os cenários se concentram em importação, cálculo e histórico. Exportação, pacotes de distribuição, autenticação e backup/restauração ficam fora dessa cobertura.
- Os arquivos de referência representam layouts distintos, mas são fictícios. Para validar uma origem de dados usada na operação, repita o fluxo com exportações reais anonimizadas e confira os resultados esperados.
- Os testes HTTP e o roteiro manual são complementares: as requisições verificam a API e a persistência, enquanto o roteiro verifica a interação no navegador.

## Registrar uma execução

Ao compartilhar resultados em uma contribuição, informe a revisão do código testada, a data, o ambiente, os comandos executados e os cenários aprovados ou com falha. Para os testes manuais, inclua os IDs `UI-01` a `UI-15` e as evidências necessárias para reproduzir problemas. Identifique explicitamente os cenários não executados.
