import type { ImportConfiguration, ImportIssue, SourceImportConfiguration } from "./types";

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

export function getImportIssueMessage(issue: ImportIssue): string {
  const messages: Record<string, string> = {
    missing_column: "A coluna configurada não foi encontrada no arquivo.",
    ambiguous_column: "Há mais de uma coluna com esse nome. Use cabeçalhos distintos.",
    duplicate_header: "O arquivo contém cabeçalhos repetidos.",
    missing_value: "Preencha o valor obrigatório.",
    invalid_date: "A data não corresponde ao formato selecionado.",
    invalid_amount: "O valor não corresponde ao formato numérico selecionado.",
    duplicate_sale_id: "O identificador da venda aparece mais de uma vez.",
    duplicate_id: "O identificador da venda aparece mais de uma vez.",
    invalid_configuration: "Confira os formatos e as colunas da configuração de importação.",
    invalid_csv: "O arquivo CSV tem uma estrutura inválida. Confira o separador, as aspas e a quantidade de colunas de cada linha.",
    empty_header: "Todas as colunas devem ter um título no cabeçalho.",
    invalid_id: "O identificador é obrigatório e deve ter até 120 caracteres.",
    amount_exceeds_limit: "O valor excede o limite numérico suportado.",
    total_exceeds_limit: "A soma das comissões desta venda excede o limite numérico suportado.",
    no_records: "O arquivo de vendas não contém registros.",
  };
  return messages[issue.code] ?? issue.message;
}
