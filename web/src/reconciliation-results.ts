import type { ReconciliationLine, ReconciliationStatus } from "./types";

export type AmountField = "grossAmount" | "expectedCommission" | "reportedCommission" | "difference";
export interface ResultFilters {
  search: string;
  status: ReconciliationStatus | "all";
  dateFrom: string;
  dateTo: string;
  amountField: AmountField;
  minimum: string;
  maximum: string;
}

export interface ResultFilterErrors {
  dateRange?: string;
  minimum?: string;
  maximum?: string;
}

/** Parses Brazilian money input without accepting exponent notation or partial numbers.
 * Ungrouped decimal-dot input is also accepted. Blank/invalid input returns null;
 * callers distinguish these cases using the trimmed input. Bounds use whole cents.
 */
export function parseMoneyFilter(input: string): number | null {
  const text = input.trim().replace(/^R\$\s*/i, "");
  if (!text) return null;
  let normalized: string;
  if (text.includes(",")) {
    if (!/^[+-]?(?:\d+|\d{1,3}(?:\.\d{3})+),\d{1,2}$/.test(text)) return null;
    normalized = text.replaceAll(".", "").replace(",", ".");
  } else if (/^[+-]?\d{1,3}(?:\.\d{3})+$/.test(text)) {
    normalized = text.replaceAll(".", "");
  } else {
    if (!/^[+-]?\d+(?:\.\d{1,2})?$/.test(text)) return null;
    normalized = text;
  }
  const negative = normalized.startsWith("-");
  const [integer, fraction = ""] = normalized.replace(/^[+-]/, "").split(".");
  const cents = Number(integer) * 100 + Number(fraction.padEnd(2, "0"));
  return Number.isSafeInteger(cents) ? (negative ? -cents : cents) / 100 : null;
}

export function validateResultFilters(filters: ResultFilters): ResultFilterErrors {
  const errors: ResultFilterErrors = {};
  if (filters.dateFrom && filters.dateTo && filters.dateFrom > filters.dateTo) {
    errors.dateRange = "A data final deve ser igual ou posterior à inicial.";
  }
  const minimum = parseMoneyFilter(filters.minimum);
  const maximum = parseMoneyFilter(filters.maximum);
  if (filters.minimum.trim() && minimum === null) errors.minimum = "Informe um valor válido com até duas casas decimais.";
  if (filters.maximum.trim() && maximum === null) errors.maximum = "Informe um valor válido com até duas casas decimais.";
  if (filters.amountField !== "difference") {
    if (minimum !== null && minimum < 0) errors.minimum = "Use um valor maior ou igual a zero.";
    if (maximum !== null && maximum < 0) errors.maximum = "Use um valor maior ou igual a zero.";
  }
  if (!errors.minimum && !errors.maximum && minimum !== null && maximum !== null && minimum > maximum) {
    errors.maximum = "O máximo deve ser maior ou igual ao mínimo.";
  }
  return errors;
}

/** Filters the full snapshot before pagination, preserving its original order.
 * Date bounds are inclusive sale dates. Null dates/amounts match only when
 * the corresponding bound is inactive; missing amounts are never treated as zero.
 */
export function filterResultLines(lines: readonly ReconciliationLine[], filters: ResultFilters): ReconciliationLine[] {
  if (Object.keys(validateResultFilters(filters)).length) return [];
  const search = filters.search.trim().toLocaleLowerCase("pt-BR");
  const minimum = parseMoneyFilter(filters.minimum);
  const maximum = parseMoneyFilter(filters.maximum);
  return lines.filter((line) => {
    if (search && !line.saleExternalId.toLocaleLowerCase("pt-BR").includes(search)) return false;
    if (filters.status !== "all" && line.status !== filters.status) return false;
    if (filters.dateFrom || filters.dateTo) {
      const date = line.saleDate?.slice(0, 10);
      if (!date || (filters.dateFrom && date < filters.dateFrom) || (filters.dateTo && date > filters.dateTo)) return false;
    }
    if (minimum !== null || maximum !== null) {
      const amount = line[filters.amountField];
      if (amount === null || (minimum !== null && amount < minimum) || (maximum !== null && amount > maximum)) return false;
    }
    return true;
  });
}

export function paginateResultLines(lines: readonly ReconciliationLine[], requestedPage: number, requestedSize: number) {
  const pageSize = [10, 25, 50, 100].includes(requestedSize) ? requestedSize : 25;
  const totalPages = Math.max(1, Math.ceil(lines.length / pageSize));
  const page = Math.min(totalPages, Math.max(1, Number.isFinite(requestedPage) ? Math.trunc(requestedPage) : 1));
  const offset = (page - 1) * pageSize;
  return {
    page,
    totalPages,
    items: lines.slice(offset, offset + pageSize),
    start: lines.length ? offset + 1 : 0,
    end: Math.min(offset + pageSize, lines.length),
  };
}
