<h1 align="center">Fuste</h1>

![Fuste, conferência de comissões](docs/fuste-banner-github.png)

**Fuste** é um aplicativo local para representantes comerciais que precisam conferir comissões de vendas. Ele cruza as vendas faturadas com o demonstrativo enviado pela representada, calcula quanto era esperado conforme o acordo vigente e mostra as diferenças por venda. O identificador do projeto é `fuste-confere`.

O projeto está em fase de **MVP**. O fluxo principal está disponível, mas pode haver instabilidades e lacunas de funcionalidade. Consulte os [limites técnicos](#limites-técnicos) para verificar se o seu processo cabe no escopo atual.

## Instalar no Linux x64

O pacote da v0.1.0 inclui o runtime do .NET e a interface web. Baixe o arquivo `.tar.gz`, o checksum `.sha256`, a assinatura `.tar.gz.asc` e a chave pública `fuste-release-key.asc` na [página da release v0.1.0](https://github.com/rosa-gus/fuste-confere/releases/tag/v0.1.0).

No diretório dos downloads, confira o checksum e compare a impressão digital da chave com a indicada nas [notas da release](release-notes.md).

```sh
archive=fuste-confere-v0.1.0-linux-x64.tar.gz
sha256sum -c "$archive.sha256"
gpg --show-keys --fingerprint fuste-release-key.asc
```

```sh
gpg --import fuste-release-key.asc &&
  gpg --verify "$archive.asc" "$archive" &&
  tar -xzf "$archive" &&
  cd "${archive%.tar.gz}" &&
  ./Fuste.Api
```

Abra [http://127.0.0.1:5080](http://127.0.0.1:5080) no navegador. Encerre o Fuste com `Ctrl+C` no terminal. Consulte [Dados locais e backup](#dados-locais-e-backup) para localizar o banco.

## Executar a partir do código-fonte

Você precisa do SDK do .NET 10, de uma versão do Node.js compatível com o Vite 8 (22.12+ na linha 22 ou 24, por exemplo) e do npm. Execute os comandos abaixo na raiz do repositório.

```sh
npm --prefix web ci
npm --prefix web run build
dotnet run --project src/Fuste.Api
```

Abra **http://127.0.0.1:5080**. A API serve a interface compilada em `src/Fuste.Api/wwwroot` e usa SQLite como banco local.

## Primeira conferência

1. Cadastre e selecione uma representada.
2. Cadastre pelo menos um acordo com a data de início e o percentual. Use `5` para representar 5%.
3. Em **Nova conferência**, selecione o CSV de vendas e o CSV do demonstrativo. Se precisar de outro layout, abra **Configurar colunas**, revise o mapeamento e os formatos de cada arquivo e clique em **Validar arquivos**. Você pode salvar essa configuração como um perfil da representada. Revalide após trocar um arquivo ou alterar a configuração.
4. Clique em **Conferir arquivos**. Use os filtros para investigar diferenças e o histórico para reabrir uma conferência.
5. Em **Itens da conferência**, use **Baixar CSV** para exportar a conferência salva. A exportação inclui a data, os valores, o percentual, a diferença e a situação de cada venda.

O [exemplo mensal](example/README.md) traz arquivos fictícios, acordos e resultados esperados. O guia de [validação ponta a ponta](tests/e2e/README.md) também inclui um exemplo com colunas e formatos personalizados.

### Arquivos de entrada

| Arquivo | Cabeçalhos padrão | Alternativas em português |
| --- | --- | --- |
| Vendas | `sale_id,sale_date,gross_amount` | `id_venda,data_venda,valor_bruto` |
| Demonstrativo | `sale_id,commission_amount` | `id_venda,valor_comissao` |

Use o mesmo identificador nos dois arquivos. Cada venda deve aparecer uma única vez no arquivo de vendas. Vários lançamentos de comissão para a mesma venda são somados no demonstrativo. No formato padrão, use vírgula como separador com decimais como `1250.50`, ou ponto e vírgula com decimais como `1.250,50`. As datas podem estar em `yyyy-MM-dd` ou `dd/MM/yyyy`. O mapeamento personalizado permite escolher o formato numérico independentemente do separador.

O perfil guarda as colunas e os formatos de leitura dos dois CSVs. Para reutilizá-lo, escolha-o em **Formato dos arquivos** após selecionar a representada.

### Como interpretar o resultado

O Fuste aplica o acordo mais recente vigente na data da venda ao seu valor bruto faturado. São usados valores decimais e arredondamento para centavos, com empates arredondados para longe de zero. A diferença é a **comissão esperada menos a informada**. Uma diferença positiva indica que a comissão informada ficou abaixo da esperada.

| Situação | Significado |
| --- | --- |
| Conferido | A comissão esperada coincide com a informada. |
| Divergência | As duas comissões têm valores diferentes. |
| Sem registro no demonstrativo | A venda existe e não tem lançamento de comissão. |
| Venda não localizada | O demonstrativo informa comissão para um ID ausente no arquivo de vendas. |
| Sem acordo vigente | A data da venda não tem um percentual aplicável. |

Você pode filtrar a tabela por ID da venda, situação, data da venda e faixa de valor bruto, comissão esperada, comissão informada ou diferença. Uma linha com data ou valor ausente fica fora quando o filtro correspondente estiver ativo. **Baixar CSV** inclui todas as linhas, inclusive as que estiverem fora dos filtros ou da página aberta.

Quando um valor está indisponível, o CSV de resultados deixa o campo em branco. Um valor monetário igual a zero aparece como `0,00`. O CSV usa UTF-8 com BOM, ponto e vírgula e vírgula decimal. Ao importar em uma planilha, selecione o tipo texto para **ID da venda** para preservar zeros à esquerda. IDs que possam ser interpretados como fórmulas recebem um apóstrofo de proteção.

### Reimportações

O histórico preserva os valores e o percentual usados em cada execução. Ao reenviar arquivos idênticos para a mesma empresa, com a mesma configuração, o Fuste permite abrir o resultado anterior ou confirmar uma nova execução com os acordos atuais. A nova conferência mantém a anterior no histórico.

### Atualizações

Em **Opções → Atualizações**, consulte a versão em uso. Nos pacotes compatíveis, o Fuste consulta o GitHub Releases ao iniciar e a cada 12 horas. Na execução comum com `dotnet run`, a verificação fica desativada.

Quando uma versão mais recente oferece um pacote compatível, a tela mostra os links de download e das notas da versão. A instalação é manual. Para desativar a consulta em um pacote, inicie o Fuste com `FusteUpdates__Enabled=false`.

## Dados locais e backup

Por padrão, a interface usa a API do mesmo endereço. Em **Opções → Conexão**, você pode informar outra URL de API. Por padrão, a API escuta em `127.0.0.1:5080`. Ela não oferece autenticação, portanto uma instalação em rede precisa de controle de acesso externo.

O SQLite fica no computador em que a API roda. Por padrão, o arquivo `fuste-confere.db` fica na pasta `Fuste` do diretório de dados locais do usuário. Defina `FUSTE_CONFERE_DATA_DIR` no ambiente da API para escolher outra pasta.

Para fazer backup, pare a aplicação e copie a pasta de dados inteira. Para restaurar, pare a aplicação e aponte `FUSTE_CONFERE_DATA_DIR` para uma pasta dedicada com a cópia. Os arquivos CSV de origem não são armazenados no banco. Guarde-os se precisar reprocessar uma conferência.

Excluir uma representada remove seus acordos, perfis e histórico. **Opções → Banco de dados → Redefinir banco de dados** apaga todos os registros da API em uso e mantém as preferências do navegador.

## Limites técnicos

- **Cálculo.** A regra atual aplica um percentual sobre o valor bruto faturado. Recebimentos parciais, estornos, regras de comissão por produto ou faixa e gestão de contestações estão fora do escopo atual.
- **Importação.** A entrada é por dois arquivos CSV, com limite combinado de 9 MB na interface. A aplicação rejeita registros inválidos sem salvar uma conferência parcial. A detecção de reimportações usa os bytes dos arquivos e a configuração de leitura, então alterações de formatação podem fazer o envio ser tratado como novo.
- **Integrações.** O MVP opera por arquivos e ainda não integra ERP, bancos ou marketplaces.

## Desenvolvimento e verificação

O backend usa .NET 10, ASP.NET Core e SQLite. Os projetos `Fuste.Domain`, `Fuste.Application`, `Fuste.Infrastructure` e `Fuste.Api` separam regras, casos de uso, acesso a dados e HTTP. A interface em `web/` usa Svelte, TypeScript, Vite e o Carbon Design System da IBM.

Para editar a interface, mantenha a API em execução e rode `npm --prefix web run dev`. Acesse **http://127.0.0.1:5173**. O Vite encaminha as chamadas à API da porta 5080.

```sh
dotnet build fuste-confere.slnx
dotnet run --project tests/Fuste.Persistence.Checks
dotnet run --project tests/Fuste.Updates.Checks
npm --prefix web run web:test
npm --prefix web run web:typecheck
python3 tests/e2e/run.py
```

A suíte HTTP usa um banco temporário. Gere a interface e compile a solução antes de executá-la. O [guia de validação](tests/e2e/README.md) inclui cenários automatizados e o roteiro de aceite pelo navegador.

Para gerar e publicar pacotes, siga o [guia de release](docs/RELEASE.md). Os planos para extensibilidade estão no [roadmap](ROADMAP.md).

## Licença

Copyright 2026 Rosa Gus

Fuste é distribuído sob a [Apache License 2.0](LICENSE). Consulte o [NOTICE](NOTICE) para as atribuições de dependências e fontes.
