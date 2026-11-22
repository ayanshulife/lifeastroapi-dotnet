// Records tests/LifeAstroApi.Tests/Resources/Pdf/ from the Node.js SDK's TypeScript sources (src/pdf/), for
// the PDF tests of the .NET SDK. Run it after `node tool/generate-pdf.mjs`. A port of the PHP SDK's
// tool/pdf-goldens.mjs, with the expected HTML kept as SHA-256 to keep the repository small.
//
//   node tool/pdf-goldens.mjs
//
// Inputs:
//   1. every render*Html call of the Node SDK's own PDF tests, recorded with a stubbed `vitest`;
//   2. the website's example report responses (examples.json), in all 10 languages, plain and branded;
//   3. the edge cases of tool/lib/pdf-edge-cases.mjs (truthiness, dates, escaping, hostile branding).
// Files:
//   cases.json     the inputs, each with the SHA-256 of Node's HTML
//   golden/*.html  Node's HTML for one plain case per renderer, for a readable difference
//   text.json      humanize / esc / cleanNarrative
//   brihad.json    running header and footer, table of contents, section tagging
//   outline.json   bookmark injection
//   wrapper.json   kundliBrihadPdf itself, run on a stand-in browser: what it prints and returns
//   lists.json     every list of one report per renderer broken (no list, a wrong or null item): Node's HTML;
//                  and what withoutNullItems returns
//   branding.json  colours, watermark sizes, the Brihad palette, print-option CSS and merged margins
//   runtime.json   String(number), toFixed, new Date, Number, the space characters
//
// Environment: LIFEASTRO_NODE_SDK, LIFEASTRO_EXAMPLES. Needs the Node SDK's esbuild (its dev dependency).
import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { BRANDING, LOCALES, edgeCases } from "./lib/pdf-edge-cases.mjs";
import { BLANK, REPORT_BRANDING, brandingVectors } from "./lib/pdf-branding-vectors.mjs";
import { runtimeVectors } from "./lib/pdf-runtime-vectors.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const NODE_SDK = resolve(process.env.LIFEASTRO_NODE_SDK ?? join(ROOT, "..", "lifeastroapi-node"));
const EXAMPLES = resolve(
  process.env.LIFEASTRO_EXAMPLES ??
    join(ROOT, "..", "LifeAstroAPI", "LifeAstroApiWebsite", "lib", "generated", "examples.json"),
);
const OUT = join(ROOT, "tests", "LifeAstroApi.Tests", "Resources", "Pdf");
const PDF = join(NODE_SDK, "src", "pdf");
const esbuild = createRequire(join(NODE_SDK, "package.json"))("esbuild");
if (!existsSync(EXAMPLES)) {
  console.error(`examples.json not found at ${EXAMPLES} (set LIFEASTRO_EXAMPLES).`);
  process.exit(2);
}

const RENDERERS = {
  KundliLite: "/v1/reports/kundli/lite",
  KundliDetailed: "/v1/reports/kundli/detailed",
  KundliBrihad: "/v1/reports/kundli/brihad",
  MatchMaking: "/v1/reports/match-making",
  DashaAnalysis: "/v1/reports/dasha-analysis",
  Numerology: "/v1/reports/numerology",
  SadeSati: "/v1/reports/sade-sati",
  MangalDosha: "/v1/reports/mangal-dosha",
  Varshaphal: "/v1/reports/varshaphal",
  Horoscope: null, // no example response: the Node tests' inputs, in every language
};

