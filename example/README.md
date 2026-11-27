# Exemplo de conferência mensal

Estes arquivos simulam o fechamento de março de 2026 da **Aurora Distribuição de Materiais Ltda.** Todos os dados são fictícios e os valores estão em reais.

- [sales.csv](sales.csv): 50 vendas, sendo duas de fevereiro e 48 de março.
- [statement.csv](statement.csv): 52 registros de comissão, referentes a 49 identificadores de venda.

Os CSVs usam UTF-8, ponto e vírgula como separador, valores como `12.680,40` e datas no formato `dd/MM/yyyy`. Os cabeçalhos seguem o formato padrão da aplicação; as colunas adicionais são ignoradas.

## Como experimentar

1. Inicie a aplicação conforme o [guia de execução a partir do código-fonte](../README.md#executar-a-partir-do-código-fonte).
2. Cadastre uma nova empresa chamada `Aurora Distribuição de Materiais Ltda.`.
3. Registre exatamente os acordos abaixo.
4. Em **Nova conferência**, selecione `sales.csv` como vendas e `statement.csv` como demonstrativo de comissões.
5. Execute a conferência e compare com os resultados esperados.

| Início da vigência | Percentual |
| --- | ---: |
| 01/03/2026 | 5% |
| 16/03/2026 | 5,5% |

Informe as datas explicitamente: a interface preenche a data atual por padrão. Não cadastre um acordo anterior, pois as duas vendas de fevereiro devem ficar sem acordo vigente. O cálculo considera a data da venda, mesmo que o demonstrativo tenha sido emitido em abril.

## Resultados esperados

| Resultado | Quantidade |
| --- | ---: |
| Conferido | 36 |
| Divergência | 8 |
| Sem registro no demonstrativo | 4 |
| Venda não localizada | 3 |
| Sem acordo vigente | 2 |
| **Total** | **53** |

O resumo deve apresentar **53 itens, 36 conferidos, 8 divergências e 9 pendências**.

| Total de controle | Valor |
| --- | ---: |
| Vendas brutas, incluindo as 50 vendas | R$ 558.047,01 |
| Comissão esperada, para as 48 vendas com acordo vigente | R$ 28.662,86 |
| Comissão informada, incluindo os 52 registros | R$ 27.936,35 |
| Soma das diferenças nas vendas comparáveis | +R$ 307,18 |

A soma das diferenças considera apenas vendas com acordo vigente e registro no demonstrativo. Como existem registros sem correspondência, subtrair os totais gerais de comissão não produz esse mesmo valor.

## Casos para observar

- `VEN-202603-0012`: comissão informada R$ 25,00 abaixo da esperada.
- `VEN-202603-0019`: comissão informada igual a zero; resulta em divergência, não em ausência de registro.
- `VEN-202603-0023`: diferença de um centavo.
- `VEN-202603-0026`: demonstrativo usa 5% quando já deveria aplicar 5,5%.
- `VEN-202603-0030`: comissão informada R$ 17,25 acima da esperada.
- `VEN-202603-0005`, `VEN-202603-0017` e `VEN-202603-0035`: dois registros no demonstrativo são somados para cada venda.

O exemplo também inclui vendas ausentes no demonstrativo, registros sem venda correspondente e vendas anteriores ao primeiro acordo. Os registros somados demonstram a agregação por venda; não representam controle de recebimentos parciais.

[Voltar à documentação](../README.md)
