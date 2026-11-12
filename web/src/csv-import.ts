import type { ImportConfiguration, SourceImportConfiguration } from "./types";

export function getImportDateFormatHelp(format: SourceImportConfiguration["dateFormat"]): string {
  if (format === "dd/MM/yyyy") return "Exemplo: 31/12/2026.";
  if (format === "yyyy-MM-dd") return "Exemplo: 2026-12-31.";
  return "Aceita 31/12/2026 e 2026-12-31.";
}

export function createDefaultImportConfiguration(): ImportConfiguration {
  return {
    version: 1,
    sales: {
      delimiter: "auto", numberFormat: "invariant", dateFormat: "auto",
      columns: { externalId: "sale_id", date: "sale_date", grossAmount: "gross_amount" },
    },
    statement: {
      delimiter: "auto", numberFormat: "invariant", dateFormat: "auto",
      columns: { saleExternalId: "sale_id", commissionAmount: "commission_amount" },
    },
  };
}

export function importConfigurationKey(configuration: ImportConfiguration | null): string {
  if (!configuration) return "legacy";
  return JSON.stringify({
    version: configuration.version,
    ...Object.fromEntries((["sales", "statement"] as const).map((kind) => {
      const source = configuration[kind];
      return [kind, {
        delimiter: source.delimiter,
        numberFormat: source.numberFormat,
        dateFormat: source.dateFormat,
        columns: Object.fromEntries(Object.entries(source.columns).sort(([a], [b]) => a.localeCompare(b))),
      }];
    })),
  });
}

export function hasCustomImportConfiguration(configuration: ImportConfiguration | null): boolean {
  return configuration !== null;
}

export function resolveImportColumn(configuredColumn: string, headers: readonly string[]): {
  status: "matched" | "missing" | "ambiguous";
  selected: string;
} {
  // Match the backend's Trim().TrimStart(BOM).ToLowerInvariant() order.
  // JavaScript trim also strips BOM, so keep it until the explicit BOM step.
  const normalize = (value: string) => value
    .replace(/^(?:[^\S\uFEFF]|\u0085)+|(?:[^\S\uFEFF]|\u0085)+$/g, "")
    .replace(/^\uFEFF+/, "")
    .toLowerCase();
  const normalized = normalize(configuredColumn);
  if (!normalized) return { status: "missing", selected: configuredColumn };
  const matches = headers.filter((header) => normalize(header) === normalized);
  if (matches.length === 1) return { status: "matched", selected: matches[0] };
  if (matches.length > 1) return { status: "ambiguous", selected: "" };
  return { status: "missing", selected: configuredColumn };
}

export interface ImportPreviewIdentity {
  companyId: string;
  workspaceRequestId: number;
  salesFile: File | null;
  statementFile: File | null;
  configurationKey: string;
  requestId: number;
}

export function isImportPreviewCurrent(snapshot: ImportPreviewIdentity, current: ImportPreviewIdentity): boolean {
  return snapshot.companyId === current.companyId
    && snapshot.workspaceRequestId === current.workspaceRequestId
    && snapshot.salesFile === current.salesFile
    && snapshot.statementFile === current.statementFile
    && snapshot.configurationKey === current.configurationKey
    && snapshot.requestId === current.requestId;
}