const recorder = `
import * as real from ${JSON.stringify(join(PDF, "index.ts"))};
export * from ${JSON.stringify(join(PDF, "index.ts"))};
globalThis.__pdfCalls ??= [];
const record = (name, fn) => (...args) => {
  globalThis.__pdfCalls.push({ renderer: name, args: JSON.parse(JSON.stringify(args.map((a) => a ?? null))) });
  return fn(...args);
};
${Object.keys(RENDERERS).map((n) => `export const render${n}Html = record(${JSON.stringify(n)}, real.render${n}Html);`).join("\n")}
`;
const vitest = `
const chain = new Proxy(function () {}, { get: () => chain, apply: () => chain });
const run = (fn) => { try { const r = fn?.(); if (r && typeof r.catch === "function") r.catch(() => {}); } catch {} };
export const describe = (_name, fn) => run(fn);
export const it = (_name, fn) => run(fn);
it.each = () => () => {};
export const test = it;
export const expect = () => chain;
export const vi = chain;
export const beforeAll = () => {}, afterAll = () => {}, beforeEach = () => {}, afterEach = () => {};
`;
const plugin = {
  name: "pdf-goldens",
  setup(build) {
    build.onResolve({ filter: /^vitest$/ }, () => ({ path: "vitest", namespace: "stub" }));
    build.onLoad({ filter: /.*/, namespace: "stub" }, () => ({ contents: vitest, loader: "js" }));
    build.onResolve({ filter: /src\/pdf\/index\.js$/ }, () => ({ path: "recorder", namespace: "recorder" }));
    build.onLoad({ filter: /.*/, namespace: "recorder" }, () => ({ contents: recorder, loader: "js", resolveDir: PDF }));
  },
};

const temp = mkdtempSync(join(tmpdir(), "lifeastro-pdf-goldens-"));
process.on("exit", () => rmSync(temp, { recursive: true, force: true }));
// The Node tests' htmlToPdf cases reject without Puppeteer; only their render calls matter here.
process.on("unhandledRejection", () => {});
async function bundle(entry, name) {
  const outfile = join(temp, name);
  await esbuild.build({
    entryPoints: [entry],
    bundle: true,
    format: "esm",
    platform: "node",
    outfile,
    plugins: [plugin],
    logLevel: "error",
  });
  return import(pathToFileURL(outfile).href);
}

for (const test of ["pdf.test.ts", "pdf-south-locales.test.ts", "pdf-branding-options.test.ts"]) {
  await bundle(join(NODE_SDK, "test", test), test.replace(/\.ts$/, ".mjs"));
}
await new Promise((done) => setTimeout(done, 50)); // async test bodies
const recorded = globalThis.__pdfCalls ?? [];
const entry = join(temp, "helpers.ts");
writeFileSync(
  entry,
  `export * from ${JSON.stringify(join(PDF, "index.ts"))};
export {
  brihadHeaderTemplate, brihadFooterTemplate, brihadPalette, buildBrihadToc, tagBrihadSectionsForToc, BRIHAD_PDF_MARGIN,
} from ${JSON.stringify(join(PDF, "templates", "kundliBrihadSections.ts"))};
export * from ${JSON.stringify(join(PDF, "colors.ts"))};
export { printOptionsCss, mergeMargin } from ${JSON.stringify(join(PDF, "render.ts"))};
export { withoutNullItems } from ${JSON.stringify(join(PDF, "shared.ts"))};
export { TOC_HEADING } from ${JSON.stringify(join(PDF, "locales.ts"))};
`,
);
const node = await bundle(entry, "helpers.mjs");

// ---- cases.json ----

const inputs = [];
const inputIndex = new Map();
const cases = [];
const seen = new Set();
const htmlOf = new Map();
const counters = {};
const sha256 = (text) => createHash("sha256").update(text).digest("hex");
const copy = (value) => JSON.parse(JSON.stringify(value));

/** `locale` / `language`: the input is stored once and the language is set on it. */
function addCase(renderer, data, branding = null, set = null) {
  const given = copy(data);
  const key = JSON.stringify([renderer, given, branding, set]);
  if (seen.has(key)) return;
  seen.add(key);
  const json = JSON.stringify(given);
  if (!inputIndex.has(json)) {
    inputIndex.set(json, inputs.length);
    inputs.push(given);
  }
  const input = copy(given);
  if (set) input[set.key] = set.value;
  const html = node[`render${renderer}Html`](input, branding ? copy(branding) : undefined);
  counters[renderer] = (counters[renderer] ?? 0) + 1;
  const id = `${renderer}-${String(counters[renderer]).padStart(3, "0")}`;
  cases.push({ id, renderer, input: inputIndex.get(json), set, branding, sha256: sha256(html) });
  htmlOf.set(id, html);
}

