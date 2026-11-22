// Branding and print options: what the Node.js SDK answers, for the tests of the traps that the HTML parity
// does not exercise (colour maths, the palette's rules, the hand-written print CSS).
import { createHash } from "node:crypto";

const c = String.fromCharCode;
const NBSP = c(0xa0);
const BOM = c(0xfeff);
const ARABIC_ONE = c(0x661);

export const API_DEFAULTS = {
  CompanyName: "", LogoURL: "", PrimaryColor: "#1a1a1a", SecondaryColor: "#666666",
  FooterText: "Generated using LifeAstroAPI", WatermarkText: "",
};
export const BLANK = {
  companyName: ` ${NBSP}`, logoUrl: BOM, primaryColor: `${NBSP}${BOM}`, secondaryColor: "\n", footerText: BOM,
  watermarkText: NBSP,
};
export const REPORT_BRANDING = {
  CompanyName: "Json Co", LogoURL: "https://j/l.png", PrimaryColor: "#0b5cad", SecondaryColor: "#0b6b36",
  FooterText: "Json footer", WatermarkText: "JSON",
};
export const CALLERS = [
  null,
  BLANK,
  { primaryColor: "#0b5cad", secondaryColor: "#0b6b36" },
  { primaryColor: "#0b5cad" },
  { secondaryColor: "#0b6b36" },
  { primaryColor: "#1a1a1a" },
  { secondaryColor: "#666666" },
  { primaryColor: "#1a1a1a", secondaryColor: "#666666" },
  { primaryColor: " #1A1A1A ", secondaryColor: "rgb(102,102,102)" },
  { primaryColor: "#ffe14d", secondaryColor: "white" },
  { primaryColor: "notacolour", secondaryColor: "transparent" },
  { companyName: "Acme — Astro", footerText: "© Acme — <private> \"quoted\" & 'single'" },
];
export const REPORTS = [
  undefined,
  null,
  API_DEFAULTS,
  { ...API_DEFAULTS, PrimaryColor: "#1A1A1A", SecondaryColor: " #666666 " },
  { ...API_DEFAULTS, PrimaryColor: "#0b5cad" },
  { ...API_DEFAULTS, SecondaryColor: "RebeccaPurple" },
  { CompanyName: NBSP, FooterText: BOM },
  REPORT_BRANDING,
];

export const WATERMARKS = [
  "SAMPLE", "CONFIDENTIAL", "DRAFT FOR REVIEW ONLY", "x".repeat(500), "A", "MMMMMMM", "MMMMMMMM", "a b",
  "12345678901234567890123456789012345678901234567890123456789012", // 62 units: floor(620 / 62) = 10
  "word ".repeat(40), "नमूना", "മാതൃക പകർപ്പ്", "ਨਮੂਨਾ ਕਾਪੀ",
  c(0xd83d, 0xde00).repeat(9), // 9 code points, 18 UTF-16 units
  `क${c(0x94d)}ष${c(0x93f)}`.repeat(5), // code points, not graphemes
  `${NBSP}${BOM}padded${BOM}${NBSP}`, `two${NBSP}words`, `next${c(0x85)}line`, `tab\tand\nline`,
  "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen",
];

const sha256 = (text) => createHash("sha256").update(text).digest("hex");

