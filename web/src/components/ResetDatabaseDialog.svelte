<script lang="ts">
  import ToastRegion from "./ToastRegion.svelte";
  import { ToastNotification, Modal, TextInput } from "carbon-components-svelte";
  import { api, ApiError } from "../api";
  import { getApiBaseUrl } from "../api-settings";
  import { explainApiError } from "../format";

  let { apiUrl, onClose }: {
    apiUrl: string;
    onClose: () => void;
  } = $props();

  let confirmation = $state("");
  let resetting = $state(false);
  let errorMessage = $state("");

  function close(event?: CustomEvent): void {
    if (resetting) {
      event?.preventDefault();
      return;
    }
    onClose();
  }

  async function reset(): Promise<void> {
    if (resetting || confirmation !== "EXCLUIR") return;
    if (getApiBaseUrl() !== apiUrl) {
      errorMessage = "A conexão com a API mudou. Feche esta confirmação e confira a API em uso antes de tentar novamente.";
      return;
    }
    resetting = true;
    errorMessage = "";
    try {
      await api.resetDatabase();
    } catch (error) {
      errorMessage = error instanceof ApiError
        ? explainApiError(error.message)
        : "Não foi possível redefinir o banco de dados. Tente novamente.";
      resetting = false;
      return;
    }
    // Reload discards all in-memory workspace data; browser preferences survive.
    window.location.reload();
  }
</script>

<Modal
  open
  size="sm"
  danger
  hasForm
  modalHeading="Redefinir banco de dados?"
  iconDescription="Fechar confirmação de redefinição"
  primaryButtonText="Redefinir banco de dados"
  primaryButtonDisabled={confirmation !== "EXCLUIR" || resetting}
  primaryButtonLoading={resetting}
  primaryButtonLoadingDescription="Redefinindo banco de dados…"
  secondaryButtons={[{ text: "Cancelar", disabled: resetting }]}
  shouldSubmitOnEnter={false}
  preventCloseOnClickOutside
  hideCloseButton={resetting}
  selectorPrimaryFocus="#reset-database-confirmation"
  on:close={close}
  on:click:button--secondary={() => close()}
  on:submit={() => void reset()}
>
  <div class="database-reset-content">
    <p>Todas as representadas, acordos e conferências da API abaixo serão excluídos permanentemente.</p>
    <div class="database-api-target">
      <span>API afetada</span>
      <strong>{apiUrl}</strong>
    </div>
    <p>Esta ação não pode ser desfeita. O tema e a conexão salva neste navegador serão mantidos.</p>
    <TextInput
      id="reset-database-confirmation"
      labelText="Digite EXCLUIR para confirmar"
      autocomplete="off"
      spellcheck={false}
      bind:value={confirmation}
      disabled={resetting}
    />
    {#if errorMessage}
      <ToastRegion>
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
      </ToastRegion>
    {/if}
  </div>
</Modal>
