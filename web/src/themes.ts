import { cleanupStorageKeys, createStorageKey } from "./storage";

export const themes = [
  { id: "white", name: "Branco", background: "#ffffff", layer: "#f4f4f4", text: "#161616", border: "#e0e0e0", accent: "#0f62fe" },
  { id: "g10", name: "Névoa", background: "#f4f4f4", layer: "#ffffff", text: "#161616", border: "#e0e0e0", accent: "#0f62fe" },
  { id: "g80", name: "Ardósia", background: "#393939", layer: "#525252", text: "#f4f4f4", border: "#6f6f6f", accent: "#78a9ff" },
  { id: "g90", name: "Grafite", background: "#262626", layer: "#393939", text: "#f4f4f4", border: "#525252", accent: "#78a9ff" },
  { id: "g100", name: "Carbono", background: "#161616", layer: "#262626", text: "#f4f4f4", border: "#393939", accent: "#78a9ff" },
] as const;

export type ThemeId = (typeof themes)[number]["id"];
const storageKey = createStorageKey("ui-theme", 1);

function isTheme(value: string | null): value is ThemeId {
  return themes.some((theme) => theme.id === value);
}

export function currentTheme(): ThemeId {
  const value = document.documentElement.getAttribute("theme");
  return isTheme(value) ? value : "g10";
}

export function applyTheme(theme: ThemeId): void {
  document.documentElement.setAttribute("theme", theme);
  document.documentElement.style.colorScheme = theme === "white" || theme === "g10" ? "light" : "dark";
  try {
    localStorage.setItem(storageKey, theme);
    cleanupStorageKeys(localStorage);
  } catch {
    // Theme selection remains available when browser storage is disabled.
  }
}

export function initializeTheme(): void {
  let theme: ThemeId = "g10";
  try {
    cleanupStorageKeys(localStorage);
    const saved = localStorage.getItem(storageKey);
    if (isTheme(saved)) theme = saved;
    if (saved === null) {
      // Preserve preferences written before versioned storage keys existed.
      const legacyKey = "project-conferidor.theme";
      const legacyTheme = localStorage.getItem(legacyKey);
      if (isTheme(legacyTheme)) {
        theme = legacyTheme;
        localStorage.setItem(storageKey, legacyTheme);
        localStorage.removeItem(legacyKey);
      }
    }
  } catch {
    // Use the default theme when browser storage is unavailable.
  }
  applyTheme(theme);
}
