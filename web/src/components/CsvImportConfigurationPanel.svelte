<script lang="ts">
  import { onDestroy, untrack } from "svelte";
  import { Button, InlineLoading, Modal, Select, SelectItem, Tab, TabContent, Tabs, TextInput } from "carbon-components-svelte";
  import { api, ApiError } from "../api";
  import { explainApiError } from "../format";
  import { getImportDateFormatHelp, getImportIssueMessage, importConfigurationKey, isImportPreviewCurrent, type ImportPreviewIdentity } from "../csv-import";
  import type { ImportConfiguration, ImportPreview, ImportPreviewSource, ImportProfile, ImportSourceKind, SourceImportConfiguration } from "../types";
  import SectionHeader from "./SectionHeader.svelte";
  import CsvMappingEditor from "./CsvMappingEditor.svelte";
  import CsvImportPreview from "./CsvImportPreview.svelte";

  let { companyId, workspaceRequestId, salesFile, statementFile, busy = false, onConfigurationChange, onValidated, onBusyChange, onNotice }: {
    companyId: string;
    workspaceRequestId: number;
    salesFile: File | null;
    statementFile: File | null;
    busy?: boolean;
    onConfigurationChange: (configuration: ImportConfiguration | null) => void;
    onValidated: (valid: boolean) => void;
    onBusyChange: (busy: boolean) => void;
    onNotice: (message: string) => void;
  } = $props();

  let profiles = $state<ImportProfile[]>([]);
  let selectedProfileId = $state("");
  let profileName = $state("");
  let draft = $state<ImportConfiguration | null>(null);
  let expanded = $state(false);
  let preview = $state<ImportPreview | null>(null);
  let sources = $state<ImportPreviewSource[]>([]);
  let loadingProfiles = $state(false);
  let previewing = $state(false);
  let saving = $state(false);
  let profileError = $state("");
  let previewError = $state("");
  let revisionConflict = $state(false);
  let profileToDelete = $state<ImportProfile | null>(null);
  let activeTab = $state(0);
  let previewRequestId = 0;
  let profileRequestId = 0;
  let destroyed = false;
  const selectedProfile = $derived(profiles.find((profile) => profile.id === selectedProfileId) ?? null);
  const blocked = $derived(busy || saving || previewing);

  onDestroy(() => {
    destroyed = true;
    previewRequestId++;
    profileRequestId++;
    onBusyChange(false);
  });

  $effect(() => {
    onBusyChange(previewing || saving);
  });

  $effect(() => {
    const id = companyId;
    const workspace = workspaceRequestId;
    untrack(() => {
      previewRequestId++;
      profiles = [];
      selectedProfileId = "";
      profileName = "";
      draft = null;
      expanded = false;
      preview = null;
      sources = [];
      previewing = false;
      saving = false;
      previewError = "";
      profileError = "";
      revisionConflict = false;
      profileToDelete = null;
      onConfigurationChange(null);
      onValidated(false);
      void loadProfiles(id, workspace);
    });
  });

  $effect(() => {
    salesFile;
    statementFile;
    untrack(() => {
      invalidatePreview();
      sources = [];
    });
  });

  function invalidatePreview(): void {
    previewRequestId++;
    preview = null;
    previewError = "";
    previewing = false;
    onValidated(false);
  }

  function identity(): ImportPreviewIdentity {
    return { companyId, workspaceRequestId, salesFile, statementFile, configurationKey: importConfigurationKey(draft), requestId: previewRequestId };
  }

  async function loadProfiles(id = companyId, workspace = workspaceRequestId): Promise<void> {
    const requestId = ++profileRequestId;
    loadingProfiles = true;
    try {
      const result = await api.listImportProfiles(id);
      if (companyId !== id || workspaceRequestId !== workspace || requestId !== profileRequestId) return;
      profiles = result;
    } catch (error) {
      if (companyId === id && workspaceRequestId === workspace && requestId === profileRequestId) {
        profileError = "Não foi possível carregar os perfis de importação. O formato padrão continua disponível.";
      }
    } finally {
      if (companyId === id && workspaceRequestId === workspace && requestId === profileRequestId) loadingProfiles = false;
    }
  }

  function chooseProfile(id: string): void {
    invalidatePreview();
    const profile = profiles.find((item) => item.id === id);
    selectedProfileId = profile?.id ?? "";
    profileName = profile?.name ?? "";
    draft = profile ? JSON.parse(JSON.stringify(profile.configuration)) : null;
    expanded = !!draft;
    sources = [];
    profileError = "";
    revisionConflict = false;
    onConfigurationChange(draft);
  }

  function changeSource(kind: ImportSourceKind, configuration: SourceImportConfiguration): void {
    if (!draft) return;
    invalidatePreview();
    draft = { ...draft, [kind]: configuration };
    onConfigurationChange(draft);
  }

  async function inspectOrValidate(): Promise<void> {
    if (!salesFile || !statementFile || blocked) return;
    invalidatePreview();
    expanded = true;
    previewing = true;
    const snapshot = identity();
    try {
      const result = await api.previewImport(companyId, salesFile, statementFile, draft);
      if (!isImportPreviewCurrent(snapshot, identity())) return;
      draft = result.configuration;
      sources = result.sources;
      preview = result;
      onConfigurationChange(draft);
      onValidated(result.isValid && result.validationComplete);
    } catch (error) {
      if (!isImportPreviewCurrent(snapshot, identity())) return;
      previewError = error instanceof ApiError ? explainApiError(error.message) : "Não foi possível analisar os arquivos. Tente novamente.";
    } finally {
      if (snapshot.requestId === previewRequestId) previewing = false;
    }
  }

  async function saveProfile(update: boolean): Promise<void> {
    if (!draft || !profileName.trim() || !preview?.isValid || !preview.validationComplete || blocked || (update && (!selectedProfile || revisionConflict))) return;
    const id = companyId;
    const workspace = workspaceRequestId;
    const configurationKey = importConfigurationKey(draft);
    const currentProfile = selectedProfile;
    saving = true;
    profileError = "";
    try {
      const profile = update && currentProfile
        ? await api.updateImportProfile(id, currentProfile.id, profileName.trim(), draft, currentProfile.revision)
        : await api.createImportProfile(id, profileName.trim(), draft);
      if (destroyed || companyId !== id || workspaceRequestId !== workspace || configurationKey !== importConfigurationKey(draft)) return;
      profiles = [...profiles.filter((item) => item.id !== profile.id), profile];
      selectedProfileId = profile.id;
      profileName = profile.name;
      revisionConflict = false;
      onNotice(update ? "Perfil de importação atualizado." : "Perfil de importação salvo.");
    } catch (error) {
      if (destroyed || companyId !== id || workspaceRequestId !== workspace) return;
      if (error instanceof ApiError && error.status === 409) {
        revisionConflict = update;
        profileError = update
          ? "Este perfil foi alterado em outra sessão. Selecione-o novamente para carregar a versão atual ou salve suas alterações com outro nome."
          : "Já existe um perfil com esse nome. Escolha outro nome.";
        await loadProfiles(id, workspace);
      } else {
        profileError = "Não foi possível salvar o perfil. Tente novamente.";
      }
    } finally {
      if (!destroyed && companyId === id && workspaceRequestId === workspace) saving = false;
    }
  }

  async function deleteProfile(): Promise<void> {
    if (!profileToDelete || saving) return;
    const target = profileToDelete;
    const id = companyId;
    const workspace = workspaceRequestId;
    saving = true;
    profileError = "";
    try {
      await api.deleteImportProfile(id, target.id);
      if (destroyed || companyId !== id || workspaceRequestId !== workspace) return;
      profiles = profiles.filter((profile) => profile.id !== target.id);
      if (selectedProfileId === target.id) {
        selectedProfileId = "";
        profileName = "";
      }
      profileToDelete = null;
      onNotice("Perfil excluído. A configuração em uso foi preservada para esta conferência.");
    } catch {
      if (!destroyed && companyId === id && workspaceRequestId === workspace) {
        profileToDelete = null;
        profileError = "Não foi possível excluir o perfil. Tente novamente.";
      }
    } finally {
      if (!destroyed && companyId === id && workspaceRequestId === workspace) saving = false;
    }
  }

  function closeDeleteProfile(event?: CustomEvent): void {
    if (saving) {
      event?.preventDefault();
      return;
    }
    profileToDelete = null;
  }
