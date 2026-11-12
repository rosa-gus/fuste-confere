<script lang="ts">
  import ToastRegion from "./ToastRegion.svelte";
  import { ToastNotification, Modal } from "carbon-components-svelte";
  import { api, ApiError } from "../api";
  import type { Company } from "../types";

  let { company, onClose, onDeleted }: {
    company: Company | null;
    onClose: () => void;
    onDeleted: (company: Company) => void;
  } = $props();

  let deleting = $state(false);
  let errorMessage = $state("");

  $effect(() => {
    if (company?.id) errorMessage = "";
  });

  function close(event?: CustomEvent): void {
    if (deleting) {
      event?.preventDefault();
      return;
    }
    onClose();
  }

  async function confirmDeletion(): Promise<void> {
    if (!company || deleting) return;
    const target = company;
    deleting = true;
    errorMessage = "";
    try {
      await api.deleteCompany(target.id);
    } catch (error) {
      errorMessage = error instanceof ApiError
        ? error.message
        : "Não foi possível excluir a representada. Tente novamente.";
      return;
    } finally {
      deleting = false;
    }
    onDeleted(target);
  }
</script>

<Modal
  open={company !== null}
  size="sm"
  danger
  alert
  modalLabel="Cadastro de representadas"
  modalHeading="Excluir representada?"
  iconDescription="Fechar confirmação de exclusão"
  primaryButtonText="Excluir representada"
  primaryButtonDisabled={!company || deleting}
  primaryButtonLoading={deleting}
  primaryButtonLoadingDescription="Excluindo representada…"
  secondaryButtons={[{ text: "Cancelar", disabled: deleting }]}
  shouldSubmitOnEnter={false}
  preventCloseOnClickOutside
  hideCloseButton={deleting}
  on:close={close}
  on:click:button--secondary={() => close()}
  on:submit={() => void confirmDeletion()}
>
  {#if company}
    <p class="delete-company-name"><strong>{company.name}</strong></p>
    <p>A representada, seus acordos e seu histórico de conferências serão excluídos permanentemente.</p>
    <p class="delete-company-warning">Esta ação não pode ser desfeita.</p>
  {/if}
  {#if errorMessage && company}
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
