<script lang="ts">
  import { Button, InlineNotification, Modal } from "carbon-components-svelte";
  import { formatDateTime } from "../format";

  let { previous, busy, error, onClose, onOpenPrevious, onReprocess }: {
    previous: { id: string; createdAt: string } | null;
    busy: boolean;
    error: string;
    onClose: () => void;
    onOpenPrevious: () => void;
    onReprocess: () => void;
  } = $props();

  function close(event?: CustomEvent): void {
    if (busy) { event?.preventDefault(); return; }
    onClose();
  }
</script>

<Modal
  open={previous !== null}
  size="sm"
  modalLabel="Arquivos já conferidos"
  modalHeading="Conferir novamente?"
  iconDescription="Fechar aviso de reimportação"
  primaryButtonText="Conferir novamente"
  primaryButtonDisabled={busy}
  primaryButtonLoading={busy}
  primaryButtonLoadingDescription="Aguarde…"
  secondaryButtons={[{ text: "Cancelar", disabled: busy }]}
  shouldSubmitOnEnter={false}
  preventCloseOnClickOutside
  hideCloseButton={busy}
  on:close={close}
  on:click:button--secondary={() => close()}
  on:submit={onReprocess}
>
  <div class="reimport-details">
    {#if previous}<p>Conferência anterior: <strong>{formatDateTime(previous.createdAt)}</strong>.</p>{/if}
    <p>Uma nova conferência usará os acordos atuais. O resultado anterior será preservado.</p>
    <div>
      <Button kind="ghost" size="small" disabled={busy} onclick={onOpenPrevious}>Abrir conferência anterior</Button>
    </div>
    {#if error}
      <InlineNotification kind="error" title="Não foi possível concluir" subtitle={error} hideCloseButton />
    {/if}
  </div>
</Modal>

<style>
  .reimport-details {
    display: grid;
    gap: var(--space-05);
  }

  .reimport-details p {
    margin: 0;
  }
</style>