for (const call of recorded) addCase(call.renderer, call.args[0], call.args[1] ?? null);
for (const call of recorded.filter((c) => c.renderer === "Horoscope")) {
  for (const value of LOCALES) addCase("Horoscope", call.args[0], call.args[1] ?? null, { key: "language", value });
}
const examples = JSON.parse(readFileSync(EXAMPLES, "utf8"));
for (const [renderer, path] of Object.entries(RENDERERS)) {
  if (!path) continue;
  const response = examples[path]?.response;
  if (!response || typeof response !== "object") throw new Error(`no example response for ${path}`);
  for (const value of LOCALES) addCase(renderer, response, null, { key: "Locale", value });
  addCase(renderer, response, BRANDING);
  addCase(renderer, response, BRANDING, { key: "Locale", value: "pa" });
}
for (const edge of edgeCases()) addCase(edge.renderer, edge.data, edge.branding ?? null);

rmSync(OUT, { recursive: true, force: true });
mkdirSync(join(OUT, "golden"), { recursive: true });
const write = (name, value) => writeFileSync(join(OUT, name), `${JSON.stringify(value)}\n`);
const goldens = [];
for (const renderer of Object.keys(RENDERERS)) {
  const plain = cases.filter((c) => c.renderer === renderer && !c.branding && (!c.set || c.set.value === "pa"));
  const chosen = plain.find((c) => c.set?.value === "pa") ?? plain[0];
  writeFileSync(join(OUT, "golden", `${chosen.id}.html`), htmlOf.get(chosen.id));
  goldens.push(chosen.id);
}
write("cases.json", { goldens, inputs, cases });

// ---- lists.json ----

/**
 * What happens to every list, as pdf_work/run/fuzz_lists.mjs, fuzz_shapes.mjs and fuzz_nulls.mjs do it:
 * replace = the value takes the list's place, push = it is one more item, fill = its items are the only ones.
 */
const EDITS = [
  ...["1, 5", { a: 1 }, 7].map((value) => ({ how: "replace", value })),
  ...[null, "x", 7].map((value) => ({ how: "push", value })),
  ...[[null], ["x", null], [7, null], [[null]]].map((value) => ({ how: "fill", value })),
];
const lists = [];
for (const [renderer, path] of Object.entries(RENDERERS)) {
  const calls = recorded.filter((call) => call.renderer === renderer).map((call) => JSON.stringify(call.args[0]));
  const json = path ? JSON.stringify(examples[path].response) : calls.reduce((a, b) => (b.length > a.length ? b : a));
  const input = inputIndex.get(json);
  if (input === undefined) throw new Error(`${renderer}: its report is not among the inputs`);
  for (const value of ["en", "pa"]) {
    const base = JSON.parse(json);
    const set = { key: renderer === "Horoscope" ? "language" : "Locale", value };
    base[set.key] = value;
    const paths = new Map(); // one path for each shape of list
    const walk = (o, at, shape) => {
      if (Array.isArray(o)) {
        if (at.length && !paths.has(shape)) paths.set(shape, at);
        o.forEach((v, i) => walk(v, [...at, i], `${shape}[]`));
      } else if (o && typeof o === "object") {
        for (const k of Object.keys(o)) walk(o[k], [...at, k], `${shape}.${k}`);
      }
    };
    walk(base, [], "");
    for (const at of paths.values()) {
      const html = EDITS.map(({ how, value }) => {
        const broken = copy(base);
        let o = broken;
        for (const k of at.slice(0, -1)) o = o[k];
        const last = at[at.length - 1];
        o[last] = how === "push" ? [...o[last], value] : copy(value);
        return sha256(node[`render${renderer}Html`](broken)); // a throw here stops the recorder
      });
      lists.push({ renderer, input, set, path: at, sha256: html });
    }
  }
}
const nested = (levels, inner) => {
  let value = inner;
  for (let i = 0; i < levels; i++) value = i % 2 ? [value] : { next: value };
  return value;
};
const withNulls = [
  null, 5, "x", true, [], {}, [null], [null, null], ["x", null], [null, 7, null], [false, null], [0, "", null],
  [{ a: 1 }, null], [[1], null], [[null]], [[null], null], [null, "x", { a: 1 }], { a: null }, { a: [1, 2], b: {} },
  { b: 1, 10: "ten", 2: [null, "two"], a: [null, {}], "-1": [] }, { keep: [1], change: { deep: [{ list: [3, null] }] } },
  ...[0, 30, 31, 32, 33, 34].flatMap((levels) => [nested(levels, [1, null]), nested(levels, [{}, null])]),
];
write("lists.json", {
  edits: EDITS,
  lists,
  withoutNullItems: withNulls.map((value) => {
    const given = copy(value);
    const result = node.withoutNullItems(given);
    if (JSON.stringify(given) !== JSON.stringify(value)) throw new Error("withoutNullItems edited its argument");
    return { value, result, same: result === given };
  }),
});

