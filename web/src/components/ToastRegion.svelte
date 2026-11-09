<script lang="ts">
  import type { Snippet } from "svelte";

  let { children }: { children: Snippet } = $props();

  function showToasts(element: HTMLDivElement) {
    // The top layer avoids clipping by modal transforms while keeping toast
    // buttons inside the modal's DOM and keyboard focus trap.
    element.showPopover();
    return { destroy: () => element.hidePopover() };
  }
</script>

<div class="toast-region" popover="manual" use:showToasts>
  {@render children()}
</div>

<style>
  .toast-region {
    position: fixed;
    inset: var(--space-05) var(--space-05) auto auto;
    display: flex;
    flex-direction: column;
    gap: var(--space-03);
    width: min(22rem, calc(100vw - 2 * var(--space-05)));
    max-height: calc(100dvh - 2 * var(--space-05));
    overflow-y: auto;
    margin: 0;
    padding: 0;
    border: 0;
    background: transparent;
    pointer-events: none;
  }

  .toast-region :global(.bx--toast-notification) {
    flex-shrink: 0;
    width: 100%;
    margin: 0;
    pointer-events: auto;
    overflow-wrap: anywhere;
  }
</style>
