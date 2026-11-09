import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

function loadModule(path) {
  const source = readFileSync(new URL(path, import.meta.url), "utf8");
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  });
  const exports = {};
  vm.runInNewContext(outputText, { exports });
  return exports;
}

const { explainApiError } = loadModule("../src/format.ts");
const { parseMoneyFilter, validateResultFilters, filterResultLines, paginateResultLines } = loadModule("../src/reconciliation-results.ts");
const normalizeSpaces = (text) => text.replace(/[\u00a0\u202f]/g, " ");

test("statement CSV errors retain specific Portuguese explanations", () => {
  for (const [input, expected] of [
    ["The statement file is empty.", "O arquivo de comissões está vazio."],
    ["The statement file has no header row.", "O arquivo de comissões não contém cabeçalho."],
    ["The statement file is invalid: unclosed quotation mark.", "O arquivo de comissões tem aspas sem fechamento."],
    ["The statement file has 3 columns at row 2; expected 2.", "O arquivo de comissões tem 3 colunas na linha 2; eram esperadas 2."],
  ]) {
    assert.equal(explainApiError(input), expected);
  }
});

test("numeric limits, aggregate errors and overflow are explained in Portuguese", () => {
  for (const [input, expected] of [
    ["Sales: gross amount must not exceed 999999999999.99 at row 2.", "Vendas: o valor bruto deve ser de, no máximo, R$ 999.999.999.999,99 na linha 2."],
    ["Statement: commission amount must not exceed 999999999999.99 at row 7.", "Comissões: a comissão deve ser de, no máximo, R$ 999.999.999.999,99 na linha 7."],
    ["Gross sale amount must not exceed 999999999999.99.\n(Parameter 'grossAmount')", "O valor bruto da venda deve ser de, no máximo, R$ 999.999.999.999,99."],
    ["Reported commission must not exceed 999999999999.99.", "A comissão informada deve ser de, no máximo, R$ 999.999.999.999,99."],
    ["Reported commission total for sale ID VEN-001 must not exceed 999999999999.99.", "A soma das comissões informadas para a venda VEN-001 deve ser de, no máximo, R$ 999.999.999.999,99."],
    ["The reconciliation exceeds the supported numeric range.", "Não foi possível calcular a conferência: os valores excedem o limite numérico suportado."],
  ]) {
    assert.equal(normalizeSpaces(explainApiError(input)), expected);
  }
});

test("several CSV validation errors retain their individual row numbers", () => {
  assert.equal(explainApiError("Sales: invalid gross amount at row 2.\nStatement: invalid commission amount at row 5."),
    "Vendas: o valor bruto é inválido na linha 2. Comissões: o valor da comissão é inválido na linha 5.");
});

test("money filters accept supported notation and reject malformed or unsafe numbers", () => {
  for (const [input, value] of [["1.234,56", 1234.56], ["R$ 1.234,56", 1234.56], ["1234.56", 1234.56], ["-0,01", -0.01], ["0", 0]]) {
    assert.equal(parseMoneyFilter(input), value);
  }
  for (const input of ["", "1e3", "12.34,56", "12,345", "1.2.3", "12x", "999999999999999999999"]) {
    assert.equal(parseMoneyFilter(input), null);
  }
});

const defaults = { search: "", status: "all", dateFrom: "", dateTo: "", amountField: "grossAmount", minimum: "", maximum: "" };
const line = (id, overrides = {}) => ({ saleExternalId: id, status: "Matched", saleDate: "2026-03-01", grossAmount: 100,
  expectedCommission: 5, reportedCommission: 5, difference: 0, ...overrides });

test("filters combine inclusive date, ID, status and monetary bounds", () => {
  const rows = [line("VEN-A"), line("VEN-B", { saleDate: "2026-03-02", status: "Difference", difference: -1 }), line("OTHER")];
  const filters = { ...defaults, search: "ven", status: "Difference", dateFrom: "2026-03-02", dateTo: "2026-03-02",
    amountField: "difference", minimum: "-1", maximum: "-1" };
  assert.equal(filterResultLines(rows, filters).map((row) => row.saleExternalId).join(), "VEN-B");
});

test("null values are excluded only by active bounds and never treated as zero", () => {
  const rows = [line("missing", { grossAmount: null, saleDate: null }), line("zero", { grossAmount: 0 })];
  assert.equal(filterResultLines(rows, defaults).length, 2);
  assert.equal(filterResultLines(rows, { ...defaults, minimum: "0", maximum: "0" }).map((row) => row.saleExternalId).join(), "zero");
  assert.equal(filterResultLines(rows, { ...defaults, dateFrom: "2026-03-01" }).map((row) => row.saleExternalId).join(), "zero");
});

test("invalid and reversed ranges prevent filtering results", () => {
  for (const change of [{ minimum: "20", maximum: "10" }, { minimum: "-1" }, { maximum: "bad" }, { dateFrom: "2026-03-02", dateTo: "2026-03-01" }]) {
    const filters = { ...defaults, ...change };
    assert.ok(Object.keys(validateResultFilters(filters)).length > 0);
    assert.equal(filterResultLines([line("V1")], filters).length, 0);
  }
});

test("pagination runs after filtering and clamps pages when the result shrinks", () => {
  const rows = Array.from({ length: 30 }, (_, index) => line(`V${index}`, { grossAmount: index }));
  const filtered = filterResultLines(rows, { ...defaults, minimum: "20" });
  const page = paginateResultLines(filtered, 3, 10);
  assert.equal(page.page, 1);
  assert.equal(page.items.length, 10);
  assert.equal(page.items[0].saleExternalId, "V20");
  assert.equal(page.start, 1);
  assert.equal(page.end, 10);
  const empty = paginateResultLines([], 3, 25);
  assert.equal(empty.start, 0);
  assert.equal(empty.end, 0);
});
