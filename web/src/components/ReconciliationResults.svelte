<script lang="ts">
  import SectionHeader from "./SectionHeader.svelte";
  import { Button, Divider, InlineNotification, Pagination, Search, Select, SelectItem, Tag, TextInput } from "carbon-components-svelte";
  import Download from "carbon-icons-svelte/lib/Download.svelte";
  import Filter from "carbon-icons-svelte/lib/Filter.svelte";
  import Information from "carbon-icons-svelte/lib/Information.svelte";
  import { api, ApiError } from "../api";
  import { formatCurrency, formatDate, formatDateTime, formatDifference, formatPercentage, statusLabels } from "../format";
  import { filterResultLines, paginateResultLines, validateResultFilters, type AmountField, type ResultFilters } from "../reconciliation-results";
  import type { Reconciliation, ReconciliationStatus } from "../types";

  let { reconciliation }: { reconciliation: Reconciliation } = $props();
  let search = $state("");
  let status = $state<ReconciliationStatus | "all">("all");
  let dateFrom = $state("");
  let dateTo = $state("");
  let amountField = $state<AmountField>("grossAmount");
  let minimum = $state("");
  let maximum = $state("");
  let showFilters = $state(false);
  let page = $state(1);
  let pageSize = $state(25);
  let downloading = $state(false);
  let downloadError = $state("");

  const statuses = Object.entries(statusLabels) as [ReconciliationStatus, string][];
  const filters = $derived<ResultFilters>({ search, status, dateFrom, dateTo, amountField, minimum, maximum });
  const errors = $derived(validateResultFilters(filters));
  const invalid = $derived(Object.keys(errors).length > 0);
  const filtered = $derived(filterResultLines(reconciliation.lines, filters));
  const paginated = $derived(paginateResultLines(filtered, page, pageSize));
  const advancedCount = $derived(Number(!!(dateFrom || dateTo)) + Number(!!(minimum.trim() || maximum.trim())));
  const hasFilters = $derived(!!search.trim() || status !== "all" || advancedCount > 0);

  $effect(() => {
    // Reading only filters and size keeps ordinary page navigation intact.
    filters;
    pageSize;
    page = 1;
  });

  function clearFilters(): void {
    search = "";
    status = "all";
    dateFrom = "";
    dateTo = "";
    amountField = "grossAmount";
    minimum = "";
    maximum = "";
    page = 1;
  }

  async function downloadCsv(): Promise<void> {
    if (downloading) return;
    downloading = true;
    downloadError = "";
    const { id } = reconciliation;
    try {
      const csv = await api.exportReconciliation(id);
      const url = URL.createObjectURL(csv);
      const link = document.createElement("a");
      link.href = url;
      link.download = `conferencia-${id}.csv`;
      document.body.append(link);
      try {
        link.click();
      } finally {
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      }
    } catch (error) {
      downloadError = error instanceof ApiError ? error.message : "O download falhou. Tente novamente.";
    } finally {
      downloading = false;
    }
  }

  function statusTagType(value: ReconciliationStatus): "green" | "red" | "cool-gray" | "blue" | "purple" {
    switch (value) {
      case "Matched": return "green";
      case "Difference": return "red";
      case "MissingFromStatement": return "cool-gray";
      case "UnrecognizedSale": return "blue";
      case "MissingAgreement": return "purple";
    }
  }
</script>

