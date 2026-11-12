import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const { outputText } = ts.transpileModule(readFileSync(new URL("../src/csv-import.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
});
const exports = {};
vm.runInNewContext(outputText, { exports });
const { createDefaultImportConfiguration, importConfigurationKey, isImportPreviewCurrent, resolveImportColumn } = exports;

const identity = () => ({
  companyId: "company-A", workspaceRequestId: 1,
  salesFile: { name: "sales.csv", size: 100 },
  statementFile: { name: "statement.csv", size: 100 },
  configurationKey: importConfigurationKey(createDefaultImportConfiguration()), requestId: 1,
});

test("mapping keys identify equivalent mappings but invalidate changed interpretation", () => {
  const first = createDefaultImportConfiguration();
  const reordered = createDefaultImportConfiguration();
  reordered.sales.columns = Object.fromEntries(Object.entries(reordered.sales.columns).reverse());
  reordered.statement.columns = Object.fromEntries(Object.entries(reordered.statement.columns).reverse());
  assert.equal(importConfigurationKey(first), importConfigurationKey(reordered));
  for (const mutate of [
    (config) => { config.sales.columns.externalId = "Documento"; },
    (config) => { config.sales.numberFormat = "pt-BR"; },
    (config) => { config.statement.delimiter = ";"; },
    (config) => { config.sales.dateFormat = "dd/MM/yyyy"; },
  ]) {
    const changed = createDefaultImportConfiguration();
    mutate(changed);
    assert.notEqual(importConfigurationKey(first), importConfigurationKey(changed));
  }
  assert.notEqual(importConfigurationKey(null), importConfigurationKey(first),
    "Legacy automatic numeric conventions must differ from explicit configuration.");
});

test("draft mapping edits do not leak into another fresh import configuration", () => {
  const draft = createDefaultImportConfiguration();
  const pristine = importConfigurationKey(createDefaultImportConfiguration());
  draft.sales.columns.externalId = "Documento";
  draft.statement.numberFormat = "pt-BR";
  assert.equal(importConfigurationKey(createDefaultImportConfiguration()), pristine);
  assert.equal(draft.statement.columns.saleExternalId, "sale_id");
});

test("late preview cannot be reused after changing company, workspace, files, mapping or request", () => {
  const original = identity();
  assert.equal(isImportPreviewCurrent(original, { ...original }), true);
  for (const changes of [
    { companyId: "company-B" },
    { workspaceRequestId: 3 }, // A → B → A still belongs to a newer workspace.
    { salesFile: { ...original.salesFile } }, // Same filename and size, different file.
    { statementFile: { ...original.statementFile } },
    { salesFile: null },
    { configurationKey: "new-mapping" },
    { requestId: 2 },
  ]) {
    assert.equal(isImportPreviewCurrent(original, { ...original, ...changes }), false);
  }
});

test("saved column mappings resolve unique case/whitespace/BOM variants without changing the draft", () => {
  for (const [configured, actual] of [
    ["Documento", "DOCUMENTO"],
    [" documento ", "Documento"],
    ["Documento", " \t\uFEFF\uFEFFDOCUMENTO \r\n"],
    ["\uFEFFDocumento", "Documento"],
    ["Documento", "\u0085Documento\u0085"],
    ["Comissão", "COMISSÃO"],
  ]) {
    const headers = ["Outra coluna", actual, "Total"];
    const originalHeaders = [...headers];
    const result = resolveImportColumn(configured, headers);
    assert.equal(result.status, "matched", `${JSON.stringify(configured)} must match ${JSON.stringify(actual)}.`);
    assert.equal(result.selected, actual, "Select must use the exact header text of the current file.");
    assert.deepEqual(headers, originalHeaders);
  }
  const configuration = createDefaultImportConfiguration();
  configuration.sales.columns.externalId = "Documento";
  const saved = importConfigurationKey(configuration);
  resolveImportColumn(configuration.sales.columns.externalId, ["DOCUMENTO"]);
  assert.equal(importConfigurationKey(configuration), saved, "Resolving a matching header must not silently edit a saved mapping.");
});

test("column normalization follows .NET trim-before-leading-BOM order", () => {
  // Removing BOM must not introduce a second trim operation: whitespace after
  // a leading BOM, or a trailing BOM, remains significant to the backend.
  for (const header of ["\uFEFF Documento", "Documento\uFEFF "]) {
    const result = resolveImportColumn("Documento", [header]);
    assert.equal(result.status, "missing");
    assert.equal(result.selected, "Documento");
  }
});

test("ambiguous normalized headers never select an arbitrary duplicate", () => {
  for (const headers of [
    ["Documento", "Documento"],
    ["Documento", " documento "],
    ["DOCUMENTO", "\uFEFFDocumento"],
  ]) {
    const result = resolveImportColumn("Documento", headers);
    assert.equal(result.status, "ambiguous");
    assert.equal(result.selected, "");
  }
});

test("missing column retains the saved mapping so the user can correct it", () => {
  const result = resolveImportColumn("Número da nota", ["Pedido", "Total"]);
  assert.equal(result.status, "missing");
  assert.equal(result.selected, "Número da nota");
  assert.equal(resolveImportColumn("", ["Documento"]).status, "missing");
});
