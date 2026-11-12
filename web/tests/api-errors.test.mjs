import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source = ts.transpileModule(readFileSync(new URL("../src/api.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText;

function client(fetch) {
  const exports = {};
  vm.runInNewContext(source, { exports, fetch, require: () => ({ resolveApiUrl: (path) => path }) });
  return exports;
}

test("API errors display Portuguese server details verbatim and preserve technical codes", async () => {
  const problem = { title: "Conflito na operação", detail: "Um diagnóstico novo enviado pelo servidor.",
    code: "new_server_code", issues: [{ code: "new_issue", message: "Detalhe específico da importação." }] };
  const { api, ApiError } = client(async () => ({ ok: false, status: 409, text: async () => JSON.stringify(problem) }));
  await assert.rejects(api.createCompany("Empresa"), (error) => {
    assert.ok(error instanceof ApiError);
    assert.equal(error.message, problem.detail);
    assert.equal(error.status, 409);
    assert.equal(error.problem.code, problem.code);
    assert.equal(error.problem.issues[0].message, problem.issues[0].message);
    return true;
  });
});

test("title-only and non-JSON errors have readable messages without a translation table", async () => {
  for (const [body, expected] of [
    [JSON.stringify({ title: "Recurso não encontrado" }), "Recurso não encontrado"],
    ["<html>Proxy unavailable</html>", "Não foi possível concluir a operação. Confira os dados e tente novamente."],
    ["", "Não foi possível concluir a operação. Confira os dados e tente novamente."],
  ]) {
    const { api } = client(async () => ({ ok: false, status: 502, text: async () => body }));
    await assert.rejects(api.listCompanies(), (error) => error.message === expected);
  }
});

test("network failures receive a local Portuguese message without exposing transport errors", async () => {
  const { api } = client(async () => { throw new Error("fetch failed"); });
  await assert.rejects(api.listCompanies(), (error) => error.status === 0
    && error.message === "Não foi possível conectar ao Project Conferidor. Confira se a API está em execução.");
});
