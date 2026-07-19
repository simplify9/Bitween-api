/**
 * Derives the full crimson-50..900 CSS-variable ramp from a single picked
 * color (treated as the "600" shade, which is what buttons/links/active nav
 * are built from). Lightness and saturation targets below were measured off
 * the shipped ramp in index.css so a default-color round trip reproduces it.
 */
export const COLOR_SCALE_STEPS = ["50", "100", "200", "300", "400", "500", "600", "700", "800", "900"] as const;
export type ColorScaleStep = (typeof COLOR_SCALE_STEPS)[number];

const L_TARGETS: Record<ColorScaleStep, number> = {
  "50": 97,
  "100": 94,
  "200": 89,
  "300": 82,
  "400": 71,
  "500": 60,
  "600": 50,
  "700": 42,
  "800": 35,
  "900": 31,
};
const S_TARGETS: Record<ColorScaleStep, number> = {
  "50": 86,
  "100": 100,
  "200": 100,
  "300": 100,
  "400": 97,
  "500": 91,
  "600": 78,
  "700": 79,
  "800": 76,
  "900": 68,
};

const clamp = (n: number, min: number, max: number) => Math.min(max, Math.max(min, n));

export function hexToHsl(hex: string): [h: number, s: number, l: number] {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  let h = 0;
  let s = 0;
  const l = (max + min) / 2;
  const d = max - min;
  if (d !== 0) {
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    switch (max) {
      case r:
        h = (g - b) / d + (g < b ? 6 : 0);
        break;
      case g:
        h = (b - r) / d + 2;
        break;
      default:
        h = (r - g) / d + 4;
    }
    h /= 6;
  }
  return [Math.round(h * 360), Math.round(s * 100), Math.round(l * 100)];
}

export function hslToHex(h: number, s: number, l: number): string {
  const sN = s / 100;
  const lN = l / 100;
  const k = (n: number) => (n + h / 30) % 12;
  const a = sN * Math.min(lN, 1 - lN);
  const f = (n: number) => lN - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  const toHex = (n: number) =>
    Math.round(f(n) * 255)
      .toString(16)
      .padStart(2, "0");
  return `#${toHex(0)}${toHex(8)}${toHex(4)}`;
}

/** `hex` is treated as the 600 shade; other steps are derived around it. */
export function buildColorScale(hex: string): Record<ColorScaleStep, string> {
  const [h, s] = hexToHsl(hex);
  const satScale = s / S_TARGETS["600"];
  const result = {} as Record<ColorScaleStep, string>;
  for (const step of COLOR_SCALE_STEPS) {
    if (step === "600") {
      result[step] = hex;
      continue;
    }
    const sat = clamp(Math.round(S_TARGETS[step] * satScale), 0, 100);
    result[step] = hslToHex(h, sat, L_TARGETS[step]);
  }
  return result;
}

export function applyColorScale(hex: string) {
  const scale = buildColorScale(hex);
  const root = document.documentElement.style;
  for (const step of COLOR_SCALE_STEPS) {
    root.setProperty(`--color-crimson-${step}`, scale[step]);
  }
}

export function resetColorScale() {
  const root = document.documentElement.style;
  for (const step of COLOR_SCALE_STEPS) {
    root.removeProperty(`--color-crimson-${step}`);
  }
}
