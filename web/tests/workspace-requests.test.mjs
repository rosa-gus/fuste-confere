import { randomUUID } from "node:crypto";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Run App's actual request handlers with controlled promises. Runes are reduced
// to plain state: these tests verify async data ownership, not DOM or rendering.
const source = readFileSync(new URL("../src/App.svelte", import.meta.url), "utf8")
  .match(/<script lang="ts">([\s\S]*?)<\/script>/)[1];
const parsed = ts.createSourceFile("App.ts", source, ts.ScriptTarget.ES2022, true, ts.ScriptKind.TS);
const withoutImports = ts.factory.updateSourceFile(
  parsed, parsed.statements.filter((statement) => !ts.isImportDeclaration(statement)),
);
const { outputText } = ts.transpileModule(ts.createPrinter().printFile(withoutImports), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None },
});

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

const reconciliation = (companyId, id = `history-${companyId}`) => ({ id, companyId, lines: [] });
const agreement = (companyId, id = `agreement-${companyId}`) => ({ id, companyId });
const submit = { preventDefault() {} };

const importModule = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/csv-import.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText, { exports: importModule });

class ApiError extends Error {
  constructor(message, status = 400, problem) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

function application() {
  const api = {
    listAgreements: async (id) => [agreement(id)],
    listReconciliations: async (id) => [reconciliation(id)],
    createAgreement: async (id) => agreement(id, "created"),
    createReconciliation: async (id) => reconciliation(id, "created"),
  };
  const derived = Object.assign((value) => value, { by: (fn) => fn() });
  const context = vm.createContext({
    api,
    exports: {},
    $state: (value) => value,
    $derived: derived,
    onMount() {},
    tick: async () => {},
    document: { getElementById: () => null },
    ApiError,
    crypto: { randomUUID },
  });
  vm.runInContext(`${outputText}
    globalThis.handlers = {
      chooseCompany, createAgreement, createReconciliation, selectFile,
      dropFile, enterFileDrag, leaveFileDrag, overFileDrag, preventOutsideFileDrop,
      handleImportConfigurationChange, confirmReimport, openPreviousImport, closeDuplicateImport,
      setImportValidated: (value) => { importValidated = value; },
      setImportSetupBusy: (value) => { importSetupBusy = value; },
      dismissErrorToast: () => { errorMessage = ""; },
      read: () => ({ selectedCompanyId, agreements, reconciliations,
        activeReconciliation, salesFile, statementFile, successMessage,
        errorMessage, workspaceLoading, savingAgreement, reconciling, draggingFileKind,
        importConfiguration, importConfigurationRevision, importValidated, importSetupBusy, importExecutionError, duplicateImport })
    };`, context);
  const handlers = context.handlers;
  return {
    api, ...handlers,
    choose: (id) => handlers.chooseCompany({ currentTarget: { value: id } }),
    upload: (name) => {
      for (const kind of ["sales", "statement"]) {
        handlers.selectFile(kind, { currentTarget: { files: [{ name: `${name}-${kind}.csv`, size: 1 }] } });
      }
    },
  };
}

for (const outcome of ["success", "error"]) {
  test(`late reconciliation ${outcome} from A leaves B and its uploads untouched`, async () => {
    const app = application();
    await app.choose("A");
    app.upload("A");
    const pending = deferred();
    app.api.createReconciliation = (id, sales, statement) => {
      assert.equal(id, "A");
      assert.equal(sales.name, "A-sales.csv");
      assert.equal(statement.name, "A-statement.csv");
      return pending.promise;
    };
    const operation = app.createReconciliation(submit);
    await app.choose("B");
    assert.equal(app.read().salesFile, null);
    app.upload("B");
    if (outcome === "success") pending.resolve(reconciliation("A", "late-A"));
    else pending.reject(new Error("A failed"));
    await operation;
    const state = app.read();
    assert.equal(state.activeReconciliation.id, "history-B");
    assert.equal(state.reconciliations.map((item) => item.companyId).join(), "B");
    assert.equal(state.salesFile.name, "B-sales.csv");
    assert.equal(state.statementFile.name, "B-statement.csv");
    assert.equal(state.errorMessage, "");
    assert.equal(state.successMessage, "");
    assert.equal(state.reconciling, false);
  });
}

test("returning A → B → A does not revive an earlier reconciliation response", async () => {
  const app = application();
  await app.choose("A");
  app.upload("original-A");
  const pending = deferred();
  app.api.createReconciliation = () => pending.promise;
  const operation = app.createReconciliation(submit);
  await app.choose("B");
  await app.choose("A");
  app.upload("new-A");
  pending.resolve(reconciliation("A", "obsolete-response"));
  await operation;
  assert.equal(app.read().activeReconciliation.id, "history-A");
  assert.equal(app.read().salesFile.name, "new-A-sales.csv");
  assert.equal(app.read().successMessage, "");
});

for (const phase of ["save", "reload"]) {
  test(`switching company during agreement ${phase} preserves B's agreements`, async () => {
    const app = application();
    await app.choose("A");
    const pending = deferred();
    const startedReload = deferred();
    const fetchedCompanies = [];
    app.api.createAgreement = (id) => {
      assert.equal(id, "A");
      return phase === "save" ? pending.promise : Promise.resolve(agreement(id));
    };
    app.api.listAgreements = (id) => {
      fetchedCompanies.push(id);
      if (phase === "reload" && id === "A") {
        startedReload.resolve();
        return pending.promise;
      }
      return Promise.resolve([agreement(id)]);
    };
    const operation = app.createAgreement(submit);
    if (phase === "reload") await startedReload.promise;
    await app.choose("B");
    pending.resolve(phase === "save" ? agreement("A") : [agreement("A", "late-A")]);
    await operation;
    assert.equal(app.read().agreements.map((item) => item.companyId).join(), "B");
    assert.equal(app.read().successMessage, "");
    assert.equal(app.read().savingAgreement, false);
    assert.equal(fetchedCompanies.join(), phase === "save" ? "B" : "A,B");
  });
}

test("out-of-order A → B → A loads cannot replace fresh data or clear its loading state", async () => {
  const app = application();
  const loads = [];
  app.api.listAgreements = () => {
    const pending = deferred();
    loads.push(pending);
    return pending.promise;
  };
  const first = app.choose("A");
  const second = app.choose("B");
  const third = app.choose("A");
  loads[0].resolve([agreement("A", "obsolete")]);
  await first;
  assert.equal(app.read().workspaceLoading, true);
  assert.equal(app.read().agreements.length, 0);
  loads[2].resolve([agreement("A", "fresh")]);
  await third;
  loads[1].reject(new Error("B failed"));
  await second;
  assert.equal(app.read().agreements[0].id, "fresh");
  assert.equal(app.read().workspaceLoading, false);
  assert.equal(app.read().errorMessage, "");
});

test("failed company load clears the previous company's records and files", async () => {
  const app = application();
  await app.choose("A");
  app.upload("A");
  app.api.listAgreements = async () => { throw new Error("B failed"); };
  await app.choose("B");
  const state = app.read();
  assert.equal(state.agreements.length, 0);
  assert.equal(state.reconciliations.length, 0);
  assert.equal(state.activeReconciliation, null);
  assert.equal(state.salesFile, null);
  assert.notEqual(state.errorMessage, "");
});

test("current-company saves still update results, clear uploads and show success", async () => {
  const app = application();
  await app.choose("A");
  await app.createAgreement(submit);
  assert.notEqual(app.read().successMessage, "");
  app.upload("A");
  await app.createReconciliation(submit);
  assert.equal(app.read().activeReconciliation.id, "created");
  assert.equal(app.read().activeReconciliation.companyId, "A");
  assert.equal(app.read().reconciliations.length, 2);
  assert.equal(app.read().salesFile, null);
  assert.notEqual(app.read().successMessage, "");
});

function dragEvent(files = []) {
  return {
    dataTransfer: { files, types: ["Files"], dropEffect: "" },
    prevented: false,
    stopped: false,
    preventDefault() { this.prevented = true; },
    stopPropagation() { this.stopped = true; },
  };
}

test("CSV drops populate their respective slots and are submitted as file objects", async () => {
  const app = application();
  await app.choose("A");
  const sales = { name: "sales.CSV", size: 100 };
  const statement = { name: "statement.csv", size: 200 };
  const event = dragEvent([sales]);
  app.dropFile("sales", event);
  app.dropFile("statement", dragEvent([statement]));
  assert.equal(event.prevented, true);
  assert.equal(event.stopped, true);
  assert.equal(app.read().salesFile, sales);
  assert.equal(app.read().statementFile, statement);
  app.api.createReconciliation = async (companyId, sentSales, sentStatement) => {
    assert.equal(companyId, "A");
    assert.equal(sentSales, sales);
    assert.equal(sentStatement, statement);
    return reconciliation(companyId, "dropped-files");
  };
  await app.createReconciliation(submit);
  assert.equal(app.read().activeReconciliation.id, "dropped-files");
  assert.equal(app.read().salesFile, null);
  assert.equal(app.read().statementFile, null);
});

test("invalid and multiple dropped files preserve the previous valid selection", async () => {
  const app = application();
  await app.choose("A");
  app.upload("original");
  for (const files of [[{ name: "not-csv.xlsx", size: 1 }], [{ name: "one.csv", size: 1 }, { name: "two.csv", size: 1 }]]) {
    app.dropFile("sales", dragEvent(files));
    assert.equal(app.read().salesFile.name, "original-sales.csv");
    assert.equal(app.read().statementFile.name, "original-statement.csv");
    assert.notEqual(app.read().errorMessage, "");
  }
  app.dropFile("sales", dragEvent([{ name: "replacement.csv", size: 1 }]));
  assert.equal(app.read().salesFile.name, "replacement.csv");
  assert.equal(app.read().errorMessage, "");
});

test("click and drop share the combined size limit, including replacements", async () => {
  const app = application();
  await app.choose("A");
  const megabyte = 1024 * 1024;
  app.dropFile("sales", dragEvent([{ name: "five.csv", size: 5 * megabyte }]));
  app.selectFile("statement", { currentTarget: { files: [{ name: "four.csv", size: 4 * megabyte }] } });
  assert.equal(app.read().errorMessage, "");
  app.dropFile("sales", dragEvent([{ name: "six.csv", size: 6 * megabyte }]));
  assert.equal(app.read().salesFile.name, "five.csv");
  assert.equal(app.read().statementFile.name, "four.csv");
  assert.equal(app.read().errorMessage, "Os dois arquivos devem ter, juntos, no máximo 9 MB.");
  app.selectFile("statement", { currentTarget: { files: [{ name: "not-csv.pdf", size: 1 }] } });
  assert.equal(app.read().statementFile.name, "four.csv");
  assert.equal(app.read().errorMessage, "Selecione um arquivo com extensão .csv.");
});

test("nested drag events keep the highlight until the pointer leaves the entire area", async () => {
  const app = application();
  await app.choose("A");
  app.enterFileDrag("sales", dragEvent());
  app.enterFileDrag("sales", dragEvent());
  app.leaveFileDrag("sales", dragEvent());
  assert.equal(app.read().draggingFileKind, "sales");
  const over = dragEvent();
  app.overFileDrag(over);
  assert.equal(over.dataTransfer.dropEffect, "copy");
  app.leaveFileDrag("sales", dragEvent());
  assert.equal(app.read().draggingFileKind, null);
  app.enterFileDrag("statement", dragEvent());
  app.dropFile("statement", dragEvent([{ name: "statement.csv", size: 1 }]));
  assert.equal(app.read().draggingFileKind, null);
});

test("drops outside the upload areas prevent file navigation without selecting a file", async () => {
  const app = application();
  await app.choose("A");
  app.enterFileDrag("sales", dragEvent());
  const outside = dragEvent([{ name: "sales.csv", size: 1 }]);
  app.preventOutsideFileDrop(outside);
  assert.equal(outside.prevented, true);
  assert.equal(outside.dataTransfer.dropEffect, "none");
  assert.equal(app.read().draggingFileKind, null);
  assert.equal(app.read().salesFile, null);
  const text = dragEvent();
  text.dataTransfer.types = ["text/plain"];
  app.preventOutsideFileDrop(text);
  assert.equal(text.prevented, false);
});

test("company changes clear highlights and block drops while loading the next workspace", async () => {
  const app = application();
  await app.choose("A");
  app.enterFileDrag("sales", dragEvent());
  const pending = deferred();
  app.api.listAgreements = () => pending.promise;
  const changing = app.choose("B");
  assert.equal(app.read().draggingFileKind, null);
  app.dropFile("sales", dragEvent([{ name: "old-context.csv", size: 1 }]));
  assert.equal(app.read().salesFile, null);
  pending.resolve([agreement("B")]);
  await changing;
});

test("finishing a reconciliation preserves replacement files prepared during its request", async () => {
  const app = application();
  await app.choose("A");
  app.upload("original");
  const pending = deferred();
  app.api.createReconciliation = () => pending.promise;
  const operation = app.createReconciliation(submit);
  app.dropFile("sales", dragEvent([{ name: "next-sales.csv", size: 1 }]));
  pending.resolve(reconciliation("A", "original-files"));
  await operation;
  assert.equal(app.read().salesFile.name, "next-sales.csv");
  assert.equal(app.read().statementFile, null);
  assert.equal(app.read().activeReconciliation.id, "original-files");
});


test("custom import requires validated files and an idle setup before submitting", async () => {
  const app = application();
  await app.choose("A");
  app.upload("custom");
  let calls = 0;
  app.api.createReconciliation = async (companyId) => { calls++; return reconciliation(companyId, "configured"); };
  app.handleImportConfigurationChange(importModule.createDefaultImportConfiguration());
  await app.createReconciliation(submit);
  assert.equal(calls, 0, "Unvalidated custom configuration was submitted.");
  app.setImportValidated(true);
  app.setImportSetupBusy(true);
  await app.createReconciliation(submit);
  assert.equal(calls, 0, "Submission raced an active preview/profile save.");
  app.setImportSetupBusy(false);
  await app.createReconciliation(submit);
  assert.equal(calls, 1);
  assert.equal(app.read().activeReconciliation.id, "configured");
});

test("validated custom import sends a detached snapshot as the API's fourth argument", async () => {
  const app = application();
  await app.choose("A");
  app.upload("mapped");
  const configuration = importModule.createDefaultImportConfiguration();
  configuration.sales.columns.externalId = "Documento";
  app.handleImportConfigurationChange(configuration);
  app.setImportValidated(true);
  const pending = deferred();
  let submitted;
  app.api.createReconciliation = (companyId, sales, statement, submittedConfiguration) => {
    assert.equal(companyId, "A");
    assert.equal(sales.name, "mapped-sales.csv");
    assert.equal(statement.name, "mapped-statement.csv");
    submitted = submittedConfiguration;
    return pending.promise;
  };
  const operation = app.createReconciliation(submit);
  assert.equal(JSON.stringify(submitted), JSON.stringify(configuration));
  assert.notEqual(submitted, configuration);
  assert.notEqual(submitted.sales.columns, configuration.sales.columns);
  configuration.sales.columns.externalId = "Changed after submission";
  assert.equal(submitted.sales.columns.externalId, "Documento");
  pending.resolve(reconciliation("A", "snapshot-result"));
  await operation;
  assert.equal(app.read().activeReconciliation.id, "snapshot-result");
});

for (const outcome of ["success", "error"]) {
  test(`configuration changes ignore an old reconciliation ${outcome} without clearing current uploads`, async () => {
    const app = application();
    await app.choose("A");
    app.upload("mapped");
    app.handleImportConfigurationChange(importModule.createDefaultImportConfiguration());
    app.setImportValidated(true);
    const revision = app.read().importConfigurationRevision;
    const pending = deferred();
    app.api.createReconciliation = () => pending.promise;
    const operation = app.createReconciliation(submit);
    const updated = importModule.createDefaultImportConfiguration();
    updated.sales.columns.externalId = "New ID";
    app.handleImportConfigurationChange(updated);
    assert.equal(app.read().importValidated, false);
    assert.ok(app.read().importConfigurationRevision > revision);
    if (outcome === "success") pending.resolve(reconciliation("A", "obsolete-mapping"));
    else pending.reject(new ApiError("Failure for an obsolete mapping"));
    await operation;
    const state = app.read();
    assert.equal(state.activeReconciliation.id, "history-A");
    assert.equal(state.reconciliations.length, 1);
    assert.equal(state.salesFile.name, "mapped-sales.csv");
    assert.equal(state.statementFile.name, "mapped-statement.csv");
    assert.equal(state.importConfiguration.sales.columns.externalId, "New ID");
    assert.equal(state.errorMessage, "");
    assert.equal(state.importExecutionError, "");
    assert.equal(state.successMessage, "");
    assert.equal(state.reconciling, false);
  });
}

test("structured configured-import errors preserve API messages after the toast is dismissed", async () => {
  const app = application();
  await app.choose("A");
  app.upload("invalid");
  app.handleImportConfigurationChange(importModule.createDefaultImportConfiguration());
  app.setImportValidated(true);
  app.api.createReconciliation = async () => {
    throw new ApiError("Import validation failed.", 400, { issues: [
      { sourceKind: "sales", row: 22, column: "Total", field: "grossAmount", code: "invalid_amount", message: "O valor não corresponde ao formato numérico selecionado." },
      { sourceKind: "statement", row: 3, column: "Pedido", field: "saleExternalId", code: "invalid_id", message: "O identificador é obrigatório e deve ter até 120 caracteres." },
    ] });
  };
  await app.createReconciliation(submit);
  const diagnostic = app.read().importExecutionError;
  assert.match(diagnostic, /Vendas, linha 22, coluna “Total”/);
  assert.match(diagnostic, /Demonstrativo, linha 3, coluna “Pedido”/);
  assert.match(diagnostic, /O valor não corresponde ao formato numérico selecionado/);
  assert.match(diagnostic, /O identificador é obrigatório/);
  assert.match(app.read().errorMessage, /diagnóstico/);
  app.dismissErrorToast();
  assert.equal(app.read().errorMessage, "");
  assert.equal(app.read().importExecutionError, diagnostic);
  assert.equal(app.read().salesFile.name, "invalid-sales.csv");
  assert.equal(app.read().reconciliations.length, 1);
});

const duplicateError = () => new ApiError("Already reconciled", 409, {
  code: "duplicate_import", existingReconciliation: { id: "previous", createdAt: "2026-11-12T12:00:00Z" },
});

test("duplicate warning preserves uploads; opening previous reads without submitting again", async () => {
  const app = application();
  await app.choose("A");
  app.upload("repeat");
  let calls = 0;
  app.api.createReconciliation = async () => { calls++; throw duplicateError(); };
  await app.createReconciliation(submit);
  assert.equal(app.read().duplicateImport.id, "previous");
  assert.equal(app.read().errorMessage, "");
  assert.equal(app.read().reconciliations.length, 1);
  app.api.getReconciliation = async (id) => reconciliation("A", id);
  await app.openPreviousImport();
  assert.equal(calls, 1);
  assert.equal(app.read().activeReconciliation.id, "previous");
  assert.equal(app.read().salesFile.name, "repeat-sales.csv");
  assert.equal(app.read().duplicateImport, null);
});

test("confirmation uses a fresh request ID and retains it across a network retry", async () => {
  const app = application();
  await app.choose("A");
  app.upload("repeat");
  const attempts = [];
  app.api.createReconciliation = async (_company, _sales, _statement, _config, attempt) => {
    attempts.push({ ...attempt });
    if (attempts.length === 1) throw duplicateError();
    if (attempts.length === 2) throw new ApiError("Não foi possível conectar ao Project Conferidor. Confira se a API está em execução.", 0);
    return reconciliation("A", "reprocessed");
  };
  await app.createReconciliation(submit);
  await app.confirmReimport();
  assert.equal(app.read().duplicateImport.id, "previous");
  assert.equal(app.read().salesFile.name, "repeat-sales.csv");
  await app.confirmReimport();
  assert.notEqual(attempts[0].requestId, attempts[1].requestId);
  assert.equal(attempts[1].requestId, attempts[2].requestId);
  assert.equal(attempts[1].reprocessOf, "previous");
  assert.equal(app.read().duplicateImport, null);
  assert.equal(app.read().salesFile, null);
  assert.equal(app.read().activeReconciliation.id, "reprocessed");
});

test("normal network retries reuse request IDs and do not duplicate a known history entry", async () => {
  const app = application();
  await app.choose("A");
  app.upload("repeat");
  const attempts = [];
  app.api.createReconciliation = async (_company, _sales, _statement, _config, attempt) => {
    attempts.push(attempt.requestId);
    if (attempts.length === 1) throw new ApiError("Não foi possível conectar ao Project Conferidor. Confira se a API está em execução.", 0);
    return reconciliation("A", "history-A");
  };
  await app.createReconciliation(submit);
  await app.createReconciliation(submit);
  assert.equal(attempts[0], attempts[1]);
  assert.equal(app.read().reconciliations.length, 1);
});

for (const change of ["company", "file", "configuration", "cancel"]) {
  test(`${change} invalidates the duplicate confirmation`, async () => {
    const app = application();
    await app.choose("A");
    app.upload("repeat");
    let calls = 0;
    app.api.createReconciliation = async () => { calls++; throw duplicateError(); };
    await app.createReconciliation(submit);
    if (change === "company") await app.choose("B");
    if (change === "file") app.upload("replacement");
    if (change === "configuration") app.handleImportConfigurationChange(importModule.createDefaultImportConfiguration());
    if (change === "cancel") app.closeDuplicateImport();
    assert.equal(app.read().duplicateImport, null);
    await app.confirmReimport();
    assert.equal(calls, 1);
  });
}

for (const change of ["company", "file", "configuration"]) {
  test(`late duplicate conflict cannot reopen the dialog after changing ${change}`, async () => {
    const app = application();
    await app.choose("A");
    app.upload("repeat");
    const pending = deferred();
    app.api.createReconciliation = () => pending.promise;
    const operation = app.createReconciliation(submit);
    if (change === "company") await app.choose("B");
    if (change === "file") app.upload("replacement");
    if (change === "configuration") app.handleImportConfigurationChange(importModule.createDefaultImportConfiguration());
    pending.reject(duplicateError());
    await operation;
    assert.equal(app.read().duplicateImport, null);
    assert.equal(app.read().errorMessage, "");
  });
}

test("deleted reprocess reference closes the warning and keeps uploads available", async () => {
  const app = application();
  await app.choose("A");
  app.upload("repeat");
  app.api.createReconciliation = async () => { throw duplicateError(); };
  await app.createReconciliation(submit);
  app.api.createReconciliation = async () => { throw new ApiError("Reference unavailable", 409, { code: "invalid_reprocess_reference" }); };
  await app.confirmReimport();
  assert.equal(app.read().duplicateImport, null);
  assert.equal(app.read().salesFile.name, "repeat-sales.csv");
  assert.notEqual(app.read().errorMessage, "");
});

for (const outcome of ["success", "error"]) {
  test(`opening a previous result ignores late ${outcome} after a file change`, async () => {
    const app = application();
    await app.choose("A");
    app.upload("repeat");
    app.api.createReconciliation = async () => { throw duplicateError(); };
    await app.createReconciliation(submit);
    const pending = deferred();
    app.api.getReconciliation = () => pending.promise;
    const opening = app.openPreviousImport();
    app.upload("new");
    if (outcome === "success") pending.resolve(reconciliation("A", "previous"));
    else pending.reject(new ApiError("Old result unavailable", 404));
    await opening;
    assert.equal(app.read().activeReconciliation.id, "history-A");
    assert.equal(app.read().errorMessage, "");
    assert.equal(app.read().salesFile.name, "new-sales.csv");
  });
}

test("old confirmation errors cannot reset a new upload's attempt", async () => {
  const app = application();
  await app.choose("A");
  app.upload("repeat");
  app.api.createReconciliation = async () => { throw duplicateError(); };
  await app.createReconciliation(submit);
  const pending = deferred();
  app.api.createReconciliation = () => pending.promise;
  const confirming = app.confirmReimport();
  app.upload("new");
  pending.reject(new ApiError("Old reference unavailable", 409, { code: "invalid_reprocess_reference" }));
  await confirming;
  assert.equal(app.read().duplicateImport, null);
  assert.equal(app.read().errorMessage, "");
  assert.equal(app.read().salesFile.name, "new-sales.csv");
});
