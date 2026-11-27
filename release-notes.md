Primeira release do **Fuste**, aplicativo local para representantes comerciais conferirem comissões de vendas. O Fuste cruza o CSV de vendas faturadas com o demonstrativo da representada, aplica o acordo vigente na data de cada venda e mostra as divergências por item.

Esta versão está disponível para **Linux x64**. O pacote inclui o runtime do .NET e a interface web.

### O que está incluído

- Cadastro de representadas e acordos de comissão por vigência.
- Importação de dois CSVs, com prévia e perfis de mapeamento reutilizáveis.
- Filtros por venda, situação, data e faixa de valor.
- Histórico local em SQLite e exportação completa da conferência em CSV.

### Instalação

Baixe o pacote `.tar.gz`, o checksum `.sha256`, a assinatura `.tar.gz.asc` e a chave pública `fuste-release-key.asc` nos anexos desta release. No diretório dos downloads, confira o checksum e a impressão digital da chave.

```sh
archive=fuste-confere-v0.1.0-linux-x64.tar.gz
sha256sum -c "$archive.sha256"
gpg --show-keys --fingerprint fuste-release-key.asc
```

A impressão digital esperada é `CC13 C81C F798 DBC1 FA01 62C0 DD55 A7AF 9A3F 5232`. Confirme-a por um canal confiável e independente desta release antes de importar a chave. Com o checksum aprovado, verifique a assinatura e execute o binário.

```sh
gpg --import fuste-release-key.asc &&
  gpg --verify "$archive.asc" "$archive" &&
  tar -xzf "$archive" &&
  cd "${archive%.tar.gz}" &&
  ./Fuste.Api
```

Abra [http://127.0.0.1:5080](http://127.0.0.1:5080) no navegador. Para encerrar, pressione `Ctrl+C` no terminal. O banco fica no diretório local de dados do usuário, normalmente em `~/.local/share/Fuste`.