// ---- text.json ----

const texts = new Set([
  "A — B", "x – y", "a -> b => c", "—", " – ", "word —", "— word", "a , , b", "(, x ,)", "[ , x , ]", "a, ।", "‒ ⟶ ⇒ →",
  "In Vedic astrology the chart is read on three levels: birth, dasha and transit. It is about guidance.",
  "the native's career; The native is calm; native's luck; native; natives; _native; native9",
  "वैदिक ज्योतिष में फल तीन स्तरों पर देखा जाता है। यह शास्त्र है। जातक की कुंडली।",
  "जातक की कुंडली में जातक को लाभ। जातक में जातक पर जातक स्वयं जातक",
  "जातकाच्या जीवनात जातकाला यश जातक",
  "सुनिए। जन्म, दशा और गोचर, इन तीन स्तरों पर फल देखा जाता है। आगे।",
  "x\n\n\n\ny  \t\nz",
  "three levels of reading. The native waits.",
  "In Vedic astrology there are three  levels, about guidance.",
  "सुनिए। इन तीन स्तरों पर देखते हैं। आगे।",
  "सुनिए। इन तीनस्तरों पर देखते हैं। आगे।",
  ...[0x00a0, 0x0085, 0xfeff, 0x180e, 0x2028, 0x3000, 0x0009].flatMap((code) => {
    const c = String.fromCharCode(code);
    return [`${c}padded${c}`, `a${c},b`, `a${c}—${c}b`, `the${c}native`];
  }),
  "अ—ब ਫ਼ੈਸਲਾ → ज़रूरी",
  "😀—😀",
  "éthe nativeé",
]);
const collect = (value) => {
  if (typeof value === "string") texts.add(value);
  else if (Array.isArray(value)) value.forEach(collect);
  else if (value && typeof value === "object") Object.values(value).forEach(collect);
};
for (const input of inputs) collect(input);
// Everything that a helper changes, and a sample of what it leaves alone.
const rows = [];
let unchanged = 0;
for (const s of [...texts].sort()) {
  const row = { s, humanize: node.humanize(s), esc: node.esc(s), clean: {} };
  for (const locale of ["en", "hi", "mr", "te", "pa"]) row.clean[locale] = node.cleanNarrative(s, locale);
  const same = row.humanize === s && row.esc === s && Object.values(row.clean).every((c) => c === s);
  if (same && ++unchanged % 25 !== 0) continue;
  rows.push(row);
}
write("text.json", rows);

// ---- brihad.json ----

