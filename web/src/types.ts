export interface Company {
  id: string;
  name: string;
  createdAt: string;
}

export interface Agreement {
  id: string;
  companyId: string;
  effectiveFrom: string;
  percentage: number;
}

export type ReconciliationStatus =
  | "Matched"
  | "Difference"
  | "MissingFromStatement"
  | "UnrecognizedSale"
  | "MissingAgreement";

export interface ReconciliationLine {
  saleExternalId: string;
  status: ReconciliationStatus;
  saleDate: string | null;
  grossAmount: number | null;
  agreementId: string | null;
  commissionPercentage: number | null;
  expectedCommission: number | null;
  reportedCommission: number | null;
  difference: number | null;
}

export interface Reconciliation {
  id: string;
  companyId: string;
  createdAt: string;
  lines: ReconciliationLine[];
  importConfiguration?: ImportConfiguration | null;
}

export type HistoryDeletionTarget =
  | { kind: "agreement"; agreement: Agreement }
  | { kind: "reconciliation"; reconciliation: Reconciliation };

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  issues?: ImportIssue[];
}

export type ImportSourceKind = "sales" | "statement";

export interface SourceImportConfiguration {
  delimiter: "," | ";" | "auto";
  numberFormat: "pt-BR" | "invariant";
  dateFormat: "dd/MM/yyyy" | "yyyy-MM-dd" | "auto";
  columns: Record<string, string>;
}

export interface ImportConfiguration {
  version: 1;
  sales: SourceImportConfiguration;
  statement: SourceImportConfiguration;
}

export interface ImportProfile {
  id: string;
  companyId: string;
  name: string;
  revision: number;
  configuration: ImportConfiguration;
  createdAt: string;
  updatedAt: string;
}

export interface ImportIssue {
  sourceKind: ImportSourceKind | "configuration";
  row: number | null;
  column: string | null;
  field: string | null;
  code: string;
  message: string;
}

export interface ImportPreviewSource {
  sourceKind: ImportSourceKind;
  headers: string[];
  sampleRows: string[][];
  normalizedRows: Record<string, string | null>[];
  totalRows: number;
  delimiter: string;
}

export interface ImportPreview {
  configuration: ImportConfiguration;
  sources: ImportPreviewSource[];
  issues: ImportIssue[];
  totalIssues: number;
  issuesTruncated: boolean;
  isValid: boolean;
  validationComplete: boolean;
}