</script>

<div class="csv-import-settings">
  <div class="csv-profile-toolbar">
    <Select id="csv-import-profile" labelText="Formato dos arquivos" selected={selectedProfileId} disabled={blocked || loadingProfiles} onchange={(event) => chooseProfile(event.currentTarget.value)}>
      <SelectItem value="" text={draft && !selectedProfileId ? "Configuração personalizada" : "Formato padrão atual"} />
      {#each profiles as profile (profile.id)}<SelectItem value={profile.id} text={profile.name} />{/each}
    </Select>
    <Button kind="tertiary" size="small" type="button" disabled={!salesFile || !statementFile || blocked} onclick={() => void inspectOrValidate()}>
      {previewing ? "Analisando…" : draft ? "Validar arquivos" : "Configurar colunas"}
    </Button>
    {#if draft}<Button kind="ghost" size="small" type="button" disabled={blocked} onclick={() => chooseProfile("")}>Usar padrão atual</Button>{/if}
  </div>
  {#if loadingProfiles}<InlineLoading description="Carregando perfis…" />{/if}
  {#if profileError}
    <p class="csv-import-error" role="alert">{profileError}</p>
    <Button kind="ghost" size="small" type="button" disabled={loadingProfiles || blocked} onclick={() => void loadProfiles()}>Recarregar perfis</Button>
    {#if revisionConflict && selectedProfile}<Button kind="ghost" size="small" type="button" disabled={blocked || loadingProfiles} onclick={() => chooseProfile(selectedProfileId)}>Carregar versão atual</Button>{/if}
  {/if}
  {#if !draft}<p class="csv-import-help">O padrão atual aceita os cabeçalhos conhecidos em português ou inglês. Para usar outra exportação, selecione os dois CSVs e configure as colunas.</p>{/if}
  {#if previewing}<InlineLoading description="Analisando os arquivos completos…" />{/if}
  {#if previewError}<p class="csv-import-error" role="alert">{previewError}</p>{/if}

  {#if expanded && draft}
    <SectionHeader class="csv-import-heading" title="Configuração da importação" level={3} fill="layer-01" subtitle="Associe as colunas e confirme como interpretar os valores." />
    <Tabs bind:selected={activeTab} autoWidth aria-label="Configuração dos arquivos CSV">
      <Tab label="Vendas" />
      <Tab label="Demonstrativo" />
      <svelte:fragment slot="content">
        {#each ["sales", "statement"] as sourceKind (sourceKind)}
          {@const kind = sourceKind as ImportSourceKind}
          {@const source = sources.find((item) => item.sourceKind === kind)}
          <TabContent flush>
            <div class="csv-source-settings">
              <CsvMappingEditor {kind} configuration={draft[kind]} headers={source?.headers ?? []} disabled={blocked} onChange={(configuration) => changeSource(kind, configuration)} />
              {#if source}<CsvImportPreview source={preview ? source : { ...source, normalizedRows: [] }} />{/if}
            </div>
          </TabContent>
        {/each}
      </svelte:fragment>
    </Tabs>
    {#if !preview}<p class="csv-import-help" role="status">Valide novamente após alterar os arquivos, as colunas ou os formatos. Alterar o separador também atualiza os cabeçalhos na próxima validação.</p>{/if}
    {#if preview}
      <div class="csv-validation-summary" role="status">
        {#if preview.isValid && preview.validationComplete}Arquivos validados. Você já pode conferir ou salvar este formato.
        {:else if !preview.validationComplete}Confira o mapeamento sugerido e clique em “Validar arquivos”.
        {:else}Foram encontrados {preview.totalIssues} {preview.totalIssues === 1 ? "problema" : "problemas"}. Corrija os dados ou a configuração e valide novamente.{/if}
      </div>
      {#if preview.issues.length > 0}
        <!-- svelte-ignore a11y_no_noninteractive_tabindex (Allow keyboard scrolling of the validation report.) -->
        <div class="csv-issues" role="region" aria-label="Problemas de importação" tabindex="0">
          <ul>
            {#each preview.issues as issue, index (index)}
              <li><strong>{issue.sourceKind === "sales" ? "Vendas" : issue.sourceKind === "statement" ? "Demonstrativo" : "Configuração"}{#if issue.row !== null}, linha {issue.row}{/if}{#if issue.column}, coluna “{issue.column}”{/if}:</strong> {getImportIssueMessage(issue)}{#if issue.code === "invalid_date"} {getImportDateFormatHelp(draft.sales.dateFormat)}{/if}</li>
            {/each}
          </ul>
          {#if preview.issuesTruncated}<p>A lista mostra apenas parte dos {preview.totalIssues} problemas encontrados.</p>{/if}
        </div>
      {/if}
    {/if}
    <div class="csv-profile-save">
      <TextInput id="csv-profile-name" labelText="Nome do perfil desta representada" placeholder="Ex.: Exportação do ERP" maxCount={160} bind:value={profileName} disabled={blocked} onkeydown={(event) => { if (event.key === "Enter") event.preventDefault(); }} />
      <div class="csv-profile-actions">
        <Button kind="tertiary" size="small" type="button" disabled={blocked || !profileName.trim() || !preview?.isValid || !preview.validationComplete} onclick={() => void saveProfile(false)}>Salvar como novo perfil</Button>
        {#if selectedProfile}
          <Button kind="ghost" size="small" type="button" disabled={blocked || revisionConflict || !profileName.trim() || !preview?.isValid || !preview.validationComplete} onclick={() => void saveProfile(true)}>Atualizar perfil</Button>
          <Button kind="danger-ghost" size="small" type="button" disabled={blocked} onclick={() => (profileToDelete = selectedProfile)}>Excluir perfil</Button>
        {/if}
      </div>
      <p class="csv-import-help">O perfil guarda colunas e formatos dos dois arquivos. Os arquivos não são armazenados no perfil.</p>
    </div>
  {/if}
</div>

{#if profileToDelete}
  <Modal open danger alert size="sm" modalHeading="Excluir perfil de importação?" primaryButtonText="Excluir perfil" secondaryButtons={[{ text: "Cancelar", disabled: saving }]} primaryButtonDisabled={saving} primaryButtonLoading={saving} primaryButtonLoadingDescription="Excluindo perfil…" hideCloseButton={saving} preventCloseOnClickOutside on:close={closeDeleteProfile} on:click:button--secondary={() => closeDeleteProfile()} on:submit={() => void deleteProfile()}>
    <p>O perfil “{profileToDelete.name}” será excluído. As conferências salvas e os arquivos não serão alterados.</p>
  </Modal>
{/if}
