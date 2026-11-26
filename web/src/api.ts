import { resolveApiUrl } from "./api-settings";
import type {
  Agreement,
  Company,
  ImportConfiguration,
  ImportPreview,
  ImportProfile,
  ProblemDetails,
  Reconciliation,
} from "./types";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly problem?: ProblemDetails,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function readJson(response: Response): Promise<unknown> {
  const body = await response.text();
  try {
    return body ? JSON.parse(body) : undefined;
  } catch {
    return undefined;
  }
}

async function requestResponse(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(resolveApiUrl(path), { ...init, credentials: "omit" });
  } catch {
    throw new ApiError("Não foi possível conectar ao Fuste. Confira se a API está em execução.", 0);
  }

  if (!response.ok) {
    const problem = await readJson(response) as ProblemDetails | undefined;
    throw new ApiError(
      problem?.detail ?? problem?.title ?? "Não foi possível concluir a operação. Confira os dados e tente novamente.",
      response.status,
      problem,
    );
  }

  return response;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  return await readJson(await requestResponse(path, init)) as T;
}

export const api = {
  health: () => request<{ status: string }>("/health", { cache: "no-store" }),

  resetDatabase: () => request<void>("/api/database/reset", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ confirmation: "RESET" }),
  }),

  listCompanies: () => request<Company[]>("/api/companies"),

  deleteCompany: (companyId: string) =>
    request<void>(`/api/companies/${companyId}`, { method: "DELETE" }),

  createCompany: (name: string) =>
    request<Company>("/api/companies", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    }),

  listAgreements: (companyId: string) =>
    request<Agreement[]>(`/api/companies/${companyId}/agreements`),

  deleteAgreement: (companyId: string, agreementId: string) =>
    request<void>(`/api/companies/${companyId}/agreements/${agreementId}`, { method: "DELETE" }),

  createAgreement: (
    companyId: string,
    effectiveFrom: string,
    percentage: number,
  ) =>
    request<Agreement>(`/api/companies/${companyId}/agreements`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ effectiveFrom, percentage }),
    }),

  listReconciliations: (companyId: string) =>
    request<Reconciliation[]>(`/api/companies/${companyId}/reconciliations`),

  deleteReconciliation: (companyId: string, reconciliationId: string) =>
    request<void>(`/api/companies/${companyId}/reconciliations/${reconciliationId}`, { method: "DELETE" }),

  getReconciliation: (id: string) =>
    request<Reconciliation>(`/api/reconciliations/${id}`),

  exportReconciliation: async (id: string): Promise<Blob> => {
    const response = await requestResponse(`/api/reconciliations/${id}/export`, { cache: "no-store" });
    if (response.headers.get("Content-Type")?.split(";")[0].trim().toLowerCase() !== "text/csv") {
      throw new ApiError("A API não retornou um arquivo CSV. Tente novamente.", response.status);
    }
    return response.blob();
  },

  listImportProfiles: (companyId: string) =>
    request<ImportProfile[]>(`/api/companies/${companyId}/import-profiles`),

  createImportProfile: (companyId: string, name: string, configuration: ImportConfiguration) =>
    request<ImportProfile>(`/api/companies/${companyId}/import-profiles`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, configuration }),
    }),

  updateImportProfile: (companyId: string, profileId: string, name: string, configuration: ImportConfiguration, revision: number) =>
    request<ImportProfile>(`/api/companies/${companyId}/import-profiles/${profileId}`, {
      method: "PUT", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, configuration, revision }),
    }),

  deleteImportProfile: (companyId: string, profileId: string) =>
    request<void>(`/api/companies/${companyId}/import-profiles/${profileId}`, { method: "DELETE" }),

  previewImport: (companyId: string, sales: File, statement: File, configuration?: ImportConfiguration | null) => {
    const form = new FormData();
    form.append("sales", sales);
    form.append("statement", statement);
    if (configuration) form.append("configuration", JSON.stringify(configuration));
    return request<ImportPreview>(`/api/companies/${companyId}/imports/preview`, { method: "POST", body: form });
  },

  createReconciliation: (companyId: string, sales: File, statement: File, configuration?: ImportConfiguration | null,
    attempt?: { requestId: string; reprocessOf?: string }) => {
    const form = new FormData();
    form.append("sales", sales);
    form.append("statement", statement);
    if (configuration) form.append("configuration", JSON.stringify(configuration));
    if (attempt) {
      form.append("requestId", attempt.requestId);
      if (attempt.reprocessOf) form.append("reprocessOf", attempt.reprocessOf);
    }
    return request<Reconciliation>(
      `/api/companies/${companyId}/reconciliations`,
      {
        method: "POST",
        body: form,
      },
    );
  },
};
