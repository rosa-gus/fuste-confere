import type { ReconciliationStatus } from "./types";

const currency = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

const dateTime = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "short",
  timeStyle: "short",
});

export function formatCurrency(value: number | null): string {
  return value === null ? "—" : currency.format(value);
}

export function formatDifference(value: number | null): string {
  if (value === null) return "—";
  return value > 0 ? `+${currency.format(value)}` : currency.format(value);
}

export function formatDate(value: string | null): string {
  if (!value) return "—";
  const [year, month, day] = value.slice(0, 10).split("-");
  return year && month && day ? `${day}/${month}/${year}` : value;
}

export function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : dateTime.format(date);
}

export function formatPercentage(value: number | null): string {
  if (value === null) return "—";
  return `${value.toLocaleString("pt-BR", { maximumFractionDigits: 4 })}%`;
}

export const statusLabels: Record<ReconciliationStatus, string> = {
  Matched: "Conferido",
  Difference: "Divergência",
  MissingFromStatement: "Sem registro no demonstrativo",
  UnrecognizedSale: "Venda não localizada",
  MissingAgreement: "Sem acordo vigente",
};