const raw = { CompanyName: "Acme & Co", FooterText: "Acme Astro · confidential" };
const tocEntries = [
  { title: "Part 1: Birth Details", page: 3 },
  { title: "Planets & <Houses>", page: 7 },
  { title: "भाग 2: दशा", page: 12 },
  { title: "ਭਾਗ 3: ਉਪਾਅ", page: 140 },
];
write("brihad.json", {
  templates: [
    [null, null],
    [null, BRANDING],
    [raw, null],
    [raw, BRANDING],
    [raw, { footerText: "Only the footer" }],
  ].map(([report, caller]) => ({
    report,
    caller,
    header: node.brihadHeaderTemplate(report ?? undefined, caller ?? undefined),
    footer: node.brihadFooterTemplate(report ?? undefined, caller ?? undefined),
  })),
  toc: LOCALES.map((locale) => {
    const heading = node.TOC_HEADING[locale] ?? node.TOC_HEADING.en;
    return { locale, heading, entries: tocEntries, html: node.buildBrihadToc(tocEntries, heading) };
  }),
  tagged: cases
    .filter((c) => c.renderer === "KundliBrihad")
    .map((c) => {
      const { html, titles } = node.tagBrihadSectionsForToc(htmlOf.get(c.id));
      return { id: c.id, titles, sha256: sha256(html) };
    }),
});

// ---- outline.json ----

