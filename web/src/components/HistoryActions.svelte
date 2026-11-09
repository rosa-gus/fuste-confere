<script lang="ts">
  import { tick } from "svelte";
  import { OverflowMenu, OverflowMenuItem } from "carbon-components-svelte";
  import TrashCan from "carbon-icons-svelte/lib/TrashCan.svelte";

  let { id, label, deleteLabel, disabled = false, onDelete }: {
    id: string;
    label: string;
    deleteLabel: string;
    disabled?: boolean;
    onDelete: () => void;
  } = $props();
  let trigger = $state<HTMLButtonElement | null>(null);

  async function handleClose(event: CustomEvent<{ trigger: string }>): Promise<void> {
    if (event.detail.trigger !== "item-select") return;
    // Close the portaled menu and restore its trigger before the modal takes focus.
    await tick();
    trigger?.focus();
    onDelete();
  }
</script>

<div class="history-actions">
  <OverflowMenu
    {id}
    {disabled}
    size="sm"
    flipped
    portalMenu
    iconDescription={label}
    bind:buttonRef={trigger}
    on:close={handleClose}
  >
    <OverflowMenuItem text={deleteLabel} icon={TrashCan} danger />
  </OverflowMenu>
</div>
