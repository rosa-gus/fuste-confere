const appNamespace = "conferidor";

/**
 * Builds a key using `app:version:key`. The version describes this key's
 * stored data format, independently of the product release and other keys.
 * Increment it when the stored format becomes incompatible.
 *
 * @example
 * const key = createStorageKey("ui-theme", 1); // conferidor:v1:ui-theme
 * localStorage.setItem(key, "g10");
 * cleanupStorageKeys(localStorage);
 * const theme = localStorage.getItem(key);
 *
 * Use the same helpers with sessionStorage. Call cleanupStorageKeys at
 * startup and after writing a preference. Callers handle unavailable storage
 * and explicitly migrate data when changing formats.
 */
export function createStorageKey(key: string, version: number): string {
  if (!key || key.trim() !== key || key.includes(":")) {
    throw new Error("Storage key must be non-empty, trimmed, and contain no colons.");
  }
  if (!Number.isSafeInteger(version) || version < 1) {
    throw new Error("Storage key version must be a positive safe integer.");
  }
  return `${appNamespace}:v${version}:${key}`;
}

/**
 * Checks the storage inventory and removes obsolete versions only when found.
 * For example, v2:ui-theme replaces v1:ui-theme, but not v1:another-key.
 * Foreign namespaces and malformed keys are ignored. Storage has no prefix
 * query, so detecting obsolete versions requires enumerating its keys.
 *
 * Keeps the highest version actually present, including versions newer than
 * this application knows. Cleanup never converts or reads stored values.
 * Returns the number of keys removed. Storage errors propagate to the caller.
 */
export function cleanupStorageKeys(storage: Storage): number {
  const entries: { key: string; name: string; version: number }[] = [];
  const latest = new Map<string, number>();
  const prefix = `${appNamespace}:`;

  for (let index = 0; index < storage.length; index++) {
    const key = storage.key(index);
    if (!key?.startsWith(prefix)) continue;
    const match = /^v([1-9]\d*):([^:]+)$/.exec(key.slice(prefix.length));
    if (!match) continue;
    const version = Number(match[1]);
    const name = match[2];
    if (!Number.isSafeInteger(version) || name.trim() !== name) continue;
    entries.push({ key, name, version });
    latest.set(name, Math.max(latest.get(name) ?? 0, version));
  }

  // Collect before deleting: removing entries changes Storage's key indexes.
  const outdated = entries.filter((entry) => entry.version < latest.get(entry.name)!);
  for (const entry of outdated) storage.removeItem(entry.key);
  return outdated.length;
}
