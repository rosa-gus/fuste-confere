<script lang="ts">
  import { Select, SelectItem } from "carbon-components-svelte";
  import { getImportDateFormatHelp, resolveImportColumn } from "../csv-import";
  import type { ImportSourceKind, SourceImportConfiguration } from "../types";

  let { kind, configuration, headers, disabled = false, onChange }: {
    kind: ImportSourceKind;
    configuration: SourceImportConfiguration;
    headers: string[];
    disabled?: boolean;
    onChange: (configuration: SourceImportConfiguration) => void;
  } = $props();

  const fields = $derived(kind === "sales"
    ? [{ key: "externalId", label: "Identificador da venda" }, { key: "date", label: "Data da venda" }, { key: "grossAmount", label: "Valor bruto" }]
    : [{ key: "saleExternalId", label: "Identificador da venda" }, { key: "commissionAmount", label: "Comissão informada" }]);

  function setColumn(field: string, value: string): void {
    onChange({ ...configuration, columns: { ...configuration.columns, [field]: value } });
  }
</script>

<div class="csv-mapping-fields">
  {#each fields as field (field.key)}
    {@const configuredColumn = configuration.columns[field.key] ?? ""}
    {@const columnMatch = resolveImportColumn(configuredColumn, headers)}
    {@const selectedColumn = columnMatch.selected}
    <Select
      id={`csv-${kind}-${field.key}`}
      labelText={field.label}
      selected={selectedColumn}
      {disabled}
      invalid={columnMatch.status !== "matched"}
      invalidText={columnMatch.status === "ambiguous" ? "O cabeçalho é ambíguo. Renomeie as colunas repetidas no arquivo." : "Selecione a coluna correspondente no arquivo."}
      onchange={(event) => setColumn(field.key, event.currentTarget.value)}
    >
      <SelectItem value="" text="Selecione uma coluna" />
      {#if selectedColumn && columnMatch.status === "missing"}
        <SelectItem value={selectedColumn} text={`${selectedColumn} (não encontrada)`} />
      {/if}
      {#each headers as header, index (`${index}-${header}`)}
        <SelectItem value={header} text={header || `Coluna ${index + 1} sem título`} />
      {/each}
    </Select>
  {/each}
</div>

<div class="csv-source-formats">
  <Select
    id={`csv-${kind}-delimiter`}
    labelText="Separador de colunas"
    selected={configuration.delimiter}
    {disabled}
    onchange={(event) => onChange({ ...configuration, delimiter: event.currentTarget.value as SourceImportConfiguration["delimiter"] })}
  >
    <SelectItem value="auto" text="Detectar automaticamente" />
    <SelectItem value=";" text="Ponto e vírgula (;)" />
    <SelectItem value="," text="Vírgula (,)" />
  </Select>
  <Select
    id={`csv-${kind}-number-format`}
    labelText="Formato dos valores"
    selected={configuration.numberFormat}
    {disabled}
    onchange={(event) => onChange({ ...configuration, numberFormat: event.currentTarget.value as SourceImportConfiguration["numberFormat"] })}
  >
    <SelectItem value="pt-BR" text="1.250,50 (decimal com vírgula)" />
    <SelectItem value="invariant" text="1,250.50 (decimal com ponto)" />
  </Select>
  {#if kind === "sales"}
    <Select
      id="csv-sales-date-format"
      labelText="Formato da data"
      helperText={getImportDateFormatHelp(configuration.dateFormat)}
      selected={configuration.dateFormat}
      {disabled}
      onchange={(event) => onChange({ ...configuration, dateFormat: event.currentTarget.value as SourceImportConfiguration["dateFormat"] })}
    >
      <SelectItem value="auto" text="Detectar automaticamente" />
      <SelectItem value="dd/MM/yyyy" text="Dia/mês/ano" />
      <SelectItem value="yyyy-MM-dd" text="Ano-mês-dia" />
    </Select>
  {/if}
</div>