function random(seed) {
  let a = seed;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function colourTexts(names) {
  const next = random(20261123);
  const int = (n) => Math.floor(next() * n);
  const hex = (digits) => Array.from({ length: digits }, () => "0123456789abcdefABCDEF"[int(22)]).join("");
  const out = [
    "#0B5CAD", "#05a", "#05af", "#0b5cadff", "RebeccaPurple", "rgb(11, 92, 173)", "rgb(11 92 173)", "rgba(0,0,0,0.5)",
    "hsl(210, 100%, 50%)", "rgb(100%, 0%, 0%)", "#ffe14d", "notacolour",
    "", "  ", "constructor", "toString", "valueOf", "__proto__", "inherit", "transparent", "#0000", "rgb(1,2)", "#12345",
    "rgb(1.2.3, 4, 5)", "hsl(1,2,3)", "hsl(1%,2%,3%)", "rgb(50%, 12.5%, 0.5)", "rgb(0.5,1.5,2.5)", "rgb(300,999,.4)",
    "rgba(255,255,255,0)", "rgba(10,20,30,100%)", "rgb(1 2 3 / 50%)", "rgb(1,2,3,)", "rgb( 1 , 2 , 3 )", "rgb(1,,2,3)",
    "hsla(725, 40%, 30%, .25)", "hsl(0 0% 100%)", "hsl(359.9,100%,50%)", "hsl(210deg,1%,2%)", "RGB(1,2,3)",
    "WHITE", "Wh1te", `gr${c(0x131)}y`, `DARKKHAK${c(0x130)}`, `${c(0x212a)}HAKI`, `#${ARABIC_ONE}23456`,
    `rgb(${ARABIC_ONE},2,3)`, `${NBSP}${BOM}#05a${NBSP}`, `rgb(1${NBSP}2${c(0x3000)}3)`, `${c(0x85)}#05a`, "#05a\n",
    "#05a\nx", `rgb(${"1".repeat(56)},2,3)`, `rgb(${"1".repeat(54)},2,3)`, `${" ".repeat(70)}#05a`,
    `rgb(1,2,${" ".repeat(60)}3)`, ...names, ...names.slice(0, 12).map((name) => name.toUpperCase()),
  ];
  const component = () => [String(int(256)), `${int(1001) / 10}%`, String(int(5000) / 10), ".5"][int(4)];
  for (let i = 0; i < 600; i++) {
    out.push(`#${hex([3, 4, 6, 6, 6, 8][int(6)])}`);
    const alpha = [null, null, String(int(101) / 100), `${int(101)}%`][int(4)];
    const tail = alpha === null ? "" : `, ${alpha}`;
    out.push(
      next() < 0.5
        ? `rgb${alpha === null ? "" : "a"}(${component()}, ${component()}, ${component()}${tail})`
        : `hsl(${int(7200) / 10}, ${int(1010) / 10}%, ${int(1010) / 10}%${tail})`,
    );
  }
  return out;
}

function printOptions() {
  const out = [{}];
  for (const format of [undefined, "A3", "A4", "A5", "Letter", "Legal", "Tabloid"]) {
    for (const landscape of [undefined, true]) {
      for (const preferCSSPageSize of [undefined, true, false]) out.push({ format, landscape, preferCSSPageSize });
    }
  }
  for (const landscape of [undefined, true]) {
    for (const printBackground of [undefined, false]) {
      for (const displayHeaderFooter of [undefined, true, false]) {
        for (const footerTemplate of [undefined, "", "<b>x</b>"]) {
          out.push({ landscape, printBackground, displayHeaderFooter, footerTemplate });
        }
      }
    }
  }
  const lengths = [
    "40mm", "0", "0mm", "12", "12.5", ".5in", "1CM", "2.54cM", "7Px", " 9mm ", `${NBSP}3in${BOM}`, "007mm", "1e2",
    "-1mm", "+1mm", "1.", "10 mm", "1em", "auto", "", " ", "1mm}body{display:none", "1mm\n;x", `${ARABIC_ONE}mm`, "10MM",
    `1${c(0x131)}n`, "1.2.3mm", "16mm",
  ];
  for (const value of lengths) {
    for (const side of ["top", "right", "bottom", "left"]) out.push({ margin: { [side]: value } });
  }
  out.push(
    { margin: { top: "40mm", left: "0" } },
    { margin: { top: "1mm}body{display:none", bottom: " " } },
    { margin: { left: "4", bottom: "3", right: "2", top: "1" }, format: "A5", landscape: true, printBackground: false },
    { margin: { top: "26mm", bottom: "16mm", left: "0mm", right: "0mm" }, displayHeaderFooter: true, footerTemplate: "<p>f</p>" },
  );
  return out;
}

export function brandingVectors(node, colorsSource) {
  const { parseColor, printableColor, toHex, tintOnWhite, seeThrough, darkenToContrast, brihadPalette } = node;
  const table = /NAMED_COLORS[^{]*\{([\s\S]*?)\n\};/.exec(colorsSource)[1];
  const names = [...table.matchAll(/([a-z]+): "/g)].map((m) => m[1]);
  if (names.length < 140) throw new Error(`${names.length} colour names found in colors.ts`);
  const hexOf = (rgb) => (rgb ? toHex(rgb) : null);
  let grid = "";
  for (let r = 0; r < 256; r += 15) {
    for (let g = 0; g < 256; g += 15) {
      for (let b = 0; b < 256; b += 15) {
        const rgb = [r, g, b];
        grid +=
          toHex(darkenToContrast(rgb, 4.5)) + toHex(darkenToContrast(rgb, 5.2)) + toHex(tintOnWhite(rgb, 3)) +
          toHex(tintOnWhite(rgb, 5)) + toHex(tintOnWhite(rgb, 25)) + seeThrough(rgb) + printableColor(toHex(rgb), "none") + "\n";
      }
    }
  }
  const palettes = [];
  for (const report of REPORTS) {
    for (const caller of CALLERS) {
      const palette = brihadPalette(report, caller ?? undefined);
      palettes.push({
        ...(report === undefined ? {} : { report }),
        caller,
        palette: palette
          ? { primary: hexOf(palette.primary), accent: hexOf(palette.accent), muted: hexOf(palette.muted) }
          : null,
        header: sha256(node.brihadHeaderTemplate(report, caller ?? undefined)),
        footer: sha256(node.brihadFooterTemplate(report, caller ?? undefined)),
      });
    }
  }
  const merged = [];
  const sides = [undefined, null, "", " ", `${NBSP}${BOM}`, "0", "40mm", " 8mm "];
  for (const top of sides) {
    for (const left of sides) {
      const margin = JSON.parse(JSON.stringify({ top, left }));
      merged.push({ margin, result: node.mergeMargin(node.BRIHAD_PDF_MARGIN, margin) });
    }
  }
  merged.push({ margin: null, result: node.mergeMargin(node.BRIHAD_PDF_MARGIN, undefined) });
  return {
    colours: [...new Set(colourTexts(names))].map((value) => ({
      value,
      rgb: parseColor(value) ?? null,
      printed: printableColor(value, "#1a1a1a"),
    })),
    grid: sha256(grid),
    watermarks: WATERMARKS.map((text) => {
      const html = node.renderKundliLiteHtml({}, { watermarkText: text });
      return { text, size: Number(/body::before \{[^}]*font-size: (\d+)pt/.exec(html)[1]) };
    }),
    palettes,
    printCss: printOptions().map((options) => ({ options, css: node.printOptionsCss(options) })),
    merged,
  };
}
