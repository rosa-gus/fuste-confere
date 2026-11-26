<script lang="ts">
  import SectionHeader from "./SectionHeader.svelte";
  import { tick } from "svelte";
  import { Button, Checkbox, Divider, PageHeader, Section, Tab, TabContent, Tabs, TextInput } from "carbon-components-svelte";
  import { getApiBaseUrl, getApiSettings, normalizeApiUrl, saveApiSettings } from "../api-settings";
  import ResetDatabaseDialog from "./ResetDatabaseDialog.svelte";
  import SettingsAdjust from "carbon-icons-svelte/lib/SettingsAdjust.svelte";
  import CheckmarkFilled from "carbon-icons-svelte/lib/CheckmarkFilled.svelte";
  import { applyTheme, currentTheme, themes, type ThemeId } from "../themes";
  import { formatDateTime } from "../format";
  import type { UpdateStatus } from "../types";
  import packageJson from "../../package.json";

  let { updates, updatesError, installedVersion, onOpen }: {
    updates: UpdateStatus | null;
    updatesError: boolean;
    installedVersion: string;
    onOpen: () => Promise<void>;
  } = $props();
  const updateAvailable = $derived(!updatesError && updates?.status === "updateAvailable");
  const releaseBase = `${packageJson.source}/releases/`;
  const releaseUrl = $derived(!updatesError && updates?.releaseUrl?.startsWith(releaseBase + "tag/") ? updates.releaseUrl : null);
  const downloadUrl = $derived(updateAvailable && updates?.downloadUrl?.startsWith(releaseBase + "download/") ? updates.downloadUrl : null);

  let open = $state(false);
  let selected = $state<ThemeId>(currentTheme());
  let container: HTMLDivElement;
  let trigger: HTMLButtonElement;
  const connection = getApiSettings();
  let useLocal = $state(connection.useLocal);
  let apiUrl = $state(connection.url);
  let connectionError = $state("");
  let activeTab = $state(0);
  let resetApiUrl = $state<string | null>(null);

  async function closeResetDialog(): Promise<void> {
    resetApiUrl = null;
    await tick();
    container.querySelector<HTMLButtonElement>("#reset-database-trigger")?.focus();
  }

  function saveConnection(event: SubmitEvent): void {
    event.preventDefault();
    connectionError = "";
    if (!useLocal) {
      try {
        normalizeApiUrl(apiUrl);
      } catch {
        connectionError = "Informe uma URL HTTP ou HTTPS válida, sem credenciais, parâmetros ou fragmentos.";
        return;
      }
    }
    try {
      saveApiSettings({ useLocal, url: apiUrl });
    } catch {
      connectionError = "Não foi possível salvar a conexão neste navegador.";
      return;
    }
    window.location.reload();
  }

  async function toggle(): Promise<void> {
    open = !open;
    if (open) {
      if (updateAvailable) activeTab = 3;
      void onOpen();
      await tick();
      container.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus();
    }
  }

  function selectTheme(theme: ThemeId): void {
    selected = theme;
    applyTheme(theme);
  }

  function outsidePointer(event: PointerEvent): void {
    if (open && !resetApiUrl && event.target instanceof Node && !container.contains(event.target)) open = false;
  }

  function handleKeydown(event: KeyboardEvent): void {
    if (open && !resetApiUrl && event.key === "Escape") {
      event.preventDefault();
      open = false;
      trigger.focus();
    }
  }

  function handleFocusOut(event: FocusEvent): void {
    if (!resetApiUrl && event.relatedTarget instanceof Node && !container.contains(event.relatedTarget)) open = false;
  }
</script>

<svelte:window onpointerdown={outsidePointer} onkeydown={handleKeydown} />

