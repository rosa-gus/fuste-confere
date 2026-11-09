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

export function explainApiError(message: string): string {
  if (message === "NETWORK_UNAVAILABLE") {
    return "Não foi possível conectar ao Project Conferidor. Confira se a API está em execução.";
  }

  const exact: Record<string, string> = {
    "Company not found.": "Representada não encontrada.",
    "Agreement not found.": "Acordo não encontrado. Ele pode ter sido excluído em outra sessão.",
    "Reconciliation not found.": "Conferência não encontrada. Ela pode ter sido excluída em outra sessão.",
    "Company name is required.": "Informe o nome da representada.",
    "Company name must not exceed 160 characters.":
      "O nome pode ter no máximo 160 caracteres.",
    "A company with this name already exists.":
      "Já existe uma representada com esse nome.",
    "Agreement effective date is required.":
      "Informe a data de início do acordo.",
    "An agreement already exists for this effective date.":
      "Já existe um acordo com essa data de início.",
    "The sales file contains no records.":
      "O arquivo de vendas não contém registros.",
    "The reconciliation exceeds the supported numeric range.":
      "Não foi possível calcular a conferência: os valores excedem o limite numérico suportado.",
    "Upload the files as multipart/form-data.": "Envie os dois arquivos CSV.",
    "Provide files in the 'sales' and 'statement' fields.":
      "Selecione os arquivos de vendas e de comissões.",
  };
  if (exact[message]) return exact[message];

  const lines = message
    .split("\n")
    .map((line) => {
      let match = line.match(
        /^The sales file contains a duplicate sale ID: (.+)\.$/,
      );
      if (match)
        return `O arquivo de vendas repete o identificador ${match[1]}.`;

      match = line.match(/^The (sales|statement) file is empty\.$/);
      if (match)
        return `O arquivo de ${match[1] === "sales" ? "vendas" : "comissões"} está vazio.`;

      match = line.match(/^The (sales|statement) file has no header row\.$/);
      if (match)
        return `O arquivo de ${match[1] === "sales" ? "vendas" : "comissões"} não contém cabeçalho.`;

      match = line.match(
        /^The (sales|statement) file is invalid: unclosed quotation mark\.$/,
      );
      if (match)
        return `O arquivo de ${match[1] === "sales" ? "vendas" : "comissões"} tem aspas sem fechamento.`;

      match = line.match(
        /^The (sales|statement) file has (\d+) columns at row (\d+); expected (\d+)\.$/,
      );
      if (match) {
        const kind = match[1] === "sales" ? "vendas" : "comissões";
        return `O arquivo de ${kind} tem ${match[2]} colunas na linha ${match[3]}; eram esperadas ${match[4]}.`;
      }

      match = line.match(/^Required column is missing: (.+)\.$/);
      if (match)
        return `Falta a coluna obrigatória ${translateColumn(match[1])}.`;

      match = line.match(/^(Sales|Statement): (.+) at row (\d+)\.$/);
      if (match) {
        const file = match[1] === "Sales" ? "Vendas" : "Comissões";
        const limit = match[2].match(/^(gross|commission) amount must not exceed ([0-9.]+)$/);
        if (limit) {
          const field = limit[1] === "gross" ? "o valor bruto" : "a comissão";
          return `${file}: ${field} deve ser de, no máximo, ${formatCurrency(Number(limit[2]))} na linha ${match[3]}.`;
        }
        const detail: Record<string, string> = {
          "missing sale ID": "falta o identificador da venda",
          "invalid sale date": "a data da venda é inválida",
          "invalid gross amount": "o valor bruto é inválido",
          "invalid commission amount": "o valor da comissão é inválido",
        };
        return `${file}: ${detail[match[2]] ?? "há um dado inválido"} na linha ${match[3]}.`;
      }

      match = line.match(/^(Gross sale amount|Reported commission) must not exceed ([0-9.]+)\.$/);
      if (match) {
        const field = match[1] === "Gross sale amount" ? "O valor bruto da venda" : "A comissão informada";
        return `${field} deve ser de, no máximo, ${formatCurrency(Number(match[2]))}.`;
      }

      match = line.match(/^Reported commission total for sale ID (.+) must not exceed ([0-9.]+)\.$/);
      if (match)
        return `A soma das comissões informadas para a venda ${match[1]} deve ser de, no máximo, ${formatCurrency(Number(match[2]))}.`;

      if (line === "Percentage must be between 0 and 100.")
        return "O percentual deve estar entre 0 e 100.";
      if (line === "Sale ID must contain between 1 and 120 characters.")
        return "O identificador da venda deve ter até 120 caracteres.";
      if (line === "Gross sale amount cannot be negative.")
        return "O valor bruto da venda não pode ser negativo.";
      if (line === "Reported commission cannot be negative.")
        return "A comissão informada não pode ser negativa.";
      return "";
    })
    .filter(Boolean);

  return lines.length > 0
    ? lines.join(" ")
    : "Não foi possível concluir a operação. Confira os dados e tente novamente.";
}

function translateColumn(column: string): string {
  const labels: Record<string, string> = {
    sale_id: "sale_id (identificador da venda)",
    sale_date: "sale_date (data da venda)",
    gross_amount: "gross_amount (valor bruto)",
    commission_amount: "commission_amount (valor da comissão)",
  };
  return labels[column] ?? column;
}
