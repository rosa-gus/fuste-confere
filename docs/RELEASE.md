# Publicar uma release do Fuste

Use os artefatos do workflow **Packages** para preparar a release. O workflow gera os pacotes e os testa. A publicação no GitHub é manual e requer acesso de escrita ao repositório.

## 1. Defina a versão

Use `MAJOR.MINOR.PATCH` para a versão e `vMAJOR.MINOR.PATCH` para a tag. Use o título **Fuste v0.1.0** para a release deste exemplo.

```sh
version=0.1.0
tag="v$version"
npm --prefix web version "$version" --no-git-tag-version --allow-same-version
```

Se a versão mudar, inclua `web/package.json` e `web/package-lock.json` no commit que será publicado. O workflow lê a versão de `web/package.json` e a incorpora ao binário durante o empacotamento.

## 2. Obtenha os pacotes

O workflow **Packages** roda a cada push na `main` e também pode ser disparado manualmente nessa branch. No GitHub Actions, abra a execução correspondente ao commit que será publicado e baixe os ZIPs de **Artifacts** da mesma execução. Eles expiram em 14 dias.

Cada ZIP de artefato contém um pacote e seu arquivo de checksum `.sha256`. Extraia esses dois arquivos para cada plataforma. São eles que serão anexados à release.

| Plataforma | Pacote |
| --- | --- |
| Linux x64 | `fuste-confere-v<VERSÃO>-linux-x64.tar.gz` |
| Windows x64 | `fuste-confere-v<VERSÃO>-win-x64.zip` |

### Gerar localmente no Linux x64

Como alternativa ao workflow para Linux x64, execute os comandos abaixo na raiz do repositório. Use o commit que será publicado, o SDK do .NET 10, Node.js e npm.

```sh
rid=linux-x64
package="fuste-confere-$tag-$rid"
staging_dir="$(mktemp -d)"

npm --prefix web ci
npm --prefix web run build

dotnet publish src/Fuste.Api/Fuste.Api.csproj \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  -p:Version="$version" \
  --output "$staging_dir/$package"

tar -C "$staging_dir" -czf "$package.tar.gz" "$package"
sha256sum "$package.tar.gz" > "$package.tar.gz.sha256"
```

Avance após todos os comandos concluírem com sucesso. O pacote deve conter o executável, o runtime .NET, a interface em `wwwroot` e os arquivos de configuração e licença. [Publicação no .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

## 3. Valide os pacotes

O workflow extrai cada pacote, inicia o executável e roda a suíte HTTP em uma pasta de dados temporária. A suíte também confere a versão e o RID da instalação. Verifique se os jobs das duas plataformas passaram na execução usada para a release. [Guia de validação ponta a ponta](../tests/e2e/README.md#executar-a-suíte-http)

Para conferir um pacote Linux localmente, verifique o checksum, extraia o pacote e rode a mesma suíte. Depois, inicie o binário para o aceite no navegador.

```sh
package="fuste-confere-$tag-linux-x64"
sha256sum -c "$package.tar.gz.sha256"
test_dir="$(mktemp -d)"
tar -xzf "$package.tar.gz" -C "$test_dir"
python3 tests/e2e/run.py --package-dir "$test_dir/$package" --package-version "$version" --package-rid linux-x64
(
  cd "$test_dir/$package" &&
    FUSTE_CONFERE_DATA_DIR="$test_dir/dados" ./Fuste.Api --urls http://127.0.0.1:5082
)
```

1. Abra **http://127.0.0.1:5082** e confira a versão no rodapé.
2. Rode o exemplo mensal. Confirme os **53 itens** e baixe o CSV. [Exemplo e resultados esperados](../example/README.md)
3. Pare a API com `Ctrl+C` e reinicie usando o mesmo diretório de dados. Confira o histórico e o CSV novamente.
4. A partir da segunda release, repita o teste com uma cópia do banco da versão anterior. Confirme que o histórico abre e que uma nova conferência pode ser salva.

## 4. Publique no GitHub

Crie a tag apontando explicitamente para o SHA do commit usado nos pacotes. Substitua `SHA_DO_COMMIT` pelo SHA da execução do workflow ou da compilação local validada.

```sh
git tag -a "$tag" SHA_DO_COMMIT -m "Fuste $tag"
git push origin "$tag"
```

1. Em **Releases**, abra **Draft a new release** e selecione a tag enviada.
2. Escreva notas curtas com as mudanças, as plataformas testadas e as instruções de atualização ou backup, quando aplicáveis.
3. Anexe os pacotes validados e seus arquivos `.sha256` ao rascunho. Publique a release e confira os arquivos em **Assets**. Para o canal de atualizações do Fuste, use uma release regular de versão superior, com um pacote cujo nome corresponda à plataforma. [Publicação de releases no GitHub](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository#creating-a-release)

## Remover o Fuste

1. Pare o Fuste com `Ctrl+C` no terminal em que ele está rodando.
2. Exclua a pasta extraída que contém `Fuste.Api` no Linux ou `Fuste.Api.exe` no Windows. Se quiser liberar mais espaço, exclua também o arquivo do pacote e seu checksum. O histórico fica na pasta de dados.
3. Se quiser apagar o histórico e os demais dados, faça um backup se precisar recuperá-los. Depois, com o Fuste parado, remova `fuste-confere.db` e os arquivos auxiliares `-wal`, `-shm` e `-journal`, se existirem. Use a pasta indicada em `FUSTE_CONFERE_DATA_DIR` ou a pasta padrão, normalmente `~/.local/share/Fuste` no Linux e `%LOCALAPPDATA%\Fuste` no Windows. [Dados locais e backup](../README.md#dados-locais-e-backup)
4. Para remover também as preferências do navegador, apague as chaves que começam com `fuste-confere:` no armazenamento local do endereço em que você usava o Fuste.

Para limpar apenas o banco e continuar usando o Fuste, abra **Opções → Banco de dados → Redefinir banco de dados**.

## Checks da main

Na proteção da `main`, exija pull request e os checks **CI (Linux x64)** e **CI (Windows x64)** após a primeira execução. O workflow **Packages** roda depois do merge e disponibiliza artefatos para revisão e publicação manual.
