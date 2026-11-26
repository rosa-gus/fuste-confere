<script lang="ts">
  import { onMount, tick } from "svelte";
  import CsvImportConfigurationPanel from "./components/CsvImportConfigurationPanel.svelte";
  import SectionHeader from "./components/SectionHeader.svelte";
  import ToastRegion from "./components/ToastRegion.svelte";
  import OptionsMenu from "./components/OptionsMenu.svelte";
  import DeleteCompanyDialog from "./components/DeleteCompanyDialog.svelte";
  import DuplicateImportDialog from "./components/DuplicateImportDialog.svelte";
  import DeleteHistoryDialog from "./components/DeleteHistoryDialog.svelte";
  import HistoryActions from "./components/HistoryActions.svelte";
  import ReconciliationResults from "./components/ReconciliationResults.svelte";
  import { getApiSettings } from "./api-settings";
  import {
    Button,
    Divider,
    ToastNotification,
    NumberInput,
    Select,
    SelectItem,
    Tag,
    TextInput,
  } from "carbon-components-svelte";
  import Add from "carbon-icons-svelte/lib/Add.svelte";
  import ArrowRight from "carbon-icons-svelte/lib/ArrowRight.svelte";
  import Building from "carbon-icons-svelte/lib/Building.svelte";
  import CheckmarkFilled from "carbon-icons-svelte/lib/CheckmarkFilled.svelte";
  import CloudUpload from "carbon-icons-svelte/lib/CloudUpload.svelte";
  import Compare from "carbon-icons-svelte/lib/Compare.svelte";
  import Document from "carbon-icons-svelte/lib/Document.svelte";
  import DocumentImport from "carbon-icons-svelte/lib/DocumentImport.svelte";
  import TrashCan from "carbon-icons-svelte/lib/TrashCan.svelte";
  import Information from "carbon-icons-svelte/lib/Information.svelte";
  import Time from "carbon-icons-svelte/lib/Time.svelte";
  import WarningAlt from "carbon-icons-svelte/lib/WarningAlt.svelte";
  import DataBase from "carbon-icons-svelte/lib/DataBase.svelte";
  import Launch from "carbon-icons-svelte/lib/Launch.svelte";
  import { api, ApiError } from "./api";
  import packageJson from "../package.json";
  import {
    formatDate,
    formatDateTime,
    formatPercentage,
  } from "./format";
  import type {
    Agreement,
    Company,
    HistoryDeletionTarget,
    ImportConfiguration,
    Reconciliation,
    UpdateStatus,
  } from "./types";

  function todayLocal(): string {
    const now = new Date();
    const offset = now.getTimezoneOffset() * 60_000;
    return new Date(now.getTime() - offset).toISOString().slice(0, 10);
  }

  let companies = $state<Company[]>([]);
  let agreements = $state<Agreement[]>([]);
  let reconciliations = $state<Reconciliation[]>([]);
  let activeReconciliation = $state<Reconciliation | null>(null);
  let selectedCompanyId = $state("");
  let loading = $state(true);
  let apiStatus = $state<"checking" | "connected" | "disconnected">("checking");
  let updates = $state<UpdateStatus | null>(null);
  let updatesError = $state(false);
  let fetchingUpdates = false;
  let workspaceLoading = $state(false);
  let creatingCompany = $state(false);
  let savingAgreement = $state(false);
  let reconciling = $state(false);
  let showCompanyForm = $state(false);
  let companyName = $state("");
  let companyToDelete = $state<Company | null>(null);
  let historyToDelete = $state<HistoryDeletionTarget | null>(null);
  let reconciliationRequestId = 0;
  let workspaceRequestId = $state(0);
  let agreementDate = $state(todayLocal());
  let agreementPercentage = $state<number | null>(5);
  let salesFile = $state<File | null>(null);
  let statementFile = $state<File | null>(null);
  let importConfiguration = $state<ImportConfiguration | null>(null);
  let importValidated = $state(false);
  let importSetupBusy = $state(false);
  let importExecutionError = $state("");
  let duplicateImport = $state<{ id: string; createdAt: string } | null>(null);
  let openingDuplicate = $state(false);
  let importAttempt: { requestId: string; reprocessOf?: string } | null = null;
  let importConfigurationRevision = 0;

  function resetImportAttempt(): void {
    duplicateImport = null;
    importAttempt = null;
  }

  function closeDuplicateImport(): void {
    if (reconciling || openingDuplicate) return;
    resetImportAttempt();
    errorMessage = "";
  }

  async function openPreviousImport(): Promise<void> {
    if (!duplicateImport || reconciling || openingDuplicate) return;
    const previous = duplicateImport;
    const companyId = selectedCompanyId;
    const revision = workspaceRequestId;
    openingDuplicate = true;
    errorMessage = "";
    try {
      const result = await api.getReconciliation(previous.id);
      if (selectedCompanyId !== companyId || workspaceRequestId !== revision || duplicateImport !== previous) return;
      reconciliationRequestId++;
      reconciliations = [result, ...reconciliations.filter((item) => item.id !== result.id)];
      activeReconciliation = result;
      resetImportAttempt();
    } catch (error) {
      if (selectedCompanyId === companyId && workspaceRequestId === revision && duplicateImport === previous) {
        showError(error);
        if (error instanceof ApiError && error.status === 404) resetImportAttempt();
      }
    } finally {
      openingDuplicate = false;
    }
  }

  async function confirmReimport(): Promise<void> {
    if (!duplicateImport || reconciling || openingDuplicate) return;
    const origin = duplicateImport.id;
    if (importAttempt?.reprocessOf !== origin)
      importAttempt = { requestId: crypto.randomUUID(), reprocessOf: origin };
    await createReconciliation({ preventDefault() {} } as SubmitEvent);
  }
  type UploadKind = "sales" | "statement";
  let draggingFileKind = $state<UploadKind | null>(null);
  const fileDragDepth: Record<UploadKind, number> = { sales: 0, statement: 0 };
  let errorMessage = $state("");
  let successMessage = $state("");

  const currentCompany = $derived(
    companies.find((company) => company.id === selectedCompanyId) ?? null,
  );
  const latestAgreement = $derived(agreements.at(-1) ?? null);
  const summary = $derived.by(() => {
    const lines = activeReconciliation?.lines ?? [];
    return {
      total: lines.length,
      matched: lines.filter((line) => line.status === "Matched").length,
      differences: lines.filter((line) => line.status === "Difference").length,
      pending: lines.filter(
        (line) =>
          line.status === "MissingFromStatement" ||
          line.status === "UnrecognizedSale" ||
          line.status === "MissingAgreement",
      ).length,
    };
  });

  onMount(() => {
    void initialize();
    void refreshApiStatus();
    void refreshUpdates();
    const statusInterval = window.setInterval(() => {
      void refreshApiStatus();
      void refreshUpdates();
    }, 30_000);
    return () => window.clearInterval(statusInterval);
  });

  async function refreshUpdates(): Promise<void> {
    if (fetchingUpdates) return;
    fetchingUpdates = true;
    try {
      updates = await api.getUpdates();
      updatesError = false;
    } catch {
      updatesError = true;
    } finally {
      fetchingUpdates = false;
    }
  }

  async function refreshApiStatus(): Promise<void> {
    try {
      const health = await api.health();
      apiStatus = health.status === "ok" ? "connected" : "disconnected";
    } catch {
      apiStatus = "disconnected";
    }
  }

  async function initialize(): Promise<void> {
    loading = true;
    errorMessage = "";
    try {
      companies = await api.listCompanies();
      if (companies.length > 0) {
        selectedCompanyId = companies[0].id;
        await loadWorkspace(selectedCompanyId);
      }
    } catch (error) {
      showError(error);
    } finally {
      loading = false;
    }
  }

  async function loadWorkspace(companyId: string): Promise<void> {
    const requestId = ++workspaceRequestId;
    const isCurrent = () => selectedCompanyId === companyId && workspaceRequestId === requestId;
    clearFileDrag();
    resetImportAttempt();
    importConfiguration = null;
    importValidated = false;
    importSetupBusy = false;
    importExecutionError = "";
    importConfigurationRevision++;
    // Clear the previous company's data and uploads before the next request.
    agreements = [];
    reconciliations = [];
    activeReconciliation = null;
    salesFile = null;
    statementFile = null;
    resetFileInput("sales-file");
    resetFileInput("statement-file");
    if (!companyId) {
      workspaceLoading = false;
      return;
    }

    workspaceLoading = true;
    errorMessage = "";
    try {
      const [nextAgreements, nextReconciliations] = await Promise.all([
        api.listAgreements(companyId),
        api.listReconciliations(companyId),
      ]);
      if (!isCurrent()) return;
      agreements = nextAgreements;
      reconciliations = nextReconciliations;
      activeReconciliation = nextReconciliations[0] ?? null;
    } catch (error) {
      if (isCurrent()) showError(error);
    } finally {
      if (isCurrent()) workspaceLoading = false;
    }
  }

  async function chooseCompany(event: Event): Promise<void> {
    reconciliationRequestId++;
    const companyId = (event.currentTarget as HTMLSelectElement).value;
    selectedCompanyId = companyId;
    successMessage = "";
    await loadWorkspace(companyId);
  }

  async function handleCompanyDeleted(company: Company): Promise<void> {
    reconciliationRequestId++;
    companyToDelete = null;
    companies = companies.filter((item) => item.id !== company.id);
    agreements = [];
    reconciliations = [];
    activeReconciliation = null;
    salesFile = null;
    statementFile = null;
    resetFileInput("sales-file");
    resetFileInput("statement-file");
    selectedCompanyId = companies[0]?.id ?? "";
    errorMessage = "";
    successMessage = `Representada “${company.name}” excluída.`;
    await loadWorkspace(selectedCompanyId);
    await tick();
    document.getElementById(selectedCompanyId ? "company-select" : "create-company")?.focus();
  }

  async function handleHistoryDeleted(target: HistoryDeletionTarget): Promise<void> {
    historyToDelete = null;
    const companyId = target.kind === "agreement" ? target.agreement.companyId : target.reconciliation.companyId;
    if (selectedCompanyId !== companyId) return;
    errorMessage = "";
    let focusId: string;
    if (target.kind === "agreement") {
      agreements = agreements.filter((agreement) => agreement.id !== target.agreement.id);
      successMessage = "Acordo excluído. As conferências salvas foram preservadas.";
      const remaining = agreements.at(-1);
      focusId = remaining ? `agreement-actions-${remaining.id}` : "effective-date";
    } else {
      reconciliationRequestId++;
      reconciliations = reconciliations.filter((item) => item.id !== target.reconciliation.id);
      if (activeReconciliation?.id === target.reconciliation.id) {
        activeReconciliation = reconciliations[0] ?? null;
      }
      successMessage = "Conferência excluída.";
      focusId = activeReconciliation ? `reconciliation-${activeReconciliation.id}` : "sales-file";
    }
    await tick();
    document.getElementById(focusId)?.focus();
  }

  async function createCompany(event: SubmitEvent): Promise<void> {
    event.preventDefault();
    creatingCompany = true;
    errorMessage = "";
    successMessage = "";
    try {
      const company = await api.createCompany(companyName.trim());
      companies = [...companies, company];
      selectedCompanyId = company.id;
      companyName = "";
      showCompanyForm = false;
      await loadWorkspace(company.id);
      successMessage =
        "Representada cadastrada. Agora, informe o acordo de comissão.";
    } catch (error) {
      showError(error);
    } finally {
      creatingCompany = false;
    }
  }

  async function createAgreement(event: SubmitEvent): Promise<void> {
    event.preventDefault();
    if (!selectedCompanyId || agreementPercentage === null || savingAgreement || workspaceLoading) return;

    const companyId = selectedCompanyId;
    const requestId = workspaceRequestId;
    const isCurrent = () => selectedCompanyId === companyId && workspaceRequestId === requestId;

    savingAgreement = true;
    errorMessage = "";
    successMessage = "";
    try {
      await api.createAgreement(
        companyId,
        agreementDate,
        agreementPercentage,
      );
      if (!isCurrent()) return;
      const nextAgreements = await api.listAgreements(companyId);
      if (!isCurrent()) return;
      agreements = nextAgreements;
      successMessage =
        "Acordo salvo. As próximas conferências usarão a vigência pela data da venda.";
    } catch (error) {
      if (isCurrent()) showError(error);
    } finally {
      savingAgreement = false;
    }
  }

  async function createReconciliation(event: SubmitEvent): Promise<void> {
    event.preventDefault();
    if (!selectedCompanyId || !salesFile || !statementFile || reconciling || workspaceLoading || importSetupBusy || (importConfiguration && !importValidated)) return;

    const companyId = selectedCompanyId;
    const submittedSales = salesFile;
    const submittedStatement = statementFile;
    const requestId = workspaceRequestId;
    const configurationRevision = importConfigurationRevision;
    if (!importAttempt) importAttempt = { requestId: crypto.randomUUID() };
    const attempt = importAttempt;
    const submittedConfiguration = importConfiguration ? JSON.parse(JSON.stringify(importConfiguration)) as ImportConfiguration : null;
    const isCurrent = () => selectedCompanyId === companyId && workspaceRequestId === requestId && importConfigurationRevision === configurationRevision;

    if (salesFile.size + statementFile.size > 9 * 1024 * 1024) {
      errorMessage = "Os dois arquivos devem ter, juntos, no máximo 9 MB.";
      successMessage = "";
      return;
    }

    importExecutionError = "";
    reconciling = true;
    errorMessage = "";
    successMessage = "";
    try {
      const reconciliation = await api.createReconciliation(
        companyId,
        submittedSales,
        submittedStatement,
        submittedConfiguration,
        attempt,
      );
      if (!isCurrent()) return;
      reconciliationRequestId++;
      reconciliations = [reconciliation, ...reconciliations.filter((item) => item.id !== reconciliation.id)];
      if (importAttempt === attempt) resetImportAttempt();
      activeReconciliation = reconciliation;
      successMessage = "Conferência concluída e salva no histórico.";
      // A user may prepare another CSV while this request is in flight.
      if (salesFile === submittedSales) {
        salesFile = null;
        resetFileInput("sales-file");
      }
      if (statementFile === submittedStatement) {
        statementFile = null;
        resetFileInput("statement-file");
      }
    } catch (error) {
      if (isCurrent() && importAttempt === attempt) {
        if (error instanceof ApiError && error.problem?.code === "duplicate_import" && error.problem.existingReconciliation) {
          if (salesFile === submittedSales && statementFile === submittedStatement) {
            duplicateImport = error.problem.existingReconciliation;
            errorMessage = "";
          }
          return;
        }
        // A failed connection may conceal a successful write: retain its request ID.
        if (error instanceof ApiError && ["invalid_reprocess_reference", "request_id_conflict"].includes(error.problem?.code ?? ""))
          resetImportAttempt();
        showError(error);
        importExecutionError = errorMessage;
        if (error instanceof ApiError && error.problem?.issues?.length) {
          importExecutionError = error.problem.issues.map((issue) => {
            const source = issue.sourceKind === "sales" ? "Vendas" : issue.sourceKind === "statement" ? "Demonstrativo" : "Configuração";
            const row = issue.row !== null ? `, linha ${issue.row}` : "";
            const column = issue.column ? `, coluna “${issue.column}”` : "";
            return `${source}${row}${column}: ${issue.message}`;
          }).join("\n");
          errorMessage = "Os arquivos apresentam problemas. Confira o diagnóstico na área de importação.";
        }
      }
    } finally {
      reconciling = false;
    }
  }

  async function openReconciliation(id: string): Promise<void> {
    errorMessage = "";
    const companyId = selectedCompanyId;
    const requestId = ++reconciliationRequestId;
    try {
      const reconciliation = await api.getReconciliation(id);
      if (selectedCompanyId === companyId && requestId === reconciliationRequestId && reconciliations.some((item) => item.id === id)) {
        activeReconciliation = reconciliation;
      }
    } catch (error) {
      if (selectedCompanyId === companyId && requestId === reconciliationRequestId) showError(error);
    }
  }

  function handleImportConfigurationChange(configuration: ImportConfiguration | null): void {
    resetImportAttempt();
    importConfiguration = configuration;
    importConfigurationRevision++;
    importValidated = false;
    importExecutionError = "";
  }

  function resetFileInput(id: string): void {
    const input = document.getElementById(id) as HTMLInputElement | null;
    if (input) input.value = "";
  }

  function showError(error: unknown): void {
    if (error instanceof ApiError) {
      errorMessage = error.message;
    } else {
      errorMessage = "Ocorreu um erro inesperado. Tente novamente.";
    }
    successMessage = "";
  }

  function assignCsvFile(kind: UploadKind, files: File[]): boolean {
    if (!selectedCompanyId || workspaceLoading || files.length === 0) return false;
    successMessage = "";
    if (files.length !== 1) {
      errorMessage = "Selecione apenas um arquivo CSV por área.";
      return false;
    }
    const file = files[0];
    if (!/\.csv$/i.test(file.name)) {
      errorMessage = "Selecione um arquivo com extensão .csv.";
      return false;
    }
    const other = kind === "sales" ? statementFile : salesFile;
    if (file.size + (other?.size ?? 0) > 9 * 1024 * 1024) {
      errorMessage = "Os dois arquivos devem ter, juntos, no máximo 9 MB.";
      return false;
    }
    resetImportAttempt();
    importValidated = false;
    importExecutionError = "";
    if (kind === "sales") salesFile = file;
    else statementFile = file;
    errorMessage = "";
    return true;
  }

  function selectFile(kind: UploadKind, event: Event): void {
    const input = event.currentTarget as HTMLInputElement;
    if (!assignCsvFile(kind, Array.from(input.files ?? []))) input.value = "";
  }

  function clearFileDrag(): void {
    fileDragDepth.sales = fileDragDepth.statement = 0;
    draggingFileKind = null;
  }

  function isFileDrag(event: DragEvent): boolean {
    return Array.from(event.dataTransfer?.types ?? []).includes("Files");
  }

  function enterFileDrag(kind: UploadKind, event: DragEvent): void {
    if (!isFileDrag(event)) return;
    event.preventDefault();
    event.stopPropagation();
    if (!selectedCompanyId || workspaceLoading) return;
    fileDragDepth[kind]++;
    draggingFileKind = kind;
  }

  function leaveFileDrag(kind: UploadKind, event: DragEvent): void {
    event.stopPropagation();
    fileDragDepth[kind] = Math.max(0, fileDragDepth[kind] - 1);
    if (fileDragDepth[kind] === 0 && draggingFileKind === kind) draggingFileKind = null;
  }

  function overFileDrag(event: DragEvent): void {
    if (!isFileDrag(event)) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.dataTransfer) event.dataTransfer.dropEffect = selectedCompanyId && !workspaceLoading ? "copy" : "none";
  }

  function dropFile(kind: UploadKind, event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    clearFileDrag();
    if (assignCsvFile(kind, Array.from(event.dataTransfer?.files ?? []))) {
      // File objects in state are the upload source for both selection methods.
      resetFileInput(kind === "sales" ? "sales-file" : "statement-file");
    }
  }

  function preventOutsideFileDrop(event: DragEvent): void {
    if (!isFileDrag(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = "none";
    clearFileDrag();
  }

</script>

<svelte:window ondragover={preventOutsideFileDrop} ondrop={preventOutsideFileDrop} ondragend={clearFileDrag} />

<svelte:head>
  <title>Fuste — conferência de comissões</title>
  <meta
    name="description"
    content="Confira com clareza as comissões sobre suas vendas."
  />
</svelte:head>

<ToastRegion>
  {#if errorMessage}
    {#key errorMessage}
      <ToastNotification
        timeout={5000}
        pauseOnHover
        kind="error"
        title="Não foi possível concluir"
        subtitle={errorMessage}
        role="alert"
        closeButtonDescription="Fechar aviso de erro"
        on:close={() => (errorMessage = "")}
      />
    {/key}
  {/if}
  {#if successMessage}
    {#key successMessage}
      <ToastNotification
        timeout={5000}
        pauseOnHover
        kind="success"
        title="Operação concluída"
        subtitle={successMessage}
        role="status"
        closeButtonDescription="Fechar aviso de sucesso"
        on:close={() => (successMessage = "")}
      />
    {/key}
  {/if}
  {#if currentCompany && !loading && !workspaceLoading && agreements.length === 0}
    {#key selectedCompanyId}
      <ToastNotification
        timeout={5000}
        pauseOnHover
        kind="warning"
        title="Acordo de comissão pendente"
        subtitle="Cadastre primeiro o acordo de comissão para calcular os valores esperados."
        role="status"
        closeButtonDescription="Fechar aviso de acordo pendente"
      />
    {/key}
  {/if}
</ToastRegion>

<div class="app-shell">
  <header class="topbar">
    <div class="header-identity">
    <div class="brand">
      <span class="brand-mark" aria-hidden="true"
        ><span></span><span></span><span></span></span
      >
      <span class="brand-name"><strong>Fuste</strong></span>
    </div>
    <Tag type="blue" size="default">MVP</Tag>
    </div>
    <OptionsMenu {updates} {updatesError} installedVersion={updates?.installedVersion ?? packageJson.version}
      onOpen={refreshUpdates} />
  </header>

  <main id="inicio" class="page-content">
    <SectionHeader
      class="page-heading"
      level={1}
      size="default"
      eyebrow="FINANCEIRO / COMISSÕES"
      title="Conferência de comissões"
      subtitle="Veja o que foi combinado, o que foi informado e onde estão as diferenças."
    >
      {#snippet actions()}
        <Button
          id="create-company"
          kind="tertiary"
          size="small"
          icon={Add}
          type="button"
          onclick={() => (showCompanyForm = !showCompanyForm)}
        >
          Nova representada
        </Button>
      {/snippet}
    </SectionHeader>

    {#if showCompanyForm}
      <form class="inline-create" onsubmit={createCompany}>
        <div class="grow">
          <TextInput
            id="company-name"
            labelText="Nome da representada"
            bind:value={companyName}
            maxCount={160}
            placeholder="Ex.: Editora Horizonte"
            required
            autofocus
          />
        </div>
        <div class="inline-actions">
          <Button
            kind="ghost"
            size="small"
            type="button"
            onclick={() => (showCompanyForm = false)}>Cancelar</Button
          >
          <Button
            kind="primary"
            size="small"
            type="submit"
              loading={creatingCompany}
              loadingDescription="Salvando representada"
            disabled={creatingCompany}
          >
            {creatingCompany ? "Salvando…" : "Salvar representada"}
          </Button>
        </div>
      </form>
    {/if}

    {#if loading}
      <section class="loading-panel" aria-live="polite">
        <span class="spinner"></span> Carregando seus dados…
      </section>
    {:else if companies.length === 0}
      <section class="empty-start">
        <div class="empty-illustration" aria-hidden="true">
          <Document size={32} />
          <CheckmarkFilled size={20} />
        </div>
        <SectionHeader
          class="empty-heading"
          fill="layer-01"
          eyebrow="PRIMEIRO PASSO"
          title="Vamos organizar sua primeira conferência"
          subtitle="Cadastre a representada com quem você trabalha. Depois, informe o acordo e compare as comissões."
        />
        <Button
          kind="primary"
          size="small"
          type="button"
          onclick={() => (showCompanyForm = true)}
          icon={ArrowRight}
          >Cadastrar representada</Button
        >
      </section>
    {:else if currentCompany}
      <section class="company-toolbar" aria-label="Representada selecionada">
        <div class="company-select-wrap">
          <span class="company-icon" aria-hidden="true"><Building size={20} /></span>
          <div class="company-field">
            <Select
              id="company-select"
              labelText="Representada atual"
              bind:selected={selectedCompanyId}
              size="sm"
              on:change={chooseCompany}
            >
              {#each companies as company (company.id)}
                <SelectItem value={company.id} text={company.name} />
              {/each}
            </Select>
          </div>
        </div>
        <div class="company-toolbar-actions">
          <div class="toolbar-meta">
            <span class="meta-dot"></span>
            {workspaceLoading
              ? "Atualizando…"
              : `${reconciliations.length} ${reconciliations.length === 1 ? "conferência salva" : "conferências salvas"}`}
          </div>
          <Button
            kind="danger-ghost"
            size="small"
            icon={TrashCan}
            disabled={workspaceLoading || creatingCompany || savingAgreement || reconciling}
            onclick={() => (companyToDelete = currentCompany)}
          >Excluir representada</Button>
        </div>
      </section>

      {#if activeReconciliation}
        <section
          class="summary-grid"
          aria-label="Resumo da conferência selecionada"
        >
          <article class="summary-card summary-main">
            <div class="summary-top">
              <span class="summary-icon icon-total"><Compare size={16} /></span><span
                class="summary-label">Itens conferidos</span
              >
            </div>
            <strong>{summary.total}</strong>
            <span class="summary-note">nesta conferência</span>
          </article>
          <article class="summary-card">
            <div class="summary-top">
              <span class="summary-icon icon-match"><CheckmarkFilled size={16} /></span><span
                class="summary-label">Conferidos</span
              >
            </div>
            <strong>{summary.matched}</strong>
            <span class="summary-note">valor esperado igual ao informado</span>
          </article>
          <article class="summary-card">
            <div class="summary-top">
              <span class="summary-icon icon-difference"><WarningAlt size={16} /></span><span
                class="summary-label">Divergências</span
              >
            </div>
            <strong class:metric-alert={summary.differences > 0}
              >{summary.differences}</strong
            >
            <span class="summary-note">valores diferentes</span>
          </article>
          <article class="summary-card">
            <div class="summary-top">
              <span class="summary-icon icon-pending"><Time size={16} /></span><span
                class="summary-label">Pendências</span
              >
            </div>
            <strong>{summary.pending}</strong>
            <span class="summary-note">itens que precisam de atenção</span>
          </article>
        </section>
      {/if}

      <section class="workspace-grid">
        <article class="panel reconcile-panel">
          <SectionHeader
            class="panel-heading"
            fill="layer-01"
            title="Nova conferência"
            subtitle="Compare as vendas com o demonstrativo de comissões."
          >
            {#snippet titleEnd()}<span class="panel-step">2 arquivos CSV</span>{/snippet}
          </SectionHeader>

          <form class="upload-form" onsubmit={createReconciliation}>
            <p id="csv-upload-help" class="helper-text upload-help">Arraste um arquivo CSV para cada área ou clique para selecionar.</p>
            <div class="file-grid">
              <label class="file-drop" class:file-drop-active={draggingFileKind === "sales"} class:file-drop-disabled={workspaceLoading} for="sales-file"
                ondragenter={(event) => enterFileDrag("sales", event)}
                ondragleave={(event) => leaveFileDrag("sales", event)}
                ondragover={overFileDrag}
                ondrop={(event) => dropFile("sales", event)}
              >
                <span class="file-icon" aria-hidden="true"><Document size={20} /></span>
                <span class="file-copy"
                  ><strong>Arquivo de vendas</strong><small
                    >Identificador, data e valor bruto</small
                  ></span
                >
                <span class="file-action"
                  >{draggingFileKind === "sales" ? "Soltar CSV" : salesFile ? "Trocar" : "Selecionar"}</span
                >
                <input
                  id="sales-file"
                  type="file"
                  accept=".csv,text/csv"
                  aria-describedby="csv-upload-help"
                  disabled={workspaceLoading}
                  onchange={(event) => selectFile("sales", event)}
                />
                {#if salesFile}<span class="selected-file"
                    ><CheckmarkFilled size={16} />{salesFile.name}</span
                  >{/if}
              </label>
              <label class="file-drop" class:file-drop-active={draggingFileKind === "statement"} class:file-drop-disabled={workspaceLoading} for="statement-file"
                ondragenter={(event) => enterFileDrag("statement", event)}
                ondragleave={(event) => leaveFileDrag("statement", event)}
                ondragover={overFileDrag}
                ondrop={(event) => dropFile("statement", event)}
              >
                <span class="file-icon file-icon-alt" aria-hidden="true"><DocumentImport size={20} /></span>
                <span class="file-copy"
                  ><strong>Demonstrativo</strong><small
                    >Identificador e comissão informada</small
                  ></span
                >
                <span class="file-action"
                  >{draggingFileKind === "statement" ? "Soltar CSV" : statementFile ? "Trocar" : "Selecionar"}</span
                >
                <input
                  id="statement-file"
                  type="file"
                  accept=".csv,text/csv"
                  aria-describedby="csv-upload-help"
                  disabled={workspaceLoading}
                  onchange={(event) => selectFile("statement", event)}
                />
                {#if statementFile}<span class="selected-file"
                    ><CheckmarkFilled size={16} />{statementFile.name}</span
                  >{/if}
              </label>
            </div>
            <CsvImportConfigurationPanel
              companyId={selectedCompanyId}
              {workspaceRequestId}
              {salesFile}
              {statementFile}
              busy={reconciling || workspaceLoading}
              onConfigurationChange={handleImportConfigurationChange}
              onValidated={(valid) => (importValidated = valid)}
              onBusyChange={(busy) => (importSetupBusy = busy)}
              onNotice={(message) => (successMessage = message)}
            />
            {#if importExecutionError}<p class="csv-import-error" role="alert">{importExecutionError}</p>{/if}
            <div class="upload-footer">
              <div class="helper-text inline-metadata">
                <Information size={16} aria-hidden="true" /><span>Até 9 MB no total</span><Divider orientation="vertical" decorative class="inline-separator" margin="0" /><span>CSV com cabeçalho</span>
              </div>
              <Button
                kind="primary"
                size="small"
                icon={ArrowRight}
                type="submit"
                loading={reconciling}
                loadingDescription="Conferindo arquivos"
                disabled={reconciling ||
                  importSetupBusy ||
                  !salesFile ||
                  !statementFile ||
                  agreements.length === 0 ||
                  (importConfiguration !== null && !importValidated)}
              >
                {#if reconciling}Conferindo…{:else}Conferir arquivos{/if}
              </Button>
            </div>
          </form>
        </article>

        <article class="panel agreement-panel">
          <SectionHeader
            class="panel-heading"
            fill="layer-01"
            title="Acordo de comissão"
            subtitle="Percentual aplicado a cada venda pela data."
          >
            {#snippet titleEnd()}<span class="agreement-symbol" aria-hidden="true">%</span>{/snippet}
          </SectionHeader>

          {#if latestAgreement}
            <div class="current-agreement">
              <div>
                <span class="agreement-caption">Acordo mais recente</span><span
                  class="agreement-rate"
                  >{formatPercentage(latestAgreement.percentage)}</span
                >
              </div>
              <span class="agreement-date"
                >Desde {formatDate(latestAgreement.effectiveFrom)}</span
              >
            </div>
          {:else}
            <div class="agreement-empty">
              <span class="empty-percent">%</span><span
                >Nenhum acordo cadastrado</span
              >
            </div>
          {/if}

          <form class="agreement-form" onsubmit={createAgreement}>
            <div class="field">
              <TextInput
                id="effective-date"
                labelText="Vigente a partir de"
                type="date"
                bind:value={agreementDate}
                required
              />
            </div>
            <div class="field percentage-field">
              <NumberInput
                id="commission-rate"
                labelText="Percentual (%)"
                min={0}
                max={100}
                step={0.01}
                allowDecimal
                bind:value={agreementPercentage}
                required
              />
            </div>
            <Button
              kind="secondary"
              size="small"
              icon={Add}
              fullWidth
              type="submit"
              loading={savingAgreement}
              loadingDescription="Salvando acordo"
              disabled={savingAgreement || !selectedCompanyId}
            >
              {savingAgreement ? "Salvando…" : "Adicionar acordo"}
            </Button>
          </form>

          {#if agreements.length > 0}
            <details class="agreement-history">
              <summary
                >Ver histórico de acordos <span>{agreements.length}</span
                ></summary
              >
              <ul>
                {#each [...agreements].reverse() as agreement (agreement.id)}
                  <li>
                    <span>{formatDate(agreement.effectiveFrom)}</span><strong
                      >{formatPercentage(agreement.percentage)}</strong
                    >
                    <HistoryActions
                      id={`agreement-actions-${agreement.id}`}
                      label={`Ações do acordo de ${formatPercentage(agreement.percentage)} vigente desde ${formatDate(agreement.effectiveFrom)}`}
                      deleteLabel="Excluir acordo"
                      disabled={workspaceLoading || savingAgreement || reconciling}
                      onDelete={() => (historyToDelete = { kind: "agreement", agreement })}
                    />
                  </li>
                {/each}
              </ul>
            </details>
          {/if}
        </article>
      </section>

      {#if reconciliations.length > 0}
        <section class="history-section">
          <SectionHeader
            class="history-heading"
            eyebrow="ACOMPANHAMENTO"
            title="Histórico de conferências"
          >
            {#snippet titleEnd()}
              <span class="history-count">{reconciliations.length} {reconciliations.length === 1 ? "registro" : "registros"}</span>
            {/snippet}
          </SectionHeader>
          <div class="history-list">
            {#each reconciliations as reconciliation, index (reconciliation.id)}
              <div class="history-row" class:history-active={activeReconciliation?.id === reconciliation.id}>
              <Button
                id={`reconciliation-${reconciliation.id}`}
                kind="ghost"
                size="small"
                class="history-item"
                aria-pressed={activeReconciliation?.id === reconciliation.id}
                type="button"
                onclick={() => void openReconciliation(reconciliation.id)}
              >
                <span class="history-index"
                  >{String(reconciliations.length - index).padStart(
                    2,
                    "0",
                  )}</span
                >
                <div class="history-main">
                  <strong>Conferência de comissões</strong>
                  <div class="history-metadata inline-metadata">
                    <span>{formatDateTime(reconciliation.createdAt)}</span>
                    <Divider orientation="vertical" decorative class="inline-separator" margin="0" />
                    <span>{reconciliation.lines.length} itens</span>
                  </div>
                </div>
                <span class="history-statuses">
                  <span class="mini-match"
                    >{reconciliation.lines.filter(
                      (line) => line.status === "Matched",
                    ).length} conferidos</span
                  >
                  {#if reconciliation.lines.some((line) => line.status === "Difference")}
                    <span class="mini-difference"
                      >{reconciliation.lines.filter(
                        (line) => line.status === "Difference",
                      ).length} divergências</span
                    >
                  {/if}
                </span>
              </Button>
              <HistoryActions
                id={`reconciliation-actions-${reconciliation.id}`}
                label={`Ações da conferência de ${formatDateTime(reconciliation.createdAt)}`}
                deleteLabel="Excluir conferência"
                disabled={workspaceLoading || savingAgreement || reconciling}
                onDelete={() => (historyToDelete = { kind: "reconciliation", reconciliation })}
              />
              </div>
            {/each}
          </div>
        </section>
      {/if}

      {#if activeReconciliation}
        {#key activeReconciliation.id}
          <ReconciliationResults reconciliation={activeReconciliation} />
        {/key}
      {:else if !workspaceLoading && agreements.length > 0}
        <section class="results-placeholder">
          <span class="placeholder-mark" aria-hidden="true"><CloudUpload size={20} /></span>
          <div>
            <strong>Sua próxima conferência aparece aqui</strong>
            <p>Selecione os arquivos de vendas e de comissões para começar.</p>
          </div>
        </section>
      {/if}
    {/if}
  </main>

  <DuplicateImportDialog
    previous={duplicateImport}
    busy={reconciling || openingDuplicate}
    error={errorMessage}
    onClose={closeDuplicateImport}
    onOpenPrevious={() => void openPreviousImport()}
    onReprocess={() => void confirmReimport()}
  />
  <DeleteCompanyDialog
    company={companyToDelete}
    onClose={() => (companyToDelete = null)}
    onDeleted={(company) => void handleCompanyDeleted(company)}
  />
  <DeleteHistoryDialog
    target={historyToDelete}
    onClose={() => (historyToDelete = null)}
    onDeleted={(target) => void handleHistoryDeleted(target)}
  />

  <footer class="footer" aria-label="Status e informações do projeto">
    <div class="footer-status-row">
      <div class="footer-status-items">
        <div class="footer-api-status" role="status" aria-live="polite">
          <span
            class="api-status-dot"
            class:api-status-connected={apiStatus === "connected"}
            class:api-status-disconnected={apiStatus === "disconnected"}
            aria-hidden="true"
          ></span>
          {apiStatus === "checking"
            ? "Verificando API…"
            : apiStatus === "connected"
              ? "API conectada"
              : "API indisponível"}
        </div>
        <div class="footer-storage">
          <DataBase size={16} aria-hidden="true" />
          {getApiSettings().useLocal ? "Armazenamento local" : "Armazenamento na API"}
        </div>
      </div>
      <span class="footer-version"><Tag type="purple" size="sm">v{updates?.installedVersion ?? packageJson.version}</Tag></span>
    </div>
    <div class="footer-credits-row">
      <div class="footer-credits">
        <span>© 2026 <a href={packageJson.author.url} target="_blank" rel="noopener noreferrer" aria-label={`Portfólio de ${packageJson.author.name} (abre em nova aba)`}>{packageJson.author.name}</a></span>
        <Divider orientation="vertical" decorative class="inline-separator footer-credits-separator" margin="0" />
        <span class="footer-license">Licenciado sob <a href="/LICENSE.txt" target="_blank" rel="noopener noreferrer" aria-label="Licença Apache-2.0 (abre em nova aba)">{packageJson.license}</a></span>
      </div>
      <a class="footer-github" href={packageJson.source} target="_blank" rel="noopener noreferrer" aria-label="Repositório do Fuste no GitHub (abre em nova aba)">
        GitHub <Launch size={16} aria-hidden="true" />
      </a>
    </div>
  </footer>
</div>
