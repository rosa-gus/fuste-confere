import { cleanupStorageKeys, createStorageKey } from "./storage";

export interface ApiSettings {
  useLocal: boolean;
  url: string;
}

const storageKey = createStorageKey("api-connection", 1);
const defaults: ApiSettings = { useLocal: true, url: "" };

/** Accept an HTTP(S) base URL, optionally including a deployment path prefix. */
export function normalizeApiUrl(value: string): string {
  const url = new URL(value.trim());
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
    throw new Error("API URL must use HTTP or HTTPS without credentials, query parameters, or a fragment.");
  }
  return url.href.replace(/\/+$/, "");
}

export function getApiSettings(): ApiSettings {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return { ...defaults };
    const value: unknown = JSON.parse(raw);
    if (typeof value !== "object" || value === null) return { ...defaults };
    const settings = value as Partial<ApiSettings>;
    if (typeof settings.useLocal !== "boolean" || typeof settings.url !== "string") return { ...defaults };
    if (!settings.useLocal && !settings.url) return { ...defaults };
    return { useLocal: settings.useLocal, url: settings.url ? normalizeApiUrl(settings.url) : "" };
  } catch {
    return { ...defaults };
  }
}

/** Persist before reloading the UI so data from different APIs cannot be mixed. */
export function saveApiSettings(settings: ApiSettings): void {
  const normalized = {
    useLocal: settings.useLocal,
    url: settings.useLocal ? getApiSettings().url : normalizeApiUrl(settings.url),
  };
  if (!normalized.useLocal && !normalized.url) throw new Error("API URL is required.");
  localStorage.setItem(storageKey, JSON.stringify(normalized));
  cleanupStorageKeys(localStorage);
}

export function resolveApiUrl(path: string): string {
  const settings = getApiSettings();
  return settings.useLocal ? path : `${settings.url}${path}`;
}

/** Describe the saved connection, never an unsaved draft from the options form. */
export function getApiBaseUrl(): string {
  const settings = getApiSettings();
  return settings.useLocal ? window.location.origin : settings.url;
}
