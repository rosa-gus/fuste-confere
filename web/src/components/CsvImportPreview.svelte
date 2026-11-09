<script lang="ts">
  import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "carbon-components-svelte";
  import type { ImportPreviewSource } from "../types";

  let { source }: { source: ImportPreviewSource } = $props();
  const dateFormatter = new Intl.DateTimeFormat("pt-BR", { dateStyle: "long", timeZone: "UTC" });

  function formatPreviewDate(value: string): string {
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isNaN(date.getTime()) ? value : dateFormatter.format(date);
  }
  const fields = $derived(source.sourceKind === "sales"
    ? [{ key: "externalId", label: "Identificador" }, { key: "date", label: "Data" }, { key: "grossAmount", label: "Valor bruto" }]
    : [{ key: "saleExternalId", label: "Identificador" }, { key: "commissionAmount", label: "Comissão" }]);
</script>

<p class="csv-preview-caption">{source.totalRows} {source.totalRows === 1 ? "registro" : "registros"} no arquivo. A prévia mostra apenas uma amostra.</p>
{#if source.normalizedRows.length > 0}
  <!-- svelte-ignore a11y_no_noninteractive_tabindex (Allow keyboard scrolling of wide CSV tables.) -->
  <div class="csv-table-scroll" tabindex="0" role="region" aria-label="Prévia dos dados interpretados">
    <Table size="compact">
      <TableHead><TableRow>{#each fields as field (field.key)}<TableHeader>{field.label}</TableHeader>{/each}</TableRow></TableHead>
      <TableBody>
        {#each source.normalizedRows as row, index (index)}
          <TableRow>
            {#each fields as field (field.key)}
              <TableCell>
                {#if field.key === "date" && row.date}
                  <time datetime={row.date}>{formatPreviewDate(row.date)}</time>
                {:else}
                  {row[field.key] ?? "—"}
                {/if}
              </TableCell>
            {/each}
          </TableRow>
        {/each}
      </TableBody>
    </Table>
  </div>
{/if}
<details class="csv-original-preview">
  <summary>Ver amostra original do arquivo</summary>
  <!-- svelte-ignore a11y_no_noninteractive_tabindex (Allow keyboard scrolling of wide CSV tables.) -->
  <div class="csv-table-scroll" tabindex="0" role="region" aria-label="Amostra original do CSV">
    <Table size="compact">
      <TableHead><TableRow>{#each source.headers as header, index (index)}<TableHeader>{header || `Coluna ${index + 1}`}</TableHeader>{/each}</TableRow></TableHead>
      <TableBody>
        {#each source.sampleRows as row, index (index)}
          <TableRow>{#each source.headers as _, column (column)}<TableCell>{row[column] ?? "—"}</TableCell>{/each}</TableRow>
        {/each}
      </TableBody>
    </Table>
  </div>
</details>