<div class="options-menu" bind:this={container} onfocusout={handleFocusOut}>
  <button
    bind:this={trigger}
    type="button"
    class="options-trigger bx--btn bx--btn--ghost bx--btn--sm"
    aria-expanded={open}
    aria-controls="application-options"
    onclick={toggle}
  >
    {updateAvailable ? "Atualização disponível" : "Opções"} <SettingsAdjust size={16} />
  </button>

  {#if open}
    <section id="application-options" class="options-panel" aria-labelledby="options-title">
      <Section level={2} tag="div">
        <PageHeader title="Opções" size="sm" fill="layer-01" hideDivider class="section-header options-heading">
          <span slot="titleChildren" id="options-title">Opções</span>
          <svelte:fragment slot="tabs">
            <Tabs bind:selected={activeTab} autoWidth aria-label="Categorias de opções">
              <Tab label="Aparência" />
              <Tab label="Conexão" />
              <Tab label="Banco de dados" />
              <Tab label="Atualizações" />
            </Tabs>
          </svelte:fragment>
          <TabContent flush>
            <div class="options-tab-body">
              <fieldset class="theme-picker">
                <legend>Tema</legend>
                <div class="theme-grid">
                  {#each themes as theme (theme.id)}
                    <label class="theme-card" class:theme-card-selected={selected === theme.id}>
                      <input
                        type="radio"
                        name="appearance-theme"
                        value={theme.id}
                        checked={selected === theme.id}
                        onchange={() => selectTheme(theme.id)}
                      />
                      <span
                        class="theme-preview"
                        style={`--preview-bg:${theme.background};--preview-layer:${theme.layer};--preview-text:${theme.text};--preview-border:${theme.border};--preview-accent:${theme.accent}`}
                        aria-hidden="true"
                      >
                        <span class="preview-header"><i></i><b></b></span>
                        <span class="preview-body">
                          <span class="preview-sidebar"><i></i><i></i><i></i></span>
                          <span class="preview-content">
                            <b></b>
                            <span class="preview-table"><i></i><i></i><i></i></span>
                            <span class="preview-button"></span>
                          </span>
                        </span>
                      </span>
                      <span class="theme-card-label">
                        {theme.name}
                        {#if selected === theme.id}<CheckmarkFilled size={16} />{/if}
                      </span>
                    </label>
                  {/each}
                </div>
              </fieldset>
            </div>
          </TabContent>
          <TabContent flush>
            <div class="options-tab-body">
              <form class="connection-options" onsubmit={saveConnection}>
                <TextInput
                  id="api-url"
                  labelText="URL da API"
                  type="url"
                  placeholder="https://api.exemplo.com"
                  bind:value={apiUrl}
                  disabled={useLocal}
                  required={!useLocal}
                  invalid={!!connectionError}
                  invalidText={connectionError}
                />
                <Checkbox id="use-local-api" labelText="Usar API local" bind:checked={useLocal} />
                <p class="connection-help">A API local utiliza o mesmo endereço desta interface. Salvar a conexão recarrega os dados da aplicação.</p>
                {#if connectionError && useLocal}<p class="connection-error" role="alert">{connectionError}</p>{/if}
                <Button type="submit" size="small">Salvar conexão</Button>
              </form>
            </div>
          </TabContent>
          <TabContent flush>
            <div class="options-tab-body database-options">
              <div>
                <SectionHeader class="options-section-heading" title="Armazenamento" level={3} fill="layer-01" />
                <div class="options-description inline-metadata"><span>SQLite</span><Divider orientation="vertical" decorative class="inline-separator" margin="0" /><span>No servidor da API</span></div>
              </div>
              <div class="database-api-target">
                <span>API em uso</span>
                <strong>{getApiBaseUrl()}</strong>
              </div>
              <div class="database-reset-section">
                <SectionHeader class="options-section-heading" title="Redefinir banco de dados" level={3} fill="layer-01" />
                <p class="options-description">Exclui todas as representadas, acordos e conferências. As preferências deste navegador são mantidas.</p>
                <Button id="reset-database-trigger" kind="danger" size="small" onclick={() => resetApiUrl = getApiBaseUrl()}>
                  Redefinir banco de dados
                </Button>
              </div>
            </div>
          </TabContent>
          <TabContent flush>
            <div class="options-tab-body database-options">
              <div role="status">
                <p>Versão instalada: <strong>v{installedVersion}</strong></p>
                {#if updatesError || updates?.status === "unavailable"}
                  <p class="options-description">Não foi possível verificar atualizações.</p>
                {:else if !updates || updates.status === "checking"}
                  <p class="options-description">Verificando atualizações…</p>
                {:else if updates.status === "disabled"}
                  <p class="options-description">Verificação de atualizações desativada nesta instalação.</p>
                {:else if updates.status === "upToDate"}
                  <p class="options-description">O Fuste está em dia.</p>
                {:else if updates.status === "incompatible"}
                  <p class="options-description">A versão v{updates.availableVersion} ainda não tem pacote para {updates.rid}.</p>
                {:else}
                  <p class="options-description">A versão v{updates.availableVersion} está disponível para {updates.rid}.</p>
                {/if}
                {#if updates?.lastCheckedAt}
                  <p class="options-description">Última verificação em {formatDateTime(updates.lastCheckedAt)}.</p>
                {/if}
              </div>
              {#if releaseUrl}
                <div class="inline-metadata">
                  {#if downloadUrl}
                    <Button size="small" href={downloadUrl} target="_blank" rel="noopener noreferrer">Baixar atualização</Button>
                  {/if}
                  <Button kind="ghost" size="small" href={releaseUrl} target="_blank" rel="noopener noreferrer">
                    Ver notas da versão
                  </Button>
                </div>
              {/if}
            </div>
          </TabContent>
        </PageHeader>
      </Section>
    </section>
  {/if}
</div>

{#if resetApiUrl}
  <ResetDatabaseDialog apiUrl={resetApiUrl} onClose={() => void closeResetDialog()} />
{/if}
