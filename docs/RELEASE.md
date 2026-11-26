# Publicar uma release do Fuste

O workflow **Packages** gera e testa os pacotes `linux-x64` e `win-x64` a cada push na `main`. No GitHub, abra a execução do commit que será marcado e baixe os ZIPs de **Artifacts**. Extraia de cada um o pacote e o arquivo `.sha256` para anexar à release. Use apenas arquivos da mesma execução. Os artefatos expiram em 14 dias.

O fluxo manual abaixo usa Linux x64. Execute os comandos na raiz do repositório, com o SDK do .NET 10, Node.js e npm. A publicação requer acesso de escrita ao GitHub.

## 1. Defina a versão

Use `MAJOR.MINOR.PATCH` para a versão e `vMAJOR.MINOR.PATCH` para a tag. Use o título **Fuste v0.1.0** para a release deste exemplo.

```sh
version=0.1.0
tag="v$version"
npm --prefix web version "$version" --no-git-tag-version --allow-same-version
```

Se a versão mudar, inclua `web/package.json` e `web/package-lock.json` no commit da release. Gere o pacote a partir desse commit e confirme que ele está no remoto.

## 2. Gere o pacote

Use o nome `fuste-confere-v<VERSÃO>-<RID>.<EXTENSÃO>`. O RID identifica o sistema e a arquitetura. Use `.tar.gz` para Linux e `.zip` para Windows quando houver suporte validado. O checksum recebe o sufixo `.sha256`.

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

## 3. Valide o arquivo gerado

```sh
sha256sum -c "$package.tar.gz.sha256"
test_dir="$(mktemp -d)"
tar -xzf "$package.tar.gz" -C "$test_dir"
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

Crie a tag a partir do mesmo commit usado no pacote e envie apenas essa tag.

```sh
git tag -a "$tag" -m "Fuste $tag"
git push origin "$tag"
```

1. Em **Releases**, abra **Draft a new release** e selecione a tag enviada.
2. Escreva notas curtas com as mudanças, as plataformas testadas e as instruções de atualização ou backup, quando aplicáveis.
3. Anexe os pacotes validados e seus arquivos `.sha256` ao rascunho. Publique a release e confira os arquivos em **Assets**. Para o canal de atualizações do Fuste, use uma release regular de versão superior, com um pacote cujo nome corresponda à plataforma. [Publicação de releases no GitHub](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository#creating-a-release)

## Checks da main

Na proteção da `main`, exija pull request e os checks **CI (Linux x64)** e **CI (Windows x64)** após a primeira execução. O workflow **Packages** roda depois do merge e disponibiliza artefatos para revisão e publicação manual.
