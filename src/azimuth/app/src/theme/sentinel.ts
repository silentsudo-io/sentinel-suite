/**
 * Sentinel design-system palette — EXACT tokens, never approximate.
 * Source of truth: bin\Custom\Docs\SENTINEL_DESIGN_SYSTEM.md §1.
 *
 * THE ONE RULE
 *   cyan (`accent`) is the ONLY accent and it means LIVE / watching / active.
 *   green/red (`up`/`down`) are reserved for MONEY + DIRECTION (P&L, long/short).
 *   amber (`warn`) is caution/advisory. Everything else is a blue-biased neutral.
 *
 * If you are reaching for a second accent colour, stop — restraint IS the identity.
 */

export interface SentinelPalette {
  void: string;
  panel: string;
  card2: string;
  line: string;
  dim: string;
  faint: string;
  ink: string;
  ink2: string;
  mute: string;
  accent: string;
  up: string;
  down: string;
  warn: string;
}

/** Dark — the default navy flight-deck theme. */
export const DARK: SentinelPalette = {
  void:   '#0A0E17',
  panel:  '#111726',
  card2:  '#0E1420',
  line:   '#1E2A3D',
  dim:    '#1B2536',
  faint:  '#26344C',
  ink:    '#E9EEF7',
  ink2:   '#AEBACE',
  mute:   '#6C7A92',
  accent: '#3FD1E0',
  up:     '#25D08B',
  down:   '#FF5C6A',
  warn:   '#F2B34C',
};

/** Obsidian — true-black OLED. Ground ramp is near-neutral; ink is lifted. */
export const OBSIDIAN: SentinelPalette = {
  ...DARK,
  void:  '#000000',
  panel: '#0B0B0D',
  card2: '#080809',
  line:  '#1C1C21',
  dim:   '#141418',
  faint: '#242429',
  ink:   '#F2F5FA',
};

export const THEMES = { dark: DARK, obsidian: OBSIDIAN } as const;
export type ThemeName = keyof typeof THEMES;

// ── colour helpers ────────────────────────────────────────────────────────────

export function hexToRgb(hex: string): [number, number, number] {
  const h = hex.replace('#', '');
  return [
    parseInt(h.slice(0, 2), 16),
    parseInt(h.slice(2, 4), 16),
    parseInt(h.slice(4, 6), 16),
  ];
}

/** rgba() string for a token at alpha `a` (0..1). */
export function alpha(hex: string, a: number): string {
  const [r, g, b] = hexToRgb(hex);
  return `rgba(${r},${g},${b},${a})`;
}

/**
 * `Tint(accent, k)` from the design system — a linear blend from `void` toward
 * the colour by k. Solid, so it reads on every skin. Typical k: chip .10,
 * pill .12–.16, armed/selected .22, big button .30.
 */
export function tint(pal: SentinelPalette, hex: string, k: number): string {
  const [r0, g0, b0] = hexToRgb(pal.void);
  const [r1, g1, b1] = hexToRgb(hex);
  const m = (a: number, b: number) => Math.round(a + (b - a) * k);
  return `rgb(${m(r0, r1)},${m(g0, g1)},${m(b0, b1)})`;
}

/** Publish the palette as CSS custom properties on :root. */
export function applyTheme(pal: SentinelPalette): void {
  const r = document.documentElement.style;
  for (const [k, v] of Object.entries(pal)) r.setProperty(`--s-${k}`, v);
  r.setProperty('--s-accent-08', alpha(pal.accent, 0.08));
  r.setProperty('--s-accent-16', alpha(pal.accent, 0.16));
  r.setProperty('--s-accent-28', alpha(pal.accent, 28 / 255));
  r.setProperty('--s-accent-120', alpha(pal.accent, 120 / 255));
  r.setProperty('--s-tint-chip', tint(pal, pal.accent, 0.1));
  r.setProperty('--s-tint-pill', tint(pal, pal.accent, 0.16));
  r.setProperty('--s-tint-sel', tint(pal, pal.accent, 0.22));
  r.setProperty('--s-tint-btn', tint(pal, pal.accent, 0.3));
}

/** Typography — both ship with Windows, no webfont dependency (§2). */
export const FONT_SANS = "'Segoe UI', system-ui, sans-serif";
export const FONT_MONO = "Consolas, 'Courier New', monospace";