/** A minimal cross-reference-table PDF. */
function smallPdf(pages, { info = null, catalog = "" } = {}) {
  const kids = Array.from({ length: pages }, (_, i) => `${3 + i} 0 R`).join(" ");
  const objects = [
    `<< /Type /Catalog /Pages 2 0 R /PageMode /UseNone${catalog} >>`,
    `<< /Type /Pages /Kids [${kids}] /Count ${pages} >>`,
    ...Array.from({ length: pages }, () => "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"),
  ];
  if (info) objects.push(`<< /Title (${info}) >>`);
  let text = "%PDF-1.4\n";
  const offsets = objects.map((body, i) => {
    const at = text.length;
    text += `${i + 1} 0 obj\n${body}\nendobj\n`;
    return at;
  });
  const xref = text.length;
  text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const at of offsets) text += `${String(at).padStart(10, "0")} 00000 n \n`;
  const infoRef = info ? ` /Info ${objects.length} 0 R` : "";
  text += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R${infoRef} >>\nstartxref\n${xref}\n%%EOF\n`;
  return new Uint8Array(Buffer.from(text, "latin1"));
}
const outlines = [
  { pages: 1, items: [{ title: "Only", page: 1, level: 1 }] },
  {
    pages: 5,
    items: [
      { title: "भाग 1: जन्म", page: 1, level: 1 },
      { title: "Planets (D1)", page: 2, level: 2 },
      { title: "back\\slash", page: 2, level: 3 },
      { title: "ਫ਼ੈਸਲਾ 😀", page: 3, level: 2 },
      { title: "   ", page: 3, level: 1 },
      { title: "തെലുങ്ക് తెలుగు", page: 99, level: 1 },
      { title: "below the top", page: 0, level: 0 },
      { title: "after it", page: 4, level: 1 },
    ],
  },
  { pages: 3, items: [{ title: "deep first", page: 2, level: 4 }, { title: "then top", page: 3, level: 1 }] },
  {
    pages: 3,
    info: "Brihad Kundli",
    items: [
      { title: "ഭാഗം 1: അടിസ്ഥാനങ്ങൾ", page: 1, level: 1 },
      { title: "ഊ ലഗ്നം ਕੁੰਡਲੀ ⠨ ⠩", page: 2, level: 2 }, // U+0D0A is CR LF; U+2828, U+2829 hold ( and )
    ],
  },
  {
    pages: 2,
    info: "x",
    // Bytes 0x80 to 0x9F are where windows-1252 and ISO 8859-1 differ.
    catalog: ` /Lang (${Array.from({ length: 128 }, (_, i) => String.fromCharCode(0x80 + i)).join("")})`,
    items: [{ title: "Binary catalog", page: 2, level: 1 }],
  },
];
write(
  "outline.json",
  outlines.map(({ pages, items, info, catalog }) => {
    const pdf = smallPdf(pages, { info, catalog });
    const expected = node.addPdfOutline(pdf, copy(items));
    return { pages, items, pdf: Buffer.from(pdf).toString("base64"), expected: Buffer.from(expected).toString("base64") };
  }),
);

// ---- wrapper.json ----

// kundliBrihadPdf with a browser that prints nothing: without pdfjs it asks evaluate() for the page of
// each section, and gets 3 + 2i, or two sections to a page when `shared`.
const wrapperPdf = smallPdf(160, { info: "Brihad Kundli" });
async function runWrapper(id, branding, given = null, shared = false) {
  const c = cases.find((x) => x.id === id);
  const data = copy(inputs[c.input]);
  if (c.set) data[c.set.key] = c.set.value;
  const baseHtml = node.renderKundliBrihadHtml(copy(data), branding ?? undefined);
  const { titles } = node.tagBrihadSectionsForToc(baseHtml);
  const pages = titles.map((_, i) => 3 + 2 * (shared ? Math.floor(i / 2) : i));
  const printed = [];
  let loaded = null;
  const page = {
    setContent: async (html) => void (loaded = html),
    evaluate: async () => titles.map((title, i) => ({ title, page: pages[i] })),
    pdf: async (options) => {
      printed.push({ html: loaded, options });
      return wrapperPdf.slice();
    },
    close: async () => {},
  };
  const browser = { newPage: async () => page, close: async () => {} };
  const pdf = await node.kundliBrihadPdf(copy(data), { ...(given ?? {}), branding: branding ?? undefined, browser });
  if (printed.length !== 2) throw new Error(`${id}: ${printed.length} prints, expected 2`);
  return {
    id,
    branding,
    given,
    pages,
    html: printed.map((print) => sha256(print.html)),
    options: printed[1].options,
    pdf: sha256(Buffer.from(pdf)),
  };
}
const brihadIn = (locale) => cases.find((c) => c.renderer === "KundliBrihad" && !c.branding && c.set?.value === locale).id;
const protoLocale = cases.find((c) => c.renderer === "KundliBrihad" && inputs[c.input].Locale === "toString").id;
const ownBranding = cases.find(
  (c) => c.renderer === "KundliBrihad" && inputs[c.input].Branding?.CompanyName === "From the report",
).id;
const ownColours = cases.find(
  (c) => c.renderer === "KundliBrihad" && inputs[c.input].Branding?.CompanyName === REPORT_BRANDING.CompanyName,
).id;
const runs = [];
for (const [id, branding, given, shared] of [
  [brihadIn("ml"), null],
  [brihadIn("hi"), BRANDING],
  [brihadIn("pa"), { footerText: "Only the footer" }],
  [brihadIn("ta"), { companyName: "Acme & Co <Pune>" }],
  [ownBranding, null],
  [ownBranding, { companyName: "Override" }],
  [protoLocale, null],
  [ownColours, null],
  [ownColours, BLANK],
  [ownColours, { primaryColor: "#ffe14d" }],
  [brihadIn("en"), { primaryColor: "#1a1a1a" }],
  [brihadIn("en"), { primaryColor: "#1a1a1a", secondaryColor: "#666666" }],
  [brihadIn("en"), null, { margin: { top: "40mm", bottom: " " }, format: "A5",
    headerTemplate: "<p>my header</p>", footerTemplate: "<p>my footer</p>" }],
  [brihadIn("bn"), null, { margin: { right: "0", left: "5" }, landscape: true, printBackground: false }],
  [brihadIn("te"), null, { displayHeaderFooter: false }],
  [brihadIn("te"), null, { displayHeaderFooter: false, landscape: true, format: "Letter", footerTemplate: "<i>f</i>" }],
  [brihadIn("kn"), null, { displayHeaderFooter: true, headerTemplate: "", footerTemplate: "" }],
  [brihadIn("gu"), null, { preferCSSPageSize: true, format: "A3", scale: 0.9, timeoutMs: 600000 }],
  [brihadIn("mr"), BRANDING, null, true],
]) {
  runs.push(await runWrapper(id, branding, given, shared));
}
write("wrapper.json", { pdf: Buffer.from(wrapperPdf).toString("base64"), runs });

write("branding.json", brandingVectors(node, readFileSync(join(PDF, "colors.ts"), "utf8")));
write("runtime.json", runtimeVectors());

console.log(`${cases.length} cases from ${inputs.length} inputs (${recorded.length} calls of the Node tests).`);
console.log(counters);
console.log(`${rows.length} text rows.`);
