<script lang="ts">
  import { PageHeader, Section } from "carbon-components-svelte";
  import type { Snippet } from "svelte";

  let {
    title,
    subtitle,
    eyebrow,
    headingId,
    level = 2,
    size = "sm",
    fill = "background",
    class: headerClass = "",
    actions,
    titleEnd,
  }: {
    title: string;
    subtitle?: string;
    eyebrow?: string;
    headingId?: string;
    level?: 1 | 2 | 3;
    size?: "default" | "sm";
    fill?: "background" | "layer-01" | "layer-02";
    class?: string;
    actions?: Snippet;
    titleEnd?: Snippet;
  } = $props();
</script>

<Section {level} tag="div">
  <PageHeader
    {title}
    {subtitle}
    {eyebrow}
    {size}
    {fill}
    hideDivider
    class={`section-header ${headerClass}`}
  >
    <span slot="titleChildren" id={headingId}>{title}</span>
    <svelte:fragment slot="actions">
      {#if actions}{@render actions()}{/if}
    </svelte:fragment>
    <svelte:fragment slot="titleEnd">
      {#if titleEnd}{@render titleEnd()}{/if}
    </svelte:fragment>
  </PageHeader>
</Section>
