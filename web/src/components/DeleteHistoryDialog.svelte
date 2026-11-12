<script lang="ts">
  import ToastRegion from "./ToastRegion.svelte";
  import { Divider, ToastNotification, Modal } from "carbon-components-svelte";
  import { api, ApiError } from "../api";
  import { formatDate, formatDateTime, formatPercentage } from "../format";
  import type { HistoryDeletionTarget } from "../types";

  let { target, onClose, onDeleted }: {
    target: HistoryDeletionTarget | null;
    onClose: () => void;
    onDeleted: (target: HistoryDeletionTarget) => void;
  } = $props();
  let deleting = $state(false);
  let errorMessage = $state("");
  const agreement = $derived(target?.kind === "agreement");

  $effect(() => {
    if (target) errorMessage = "";
  });

  function close(event?: CustomEvent): void {
    if (deleting) {
      event?.preventDefault();
      return;
    }
    onClose();
  }

  async function confirmDeletion(): Promise<void> {
    if (!target || deleting) return;
    const item = target;
    deleting = true;
    errorMessage = "";
    try {
      if (item.kind === "agreement") {
        await api.deleteAgreement(item.agreement.companyId, item.agreement.id);
      } else {
        await api.deleteReconciliation(item.reconciliation.companyId, item.reconciliation.id);
      }
    } catch (error) {
      errorMessage = error instanceof ApiError
        ? error.message
        : "Não foi possível excluir o registro. Tente novamente.";
      return;
    } finally {
      deleting = false;
    }
    onDeleted(item);
  }
</script>

<Modal
  open={target !== null}
  size="sm"
  danger
  alert
  modalLabel={agreement ? "Acordos de comissão" : "Histórico de conferências"}
  modalHeading={agreement ? "Excluir acordo?" : "Excluir conferência?"}
  iconDescription="Fechar confirmação de exclusão"
  primaryButtonText={agreement ? "Excluir acordo" : "Excluir conferência"}
  primaryButtonDisabled={!target || deleting}
  primaryButtonLoading={deleting}
  primaryButtonLoadingDescription="Excluindo…"
  secondaryButtons={[{ text: "Cancelar", disabled: deleting }]}
  shouldSubmitOnEnter={false}
  preventCloseOnClickOutside
  hideCloseButton={deleting}
  on:close={close}
  on:click:button--secondary={() => close()}
  on:submit={() => void confirmDeletion()}
>
  {#if target?.kind === "agreement"}
    <div class="deletion-identity inline-metadata"><strong>{formatPercentage(target.agreement.percentage)}</strong><Divider orientation="vertical" decorative class="inline-separator" margin="0" /><strong>Vigente desde {formatDate(target.agreement.effectiveFrom)}</strong></div>
    <p>As próximas conferências usarão os acordos restantes. Vendas sem um acordo aplicável serão sinalizadas como sem acordo vigente.</p>
    <p class="deletion-warning">As conferências já salvas manterão os percentuais e valores calculados.</p>
  {:else if target?.kind === "reconciliation"}
    <div class="deletion-identity inline-metadata"><strong>{formatDateTime(target.reconciliation.createdAt)}</strong><Divider orientation="vertical" decorative class="inline-separator" margin="0" /><strong>{target.reconciliation.lines.length} itens</strong></div>
    <p>Esta conferência e seus itens serão excluídos permanentemente.</p>
  {/if}
  <p class="deletion-warning">Esta ação não pode ser desfeita.</p>
  {#if errorMessage && target}
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
</Modal>