<section class="results-section" aria-labelledby="results-title">
  <SectionHeader
    class="results-heading"
    eyebrow="RESULTADO"
    title="Itens da conferência"
    headingId="results-title"
  >
    {#snippet titleEnd()}<span class="result-date">{formatDateTime(reconciliation.createdAt)}</span>{/snippet}
    {#snippet actions()}
      <Button kind="tertiary" size="field" icon={Download} disabled={downloading} onclick={downloadCsv}>
        {downloading ? "Preparando CSV…" : "Baixar CSV"}
      </Button>
    {/snippet}
  </SectionHeader>

  {#if downloadError}
    <InlineNotification kind="error" title="Não foi possível baixar o CSV" subtitle={downloadError} hideCloseButton />
  {/if}

  <div class="results-toolbar">
    <div class="results-search">
      <Search
        id="result-search"
        size="lg"
        labelText="Buscar pelo ID da venda"
        placeholder="Buscar pelo ID da venda…"
        closeButtonLabelText="Limpar busca"
        bind:value={search}
      />
    </div>
    <div class="results-status-select">
      <Select id="result-status" labelText="Situação" hideLabel bind:selected={status}>
        <SelectItem value="all" text="Todas as situações" />
        {#each statuses as [value, label] (value)}
          <SelectItem {value} text={label} />
        {/each}
      </Select>
    </div>
    <Button
      kind="tertiary"
      size="field"
      icon={Filter}
      aria-expanded={showFilters}
      aria-controls="result-filters"
      onclick={() => (showFilters = !showFilters)}
    >Filtros{#if advancedCount}<Divider orientation="vertical" decorative class="inline-separator" margin={3} />{advancedCount}{/if}</Button>
  </div>

  {#if showFilters}
    <div id="result-filters" class="results-filters">
      <fieldset class="results-filter-group">
        <legend>Data da venda</legend>
        <div class="results-date-fields">
          <TextInput id="result-date-from" type="date" labelText="De" bind:value={dateFrom} invalid={!!errors.dateRange} />
          <TextInput id="result-date-to" type="date" labelText="Até" bind:value={dateTo} invalid={!!errors.dateRange} invalidText={errors.dateRange ?? ""} />
        </div>
      </fieldset>
      <fieldset class="results-filter-group">
        <legend>Valor</legend>
        <div class="results-amount-fields">
          <Select id="result-amount-field" labelText="Campo" bind:selected={amountField}>
            <SelectItem value="grossAmount" text="Valor bruto" />
            <SelectItem value="expectedCommission" text="Esperado" />
            <SelectItem value="reportedCommission" text="Informado" />
            <SelectItem value="difference" text="Diferença" />
          </Select>
          <TextInput
            id="result-minimum"
            labelText="Mínimo (R$)"
            inputmode="decimal"
            placeholder="Ex.: 1.234,56"
            bind:value={minimum}
            invalid={!!errors.minimum}
            invalidText={errors.minimum ?? ""}
          />
          <TextInput
            id="result-maximum"
            labelText="Máximo (R$)"
            inputmode="decimal"
            placeholder="Ex.: 5.000,00"
            bind:value={maximum}
            invalid={!!errors.maximum}
            invalidText={errors.maximum ?? ""}
          />
        </div>
        {#if amountField === "difference"}
          <p class="results-filter-help">Valores negativos indicam comissão informada maior que a esperada.</p>
        {/if}
      </fieldset>
      <div class="results-filter-actions">
        <Button kind="ghost" size="small" disabled={!hasFilters && amountField === "grossAmount"} onclick={clearFilters}>Limpar filtros</Button>
      </div>
    </div>
  {/if}

  <p class="bx--visually-hidden" role="status" aria-live="polite">
    {invalid ? "Ajuste os filtros para visualizar os resultados." : `${filtered.length} de ${reconciliation.lines.length} itens encontrados.`}
  </p>

  <div class="table-wrap">
    <table>
      <caption class="bx--visually-hidden">Itens da conferência de {formatDateTime(reconciliation.createdAt)}</caption>
      <thead>
        <tr>
          <th scope="col">Venda</th><th scope="col">Data</th><th scope="col">Valor bruto</th><th scope="col">Comissão</th>
          <th scope="col">Esperado</th><th scope="col">Informado</th><th scope="col">Diferença</th><th scope="col">Situação</th>
        </tr>
      </thead>
      <tbody>
        {#each paginated.items as line, index (`${line.saleExternalId}-${line.status}-${index}`)}
          <tr>
            <td><span class="sale-id">{line.saleExternalId}</span></td>
            <td>{formatDate(line.saleDate)}</td>
            <td>{formatCurrency(line.grossAmount)}</td>
            <td>{formatPercentage(line.commissionPercentage)}</td>
            <td>{formatCurrency(line.expectedCommission)}</td>
            <td>{formatCurrency(line.reportedCommission)}</td>
            <td class:positive-difference={line.difference !== null && line.difference > 0} class:negative-difference={line.difference !== null && line.difference < 0}>{formatDifference(line.difference)}</td>
            <td><Tag type={statusTagType(line.status)} size="sm">{statusLabels[line.status]}</Tag></td>
          </tr>
        {:else}
          <tr>
            <td class="table-empty" colspan="8">
              <div class="results-empty">
                <p>{invalid ? "Ajuste os filtros para visualizar os resultados." : reconciliation.lines.length ? "Nenhum resultado encontrado." : "Esta conferência não possui itens."}</p>
                {#if hasFilters}<Button kind="ghost" size="small" onclick={clearFilters}>Limpar filtros</Button>{/if}
              </div>
            </td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
  <nav class="results-pagination" aria-label="Paginação dos itens da conferência">
    <Pagination
      bind:page
      bind:pageSize
      pageSizes={[10, 25, 50, 100]}
      totalItems={filtered.length}
      itemsPerPageText="Itens por página:"
      forwardText="Próxima página"
      backwardText="Página anterior"
      itemRangeText={(min, max, total) => total ? `${min}–${max} de ${total} resultados` : "0 resultados"}
      pageText={(value) => `Página ${value}`}
      pageRangeText={(_current, total) => `de ${total} ${total === 1 ? "página" : "páginas"}`}
      pageSelectLabelText={(total) => `Número da página, de ${total} ${total === 1 ? "página" : "páginas"}`}
    />
  </nav>
  <p class="table-note"><Information size={16} aria-hidden="true" /> A diferença é a comissão esperada menos a informada. Os filtros afetam somente esta tabela.</p>
</section>
