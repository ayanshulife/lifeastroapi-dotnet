// Code generator for the .NET SDK.
//
// Reads the endpoint manifest of the Node.js SDK (scripts/endpoints_part1.json +
// endpoints_part2.json, resolved by scripts/manifest.mjs: per-path overrides, the `lang` rules,
// namespace and method names) and writes:
//
//   src/LifeAstroApi/Generated/Params/*Params.g.cs          one parameter class per method
//   src/LifeAstroApi/Generated/Resources/*Resource.g.cs     one class per resource group
//   src/LifeAstroApi/Generated/LifeAstroClient.Resources.g.cs  the group properties of the client
//   tests/Shared/Samples.g.cs                                 a sample call of every method (tests, live sweep)
//   tests/LifeAstroApi.Tests/Resources/manifest.json          the resolved manifest with the C# names
//
// and tool/param-docs.json (parameter docs from the website's examples.json, kept so the generator
// also runs without the website checkout).
//
// Importing manifest.mjs, rather than copying its rules, keeps the Node, Python, Dart, PHP, Java and
// .NET SDKs on the same endpoint list, method names and `lang` rules. This file is a port of
// lifeastroapi-java/tool/generate.mjs: the same parameter model, conveniences and samples.
//
//   node tool/generate.mjs
//   node tool/generate.mjs --costs-url http://127.0.0.1:18080   # refresh tool/costs.json first
//
// Credit costs in the XML docs come from tool/costs.json, a snapshot of GET /v1/meta/endpoints.
//
// Environment:
//   LIFEASTRO_NODE_SDK   path to the Node SDK repo (default ../lifeastroapi-node)
//   LIFEASTRO_EXAMPLES   path to the website's examples.json (default
//                        ../LifeAstroAPI/LifeAstroApiWebsite/lib/generated/examples.json)
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const NODE_SDK = resolve(process.env.LIFEASTRO_NODE_SDK ?? join(ROOT, "..", "lifeastroapi-node"));
const EXAMPLES = resolve(
  process.env.LIFEASTRO_EXAMPLES ??
    join(ROOT, "..", "LifeAstroAPI", "LifeAstroApiWebsite", "lib", "generated", "examples.json"),
);
const LIB = join(ROOT, "src", "LifeAstroApi");
const GEN = join(LIB, "Generated");
const PARAMS_DIR = join(GEN, "Params");
const RES_DIR = join(GEN, "Resources");
const SHARED = join(ROOT, "tests", "Shared");
const TEST_RES = join(ROOT, "tests", "LifeAstroApi.Tests", "Resources");
const COSTS = join(ROOT, "tool", "costs.json");
const DOCS_FILE = join(ROOT, "tool", "param-docs.json");
const SAMPLES = JSON.parse(readFileSync(join(ROOT, "tool", "samples.json"), "utf8"));

const manifestPath = join(NODE_SDK, "scripts", "manifest.mjs");
if (!existsSync(manifestPath)) {
  console.error(`Node SDK manifest not found at ${manifestPath}. Set LIFEASTRO_NODE_SDK.`);
  process.exit(2);
}
const { entries } = await import(pathToFileURL(manifestPath).href);

// ---------------------------------------------------------------------------
// Credit costs (snapshot of GET /v1/meta/endpoints)
// ---------------------------------------------------------------------------
const argv = process.argv.slice(2);
const costsUrlAt = argv.indexOf("--costs-url");
if (costsUrlAt !== -1) {
  const base = String(argv[costsUrlAt + 1] ?? "").replace(/\/+$/, "");
  if (!base) {
    console.error("--costs-url needs a base URL, e.g. http://127.0.0.1:18080");
    process.exit(2);
  }
  const headers = { Accept: "application/json" };
  if (process.env.LIFEASTRO_API_KEY) headers.Authorization = `Bearer ${process.env.LIFEASTRO_API_KEY}`;
  const res = await fetch(`${base}/v1/meta/endpoints`, { headers });
  if (!res.ok) {
    console.error(`GET ${base}/v1/meta/endpoints answered ${res.status}`);
    process.exit(1);
  }
  const body = await res.json();
  const routes = {};
  for (const r of body.routes ?? []) {
    if ((r.method ?? "GET") === "GET" && String(r.path).startsWith("/v1/")) routes[r.path] = r.cost;
  }
  const sorted = Object.fromEntries(Object.entries(routes).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
  const snapshot = { source: `GET ${base}/v1/meta/endpoints`, fetched: new Date().toISOString().slice(0, 10), routes: sorted };
  writeFileSync(COSTS, JSON.stringify(snapshot, null, 1) + "\n");
  console.log(`Wrote tool/costs.json: ${Object.keys(sorted).length} routes from ${snapshot.source}`);
}
const COST_ROUTES = existsSync(COSTS) ? JSON.parse(readFileSync(COSTS, "utf8")).routes : {};

const norm = (path) => path.replace(/\{[^}]+\}/g, "{}");
const templateRegex = (path) =>
  new RegExp("^" + path.split(/\{[^}]+\}/).map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("[^/]+") + "$");

function costFor(path) {
  if (path in COST_ROUTES) return COST_ROUTES[path];
  const matches = Object.entries(COST_ROUTES)
    .filter(([route]) => norm(route) === norm(path) || templateRegex(route).test(path))
    .map(([route, cost]) => [(route.match(/\{/g) ?? []).length, cost])
    .sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  return matches.length ? matches[0][1] : null;
}

// ---------------------------------------------------------------------------
// Parameter docs (website examples.json, or the committed tool/param-docs.json)
// ---------------------------------------------------------------------------
let paramDocs = {};
if (existsSync(EXAMPLES)) {
  const raw = JSON.parse(readFileSync(EXAMPLES, "utf8"));
  for (const path of Object.keys(raw).sort()) {
    const docs = {};
    for (const d of raw[path].params_doc ?? []) {
      if (d.description) docs[d.name] = String(d.description).replace(/\s+/g, " ").trim();
    }
    paramDocs[path] = docs;
  }
  writeFileSync(DOCS_FILE, JSON.stringify(paramDocs, null, 1) + "\n");
} else if (existsSync(DOCS_FILE)) {
  console.log(`examples.json not found at ${EXAMPLES}; using tool/param-docs.json`);
  paramDocs = JSON.parse(readFileSync(DOCS_FILE, "utf8"));
} else {
  console.log(`examples.json not found at ${EXAMPLES}; parameter docs are left out.`);
}
const docsByTemplate = Object.fromEntries(Object.entries(paramDocs).map(([k, v]) => [norm(k), v]));
function docsFor(path) {
  if (paramDocs[path]) return paramDocs[path];
  if (docsByTemplate[norm(path)]) return docsByTemplate[norm(path)];
  for (const [template, d] of Object.entries(paramDocs)) {
    if (template.includes("{") && templateRegex(template).test(path)) return d;
  }
  return {};
}

// ---------------------------------------------------------------------------
// C# names
// ---------------------------------------------------------------------------
const CS_KEYWORDS = new Set(
  ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do " +
    "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int " +
    "interface internal is lock long namespace new null object operator out override params private protected " +
    "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true " +
    "try typeof uint ulong unchecked unsafe ushort using virtual void volatile while").split(" "),
);
/**
 * Node method names that C# cannot use as they are. Every method is the Node name in PascalCase plus
 * `Async`, so keywords (`char` becomes `CharAsync`), `Equals` / `GetType` / `ToString` and a method named
 * like its class cannot clash; the list stays empty unless the generator's checks below say otherwise.
 */
const CS_METHOD = {};
/** Property names a parameter class cannot have: its own members and those of object. */
const RESERVED_PROPERTY = new Set(["ToPath", "ToQuery", "WriteQuery", "ToString", "Equals", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "Finalize"]);
/** Members of every resource class and of the client: a method or group may not take these names. */
const RESERVED_MEMBER = new Set(["ToString", "Equals", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "Finalize",
  "RequestAsync", "Dispose", "BaseUrl", "Timeout", "MaxRetries", "Version", "DefaultBaseUrl", "DefaultTimeout", "DefaultMaxRetries",
  "ApiKeyEnvironmentVariable", "Core", "SendAsync", "InitResources"]);

function camelOf(s) {
  const words = String(s).split(/[^a-zA-Z0-9]+/).filter(Boolean);
  return words.map((w, i) => (i === 0 ? w[0].toLowerCase() + w.slice(1) : w[0].toUpperCase() + w.slice(1))).join("");
}
const pascal = (s) => {
  const c = camelOf(s);
  return c[0].toUpperCase() + c.slice(1);
};
const csParam = (name) => (CS_KEYWORDS.has(name) ? "@" + name : name);
const nameofParam = (name) => name; // nameof(@x) is "x"

// ---------------------------------------------------------------------------
// Parameter model (the Java generator's). `kind` is scalar, object (a BirthInput sent under a prefix)
// or path. Scalar `type`: Double, Integer, Boolean, String, Lang, Ayanamsa, HouseSystem. `def` is the
// default the Node SDK's src/params.ts applies: dateToday, tzUtc, dateTodayUtc, lower.
// ---------------------------------------------------------------------------
const scalar = (name, type, required, wire, more = {}) => ({ name, type, required, kind: "scalar", wires: [wire ?? name], ...more });
const obj = (name, required, prefix, more = {}) => ({ name, type: "BirthInput", required, kind: "object", prefix, place: false, window: false, ...more });
const AYANAMSA = () => scalar("ayanamsa", "Ayanamsa", false);
const HOUSE = () => scalar("houseSystem", "HouseSystem", false, "house_system");
const flatBirth = (prefix = "") => [
  scalar("lat", "Double", true, prefix + "lat"),
  scalar("lon", "Double", true, prefix + "lon"),
  scalar("date", "String", true, prefix + "date"),
  scalar("time", "String", true, prefix + "time"),
  scalar("tz", "String", true, prefix + "tz"),
];

// Base shapes, in the order of the Node SDK's src/params.ts builders (q* functions).
const SHAPES = {
  moment: () => [
    scalar("lat", "Double", true),
    scalar("lon", "Double", true),
    scalar("tz", "String", true),
    scalar("date", "String", false, "date", { def: "dateToday", doc: "date_today" }),
    scalar("time", "String", false),
    AYANAMSA(),
  ],
  birth: () => [...flatBirth(), AYANAMSA(), HOUSE()],
  birthPrefixed: () => [...flatBirth("birth."), AYANAMSA()],
  boygirl: () => [obj("boy", true, "boy."), obj("girl", true, "girl."), AYANAMSA()],
  twoPerson: () => [obj("personA", true, "personA."), obj("personB", true, "personB.")],
  rashi: () => [scalar("rashi", "String", true, "rashi", { def: "lower" }), scalar("date", "String", false), scalar("lang", "Lang", false)],
  numerology: () => [scalar("name", "String", false), scalar("dob", "String", false), scalar("lang", "Lang", false)],
  dateRange: () => [scalar("startDate", "String", true, "start_date"), scalar("endDate", "String", true, "end_date")],
  westernNatal: () => [...flatBirth(), HOUSE()],
  westernMoment: () => [scalar("date", "String", true), scalar("time", "String", true), scalar("tz", "String", true)],
  muhurta: () => [
    scalar("lat", "Double", true),
    scalar("lon", "Double", true),
    scalar("tz", "String", true),
    scalar("startDate", "String", true, "start_date"),
    scalar("endDate", "String", true, "end_date"),
  ],
  natalTransit: () => [obj("birth", true, "birth."), scalar("date", "String", true), scalar("time", "String", true), scalar("tz", "String", true), AYANAMSA()],
  // Also sends the birth place unprefixed: the handler casts the natal lagna from it.
  natalTransitPlace: () => [
    obj("birth", true, "birth.", { place: true }),
    scalar("date", "String", true),
    scalar("time", "String", true),
    scalar("tz", "String", true),
    AYANAMSA(),
  ],
  natalWindow: () => [obj("birth", true, "birth.", { window: true }), scalar("from", "String", false), scalar("tz", "String", false), AYANAMSA()],
  natalWindowOptional: () => [obj("birth", false, "birth.", { window: true }), scalar("from", "String", false), scalar("tz", "String", false), AYANAMSA()],
  instant: () => [
    scalar("tz", "String", false, "tz", { def: "tzUtc", doc: "tz_utc" }),
    scalar("date", "String", false, "date", { def: "dateTodayUtc", doc: "date_today" }),
    scalar("time", "String", false),
    AYANAMSA(),
  ],
  none: () => [],
  custom: () => [],
};

function extraType(x, name) {
  if (name === "lang" || name === "locale" || x.type === "locale") return "Lang";
  if (name === "ayanamsa") return "Ayanamsa";
  if (name === "houseSystem") return "HouseSystem";
  return { number: "Double", integer: "Integer", boolean: "Boolean" }[x.type] ?? "String";
}

function resolveEntry(e) {
  const { ns } = e._resolved;
  const nodeMethod = e._resolved.method;
  const method = CS_METHOD[`${ns}.${nodeMethod}`] ?? pascal(nodeMethod) + "Async";
  const shapeFn = SHAPES[e.shape];
  if (!shapeFn) throw new Error(`Unknown shape "${e.shape}" for ${e.path}`);
  const params = [];
  const byName = new Map();
  const ops = []; // [paramName, wire, def]

  const add = (p) => {
    if (!/^[a-z][A-Za-z0-9]*$/.test(p.name) || RESERVED_PROPERTY.has(pascal(p.name))) {
      throw new Error(`${e.path}: parameter name "${p.name}" cannot be used in C# here`);
    }
    const existing = byName.get(p.name);
    if (!existing) {
      params.push(p);
      byName.set(p.name, p);
      return p;
    }
    if (existing.kind !== "scalar" || p.kind !== "scalar") throw new Error(`${e.path}: "${p.name}" is declared twice`);
    existing.required = existing.required || p.required;
    return existing;
  };

  for (const n of e.pathParams ?? []) {
    add({ name: n, type: "String", required: true, kind: "path", wires: [], numeric: n === "n" || n === "number" });
  }
  for (const p of shapeFn()) {
    const target = add(p);
    if (p.kind === "object") continue;
    for (const w of p.wires) ops.push([target.name, w, p.def ?? null]);
  }
  for (const x of e.extras ?? []) {
    const name = x.name ?? camelOf(x.wire);
    const target = add(scalar(name, extraType(x, name), !!x.required, x.wire));
    // `lang` is sent as both `lang` and `locale` (the API reads either; some older routes read only
    // one), exactly as the Node SDK does. A later write of the same wire name wins, so `lang` wins
    // over the older `locale` input when both are set.
    const wires = x.wire === "lang" ? ["lang", "locale"] : [x.wire];
    for (const w of wires) {
      ops.push([target.name, w, null]);
      if (!target.wires.includes(w)) target.wires.push(w);
    }
  }
  const className = pascal(ns.replace(/\./g, "-")) + pascal(nodeMethod) + "Params";
  const m = { entry: e, ns, method, nodeMethod, params, ops, cost: costFor(e.path), className };
  m.conv = conveniences(e, params);
  for (const p of params) {
    if (p.kind === "object" && !p.required && !p.window) throw new Error(`${e.path}: optional birth object without the window rule`);
  }
  return m;
}

/**
 * Convenience constructor arguments that fill several fields at once: a BirthInput (`birth`, `boy`,
 * `girl`) for flat birth fields, a Place for lat / lon / tz. Same rules as the Java builder's setters.
 */
function conveniences(e, params) {
  const has = (n) => params.some((p) => p.name === n && p.kind === "scalar");
  const wire = (n) => params.find((p) => p.name === n)?.wires?.[0];
  const out = [];
  const birthShapes = ["birth", "birthPrefixed", "westernNatal"];
  if (["date", "time", "tz", "lat", "lon"].every(has) && (birthShapes.includes(e.shape) || (e.shape === "custom" && wire("date") === "birth.date"))) {
    out.push({ arg: "birth", type: "BirthInput", map: { date: "date", time: "time", tz: "tz", lat: "lat", lon: "lon" } });
  }
  if (["birthDate", "birthTime", "birthTz"].every(has)) {
    const map = { date: "birthDate", time: "birthTime", tz: "birthTz" };
    if (has("lat") && has("lon")) Object.assign(map, { lat: "lat", lon: "lon" });
    out.push({ arg: "birth", type: "BirthInput", map });
  }
  for (const who of ["boy", "girl"]) {
    if ([`${who}Date`, `${who}Time`, `${who}Tz`].every(has) && !params.some((p) => p.name === who)) {
      out.push({ arg: who, type: "BirthInput", map: { date: `${who}Date`, time: `${who}Time`, tz: `${who}Tz` } });
    }
  }
  const names = new Set(out.map((c) => c.arg));
  if (names.size !== out.length) throw new Error(`${e.path}: two conveniences with one name`);
  for (const s of names) if (params.some((p) => p.name === s)) throw new Error(`${e.path}: convenience ${s} clashes with a parameter`);
  if (!out.length && ["lat", "lon", "tz"].every(has) && !params.some((p) => p.name === "place")) {
    out.push({ arg: "place", type: "Place", map: { lat: "lat", lon: "lon", tz: "tz" } });
  }
  return out;
}

// ---------------------------------------------------------------------------
// XML doc helpers
// ---------------------------------------------------------------------------
const escapeXml = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
/** Plain text (with `code` spans) to XML doc text. */
function xml(text) {
  const parts = String(text).replace(/\s+/g, " ").trim().split("`");
  return parts.map((p, i) => (i % 2 === 1 ? `<c>${escapeXml(p)}</c>` : escapeXml(p))).join("");
}
/** Splits on spaces, but never inside an XML tag (`<see cref="..."/>` stays on one line). */
function tokens(text) {
  const out = [];
  let cur = "";
  let depth = 0;
  for (const ch of text) {
    if (ch === "<") depth++;
    if (ch === ">") depth = Math.max(0, depth - 1);
    if (ch === " " && depth === 0) {
      if (cur) out.push(cur);
      cur = "";
    } else cur += ch;
  }
  if (cur) out.push(cur);
  return out;
}
function wrap(text, width) {
  const words = tokens(text);
  const lines = [];
  let line = "";
  for (const w of words) {
    if (line && line.length + 1 + w.length > width) {
      lines.push(line);
      line = w;
    } else line = line ? `${line} ${w}` : w;
  }
  if (line) lines.push(line);
  return lines;
}
const WIDTH = 120;
/** A doc element: `<tag attrs>text</tag>`, wrapped over `///` lines. */
function docTag(indent, tag, text, attrs = "") {
  const pre = `${indent}/// `;
  const open = `<${tag}${attrs}>`;
  const close = `</${tag}>`;
  const one = `${pre}${open}${text}${close}`;
  if (one.length <= WIDTH) return [one];
  const out = [`${pre}${open}`];
  for (const l of wrap(text, WIDTH - pre.length)) out.push(pre + l);
  out.push(`${pre}${close}`);
  return out;
}
/** A <code> block inside <remarks> or <summary>. */
function docCode(indent, lines) {
  const pre = `${indent}/// `;
  return [`${pre}<code>`, ...lines.map((l) => pre + escapeXml(l)), `${pre}</code>`];
}

const PARAM_DOC = {
  lat: "Latitude in decimal degrees, north positive.",
  lon: "Longitude in decimal degrees, east positive.",
  tz: "Time zone: IANA name (`Asia/Kolkata`) or UTC offset (`+05:30`).",
  tz_utc: "Time zone: IANA name (`Asia/Kolkata`) or UTC offset (`+05:30`). Defaults to `UTC`.",
  date: "Date, `YYYY-MM-DD`.",
  date_today: "Date, `YYYY-MM-DD`. Defaults to today in `tz`.",
  time: "Local time, 24-hour `HH:mm`.",
  lang: "Response language. Sent as `lang` and `locale`.",
  lang_shape: "Response language.",
  ayanamsa: "Sidereal ayanamsa; the API uses Lahiri when left out.",
  houseSystem: "House system, sent as `house_system`.",
  startDate: "First day of the window, `YYYY-MM-DD`.",
  endDate: "Last day of the window, `YYYY-MM-DD`.",
  rashi: "Moon sign, English or Hindi name in any case (`aries`, `Mesh`); sent lowercased.",
  name: "Full name as written on official documents.",
  dob: "Date of birth, `YYYY-MM-DD`.",
  from: "Start of the range, `YYYY-MM-DD` (local midnight in `tz`); defaults to now.",
  boy: "Groom's birth details, sent as `boy.date`, `boy.time`, `boy.tz`, `boy.lat`, `boy.lon`.",
  girl: "Bride's birth details, sent as `girl.date`, `girl.time`, `girl.tz`, `girl.lat`, `girl.lon`.",
  personA: "First person's birth details, sent as `personA.date`, `personA.time`, `personA.tz`, `personA.lat`, `personA.lon`.",
  personB: "Second person's birth details, sent as `personB.date`, `personB.time`, `personB.tz`, `personB.lat`, `personB.lon`.",
  birth: "Natal chart, sent as `birth.date`, `birth.time`, `birth.tz`, `birth.lat`, `birth.lon`.",
  birth_place: "Natal chart, sent as `birth.date`, `birth.time`, `birth.tz`, `birth.lat`, `birth.lon`; `lat` and `lon` are also sent unprefixed.",
  birth_window: "Natal chart, sent as `birth.*`: `date`, `time`, `tz`; `lat` and `lon` (optional: `BirthInput.WithoutPlace`) add the lagna-based fields.",
};

function paramDoc(m, p) {
  if (p.kind === "object") {
    if (p.place) return PARAM_DOC.birth_place;
    if (p.window) return PARAM_DOC.birth_window;
    return PARAM_DOC[p.name] ?? "";
  }
  if (p.name === "lang") return p.wires.includes("locale") ? PARAM_DOC.lang : PARAM_DOC.lang_shape;
  if (p.doc) return PARAM_DOC[p.doc];
  const docs = docsFor(m.entry.path);
  const fromExample = docs[p.wires[0]] ?? (p.kind === "path" ? docs[p.name] : undefined);
  if (fromExample) return fromExample;
  return PARAM_DOC[p.name] ?? "";
}

function wireNote(p) {
  if (p.kind === "path") return "Path parameter.";
  if (p.name === "lang" && p.wires.includes("locale")) return "";
  if (p.kind === "object") return "";
  const w = p.wires.filter((x) => x !== p.name);
  if (!w.length) return "";
  return `Wire name ${w.map((x) => "`" + x + "`").join(", ")}.`;
}
const fullDoc = (m, p) => [paramDoc(m, p) || `The \`${p.wires[0] ?? p.name}\` value.`, wireNote(p)].filter(Boolean).join(" ");

// ---------------------------------------------------------------------------
// C# types and literals
// ---------------------------------------------------------------------------
const DATE_NAMES = new Set(["date", "startDate", "endDate", "dob", "birthDate", "onDate", "boyDate", "girlDate", "weekStart", "refDate"]);
const TIME_NAMES = new Set(["time", "birthTime", "boyTime", "girlTime"]);
const VALUE_TYPES = new Set(["double", "int", "bool", "Lang", "Ayanamsa", "HouseSystem", "LifeAstroDate", "LifeAstroTime"]);

function baseType(p) {
  if (p.kind === "object") return "BirthInput";
  if (p.kind === "path") return "string";
  switch (p.type) {
    case "Double":
      return "double";
    case "Integer":
      return "int";
    case "Boolean":
      return "bool";
    case "Lang":
    case "Ayanamsa":
    case "HouseSystem":
      return p.type;
    default:
      if (DATE_NAMES.has(p.name)) return "LifeAstroDate";
      if (TIME_NAMES.has(p.name)) return "LifeAstroTime";
      return "string";
  }
}
const propType = (p) => (p.required ? baseType(p) : baseType(p) + "?");
const prop = (p) => pascal(p.name);

function csString(s) {
  let out = '"';
  for (const ch of String(s)) {
    const c = ch.codePointAt(0);
    if (ch === '"') out += '\\"';
    else if (ch === "\\") out += "\\\\";
    else if (c < 0x20 || c > 0x7e) {
      for (const unit of ch.split("")) out += "\\u" + unit.charCodeAt(0).toString(16).padStart(4, "0");
    } else out += ch;
  }
  return out + '"';
}
function csNumber(v, type, where) {
  const n = typeof v === "number" ? v : Number(v);
  if (!Number.isFinite(n)) throw new Error(`${where}: ${v} is not a number`);
  if (type === "int") {
    if (!Number.isInteger(n)) throw new Error(`${where}: ${v} is not an integer`);
    return String(n);
  }
  const s = String(n);
  return /[.e]/.test(s) ? s : s + ".0";
}
const LANG_NAMES = { en: "En", hi: "Hi", mr: "Mr", gu: "Gu", bn: "Bn", te: "Te", ta: "Ta", kn: "Kn", ml: "Ml", pa: "Pa" };
function birthLiteral(b) {
  if (b.lat === undefined) return `BirthInput.WithoutPlace(${csString(b.date)}, ${csString(b.time)}, ${csString(b.tz)})`;
  return `new BirthInput(${csString(b.date)}, ${csString(b.time)}, ${csString(b.tz)}, ${csNumber(b.lat, "double")}, ${csNumber(b.lon, "double")})`;
}
function literal(p, v, where) {
  if (p.kind === "object") return birthLiteral(v);
  if (p.kind === "path") return p.numeric && typeof v === "number" ? String(v) : csString(String(v));
  const t = baseType(p);
  switch (t) {
    case "double":
    case "int":
      return csNumber(v, t, where);
    case "bool":
      if (typeof v !== "boolean") throw new Error(`${where}: ${v} is not a boolean`);
      return String(v);
    case "Lang":
      if (!LANG_NAMES[v]) throw new Error(`${where}: unknown language ${v}`);
      return `Lang.${LANG_NAMES[v]}`;
    default:
      return csString(String(v));
  }
}

/** The sample input of a method, keyed by parameter name: the same rule as tool/node-goldens.mjs. */
function sampleInput(m) {
  const input = structuredClone(SAMPLES.shapes[m.entry.shape] ?? {});
  for (const p of m.params) {
    if (p.kind === "path" && p.name in SAMPLES.path) input[p.name] = SAMPLES.path[p.name];
  }
  for (const x of m.entry.extras ?? []) {
    if (x.required && x.wire in SAMPLES.wire) input[x.name ?? camelOf(x.wire)] = SAMPLES.wire[x.wire];
  }
  Object.assign(input, SAMPLES.perPath[m.entry.path] ?? {});
  const names = new Set(m.params.map((p) => p.name));
  return Object.fromEntries(Object.entries(input).filter(([k]) => names.has(k)));
}

// ---------------------------------------------------------------------------
// Constructors: the required values, plus one overload with the conveniences (BirthInput / Place),
// each doubled with an int for a numeric path parameter (`n`, `number`).
// ---------------------------------------------------------------------------
const ordered = (params) => [...params.filter((p) => p.required), ...params.filter((p) => !p.required)];

/** Constructor argument lists. Each argument: { name, type, doc, param? (a field), conv? (a convenience), intPath? }. */
function ctorVariants(m) {
  const req = m.params.filter((p) => p.required);
  const base = req.map((p) => ({ name: p.name, type: baseType(p), param: p }));
  const variants = [{ args: base, conv: false }];
  if (m.conv.length) {
    let args = [];
    const placed = new Set();
    for (const p of req) {
      const c = m.conv.find((c) => Object.values(c.map).includes(p.name));
      if (!c) {
        args.push({ name: p.name, type: baseType(p), param: p });
      } else if (!placed.has(c.arg)) {
        placed.add(c.arg);
        args.push({ name: c.arg, type: c.type, conv: c });
      }
    }
    for (const c of m.conv) if (!placed.has(c.arg)) args.push({ name: c.arg, type: c.type, conv: c });
    variants.push({ args, conv: true });
  }
  const out = [];
  for (const v of variants) {
    out.push(v);
    if (v.args.some((a) => a.param?.kind === "path" && a.param.numeric)) {
      out.push({ ...v, args: v.args.map((a) => (a.param?.kind === "path" && a.param.numeric ? { ...a, type: "int", intPath: true } : a)), intOf: v });
    }
  }
  const sigs = new Set();
  for (const v of out) {
    const sig = v.args.map((a) => a.type).join(",");
    if (sigs.has(sig)) throw new Error(`${m.entry.path}: two constructors with the signature (${sig})`);
    sigs.add(sig);
  }
  return out;
}

/** A BirthInput convenience whose lat / lon are required fields: the birth must carry coordinates. */
function convNeedsPlace(m, c) {
  return c.type === "BirthInput" && !!c.map.lat && m.params.find((p) => p.name === c.map.lat)?.required === true;
}

function convFieldList(c) {
  const list = Object.values(c.map).map((f) => `<c>${pascal(f)}</c>`);
  return list.length > 1 ? list.slice(0, -1).join(", ") + " and " + list[list.length - 1] : list[0];
}
const CONV_WHO = { birth: "the birth details", boy: "the groom's birth details", girl: "the bride's birth details", place: "the place" };

function ctorSrc(m, v, I) {
  const cls = m.className;
  const out = [];
  const convArgs = v.args.filter((a) => a.conv);
  const from = convArgs.length
    ? convArgs.map((a) => (a.conv.type === "Place" ? "a place" : a.conv.arg === "birth" ? "a birth" : `the ${a.conv.arg === "boy" ? "groom's" : "bride's"} birth`)).join(" and ")
    : "";
  out.push(...docTag(I, "summary", from ? `Creates the parameters from ${from}.` : "Creates the parameters."));
  for (const a of v.args) {
    let text;
    if (a.conv) {
      const needsPlace = convNeedsPlace(m, a.conv);
      text = `${CONV_WHO[a.conv.arg]}: sets ${convFieldList(a.conv)}${needsPlace ? " (it needs <c>Lat</c> and <c>Lon</c>)" : ""}.`;
      text = text[0].toUpperCase() + text.slice(1);
    } else {
      text = xml(fullDoc(m, a.param));
      if (a.param.kind === "path" && a.param.numeric) text += a.intPath ? " A number, sent as text." : "";
    }
    out.push(...docTag(I, "param", text, ` name="${nameofParam(a.name)}"`));
  }
  const nullable = v.args.filter((a) => a.type === "string" || a.type === "BirthInput" || a.type === "Place");
  if (nullable.length) {
    out.push(...docTag(I, "exception", `${nullable.map((a) => `<paramref name="${a.name}"/>`).join(", ")} is null.`, ' cref="System.ArgumentNullException"'));
  }
  const needsCoords = (a) => a.type === "BirthInput" && (a.conv ? convNeedsPlace(m, a.conv) : !a.param.window);
  const blank = v.args.filter((a) => (a.type === "string" && !a.intPath) || a.type === "LifeAstroDate" || a.type === "LifeAstroTime" || needsCoords(a));
  if (blank.length) {
    out.push(...docTag(I, "exception", "A required value is empty" + (v.args.some(needsCoords) ? ", or a birth has no <c>Lat</c> / <c>Lon</c>" : "") + ".", ' cref="System.ArgumentException"'));
  }
  const sig = v.args.map((a) => `${a.type} ${csParam(a.name)}`).join(", ");
  if (v.intOf) {
    // int path parameter: chain to the string form.
    const args = v.args.map((a) => (a.intPath ? `${csParam(a.name)}.ToString(CultureInfo.InvariantCulture)` : csParam(a.name)));
    out.push(`${I}public ${cls}(${sig})`, `${I}    : this(${args.join(", ")})`, `${I}{`, `${I}}`);
    return out;
  }
  if (!v.conv) {
    out.push(`${I}public ${cls}(${sig})`, `${I}{`);
    for (const a of v.args) out.push(`${I}    ${prop(a.param)} = ${assignExpr(a.param, csParam(a.name))};`);
    out.push(`${I}}`);
    return out;
  }
  // Convenience constructor: chain to the base constructor; optional fields of a convenience go in the body.
  const firstUse = new Set();
  const use = (c) => {
    const id = csParam(c.arg);
    if (firstUse.has(c.arg)) return id;
    firstUse.add(c.arg);
    if (c.type === "Place") return `Wire.NotNull(${id}, nameof(${c.arg}))`;
    return convNeedsPlace(m, c) ? `Wire.RequirePlace(${id}, nameof(${c.arg}))` : `Wire.NotNull(${id}, nameof(${c.arg}))`;
  };
  const fieldOf = (c, field) => {
    const k = Object.entries(c.map).find(([, f]) => f === field)[0];
    return k;
  };
  const access = (c, k, required) => {
    const expr = `${use(c)}.${pascal(k)}`;
    if (c.type === "BirthInput" && (k === "lat" || k === "lon")) return required ? `${expr}!.Value` : expr;
    return expr;
  };
  const req = m.params.filter((p) => p.required);
  const baseArgs = req.map((p) => {
    const c = m.conv.find((c) => Object.values(c.map).includes(p.name));
    return c ? access(c, fieldOf(c, p.name), true) : csParam(p.name);
  });
  out.push(`${I}public ${cls}(${sig})`, `${I}    : this(${baseArgs.join(", ")})`, `${I}{`);
  for (const c of m.conv) {
    for (const [k, f] of Object.entries(c.map)) {
      const p = m.params.find((x) => x.name === f);
      if (!p.required) out.push(`${I}    ${prop(p)} = ${access(c, k, false)};`);
    }
  }
  out.push(`${I}}`);
  return out;
}

function assignExpr(p, id) {
  const t = baseType(p);
  if (p.kind === "object") return p.window ? `Wire.NotNull(${id}, nameof(${p.name}))` : `Wire.RequirePlace(${id}, nameof(${p.name}))`;
  if (t === "string") return `Wire.RequireText(${id}, nameof(${p.name}))`;
  if (t === "LifeAstroDate") return `Wire.RequireDate(${id}, nameof(${p.name}))`;
  if (t === "LifeAstroTime") return `Wire.RequireTime(${id}, nameof(${p.name}))`;
  return id;
}

/** The C# value written for a wire name: the field, or the Node SDK's default. */
function opExpr(m, name, def) {
  const P = (n) => pascal(n);
  switch (def) {
    case "dateToday":
      return `${P(name)} ?? Wire.TodayIn(Tz)`;
    case "tzUtc":
      return `${P(name)} ?? "UTC"`;
    case "dateTodayUtc":
      return `${P(name)} ?? Wire.TodayIn(Tz ?? "UTC")`;
    case "lower": {
      const p = m.params.find((x) => x.name === name);
      return p.required ? `${P(name)}.Trim().ToLowerInvariant()` : `${P(name)}?.Trim().ToLowerInvariant()`;
    }
    default:
      return P(name);
  }
}

const HEADER = `// <auto-generated>
// GENERATED by tool/generate.mjs from the endpoint manifest of the Node.js SDK.
// Do not edit by hand: run \`node tool/generate.mjs\`.
// </auto-generated>
#nullable enable`;

const resourceClass = (ns) => (ns.startsWith("western.") ? "Western" + pascal(ns.split(".")[1]) : pascal(ns)) + "Resource";
const accessorChain = (ns) => ns.split(".").map((s) => pascal(s)).join(".");
const methodCref = (m) =>
  `LifeAstroApi.Resources.${resourceClass(m.ns)}.${m.method}(${m.params.length ? `LifeAstroApi.Params.${m.className}, ` : ""}LifeAstroApi.RequestOptions, System.Threading.CancellationToken)`;

/** `new X(args) { Prop = value, ... }` for a sample input. */
function creation(m, input, extraInit = []) {
  const variants = ctorVariants(m);
  const convOk = m.conv.length && m.conv.every((c) => Object.values(c.map).every((f) => input[f] !== undefined));
  const numericPath = m.params.find((p) => p.kind === "path" && p.numeric);
  const useInt = numericPath && typeof input[numericPath.name] === "number";
  const v = variants.find((x) => x.conv === !!convOk && !!x.intOf === !!useInt);
  const used = new Set();
  const args = v.args.map((a) => {
    if (a.conv) {
      const vals = Object.fromEntries(Object.entries(a.conv.map).map(([k, f]) => [k, input[f]]));
      for (const f of Object.values(a.conv.map)) used.add(f);
      return a.conv.type === "Place" ? `new Place(${csNumber(vals.lat, "double")}, ${csNumber(vals.lon, "double")}, ${csString(vals.tz)})` : birthLiteral(vals);
    }
    used.add(a.param.name);
    if (input[a.param.name] === undefined) throw new Error(`${m.entry.path}: no sample for required ${a.param.name}`);
    return literal(a.param, input[a.param.name], `${m.entry.path} ${a.param.name}`);
  });
  const init = [];
  for (const p of ordered(m.params)) {
    if (used.has(p.name) || input[p.name] === undefined) continue;
    init.push(`${prop(p)} = ${literal(p, input[p.name], `${m.entry.path} ${p.name}`)}`);
  }
  init.push(...extraInit);
  return `new ${m.className}(${args.join(", ")})` + (init.length ? ` { ${init.join(", ")} }` : "");
}

// ---------------------------------------------------------------------------
// Parameter class source
// ---------------------------------------------------------------------------
function paramsClassSrc(m) {
  const cls = m.className;
  const P = ordered(m.params);
  const I = "        ";
  const req = P.filter((p) => p.required);
  const out = [HEADER, "using System.Globalization;", "", "namespace LifeAstroApi.Params", "{"];
  const desc = m.entry.desc || m.entry.path;
  const example = creation(m, sampleInput(m));
  const call = `client.${accessorChain(m.ns)}.${m.method}`;
  out.push(...docTag("    ", "summary", `Parameters of <c>GET ${escapeXml(m.entry.path)}</c>, for <see cref="${methodCref(m)}"/>.`));
  const remarks = [xml(desc)];
  out.push("    /// <remarks>");
  out.push(...docTag("    ", "para", remarks[0]));
  out.push(...docCode("    ", [`var p = ${example};`, `JsonElement result = await ${call}(p);`]));
  out.push(
    ...docTag(
      "    ",
      "para",
      req.length
        ? `Required: ${req.map((p) => `<c>${prop(p)}</c>`).join(", ")} (constructor parameters). The other values are optional properties; ${"<see cref=\"LifeAstroApi.LifeAstroParams.ToQuery\"/>"} shows what is sent.`
        : `Every value is optional. ${"<see cref=\"LifeAstroApi.LifeAstroParams.ToQuery\"/>"} shows what is sent.`,
    ),
  );
  out.push("    /// </remarks>");
  out.push(`    public sealed class ${cls} : LifeAstroParams`, "    {");
  const variants = ctorVariants(m);
  if (!req.length) {
    out.push(...docTag(I, "summary", "Creates the parameters; set the values you need."));
    out.push(`${I}public ${cls}()`, `${I}{`, `${I}}`);
  }
  let first = !!req.length;
  for (const v of variants) {
    if (!req.length && !v.args.length) continue;
    if (!first) out.push("");
    first = false;
    out.push(...ctorSrc(m, v, I));
  }
  for (const p of P) {
    out.push("");
    out.push(...docTag(I, "summary", xml(fullDoc(m, p))));
    out.push(p.required ? `${I}public ${propType(p)} ${prop(p)} { get; }` : `${I}public ${propType(p)} ${prop(p)} { get; set; }`);
  }
  // ToPath
  out.push("");
  out.push(`${I}/// <inheritdoc/>`);
  const pathParams = P.filter((p) => p.kind === "path");
  if (pathParams.length) {
    const args = pathParams.map((p) => `"${p.name}", ${prop(p)}`).join(", ");
    out.push(`${I}public override string ToPath() => Wire.Path(${csString(m.entry.path)}, ${args});`);
  } else {
    out.push(`${I}public override string ToPath() => ${csString(m.entry.path)};`);
  }
  // WriteQuery
  out.push("");
  out.push(`${I}private protected override void WriteQuery(QueryWriter query)`, `${I}{`);
  for (const p of m.params.filter((x) => x.kind === "object")) {
    out.push(`${I}    query.PutBirth(${csString(p.prefix)}, ${prop(p)});`);
    if (p.place) out.push(`${I}    query.PutPlace(${prop(p)});`);
  }
  for (const [name, wire, def] of m.ops) out.push(`${I}    query.Put(${csString(wire)}, ${opExpr(m, name, def)});`);
  out.push(`${I}}`);
  out.push("    }", "}", "");
  // Keep `using System.Globalization;` only where an int path parameter needs CultureInfo.
  let src = out.join("\n");
  if (!src.split("namespace LifeAstroApi.Params")[1].includes("CultureInfo")) src = src.replace("using System.Globalization;\n\n", "\n");
  return src;
}

// ---------------------------------------------------------------------------
// Resource class source
// ---------------------------------------------------------------------------
const NS_DOC = {
  ashtakavarga: "Ashtakavarga: sarva, bhinna (per planet), kaksha and transit score.",
  calendar: "Hindu calendar: month, ritu, samvatsara, adhik maas, solstices.",
  chart: "Birth chart (kundli): planets, ascendant, houses, divisional charts, dignity, aspects, shadbala, KP, and the data bundles `KundliFactsAsync`, `YogasAsync`, `DoshasAsync`, `SensitivityAsync`.",
  dasha: "Vimshottari (5 levels), Yogini and Chara dashas.",
  eclipses: "Solar and lunar eclipses in a date range, with local contact times and sutak.",
  festivals: "Hindu festivals and vrat dates by month, on a date, or by vrat family for a year.",
  geo: "Place search, reverse geocoding, time zone lookup. Answers in English.",
  horoscope: "Daily, weekly and monthly horoscope by rashi.",
  lalkitab: "Lal Kitab chart and dasha.",
  meta: "Free catalogs: endpoints with credit costs, ayanamsas, yogas, doshas, locales, version. Answers in English.",
  milan: "Kundli matching: Ashtakoota, doshas, navamsa and dasha compatibility, overlay, summary.",
  muhurta: "Muhurta: vivah, naamkaran, griha pravesh, vyapar, yatra, best time, chandra/tara bala.",
  narrative: "Authored Vedic readings: lagna, planets, yogas, doshas, dasha phal, transit phal, prashna, horoscopes and more.",
  numerology: "Numerology: driver, conductor, soul, personality, destiny, full, advanced, challenges, personal periods.",
  panchang: "Panchang: tithi, nakshatra, yoga, karana, vara, sunrise/sunset, muhurta windows, choghadiya, hora, monthly calendar.",
  planetMoments: "Planet moments: retrograde windows, sign ingress, combustion window, speed.",
  prashna: "Prashna (horary): answer, chart, arudha, significators, KP horary.",
  remedies: "Remedies: by rule id, by graha (`ForGrahaAsync`), mantra, pooja, vrat, daan, rudraksha, yantra, Lal Kitab.",
  reports: "Composed JSON reports: kundli (lite, detailed, brihad), match making, mangal dosha, sade sati, varshaphal, numerology, horoscopes.",
  tarot: "Tarot: cards, a single card, daily card, spreads, yes/no. Answers in English.",
  transit: "Transits: positions, sade sati, small panoti, tarabala, vedha, double transit, dated windows.",
  varshaphal: "Varshaphal (annual chart): chart, lord, muntha, mudda dasha, harsha bala, tajika yogas.",
  western: "Western (tropical) astrology. Sub-groups: `Natal`, `Synastry`, `Transits`, `Returns` and more. Answers in English (`Narrative` also in Hindi and Marathi).",
};

function methodSrc(m, I) {
  const out = [];
  const cls = m.className;
  const noParams = m.params.length === 0;
  const optional = !noParams && !m.params.some((p) => p.required);
  const costLine = m.cost !== null && m.cost !== undefined ? `Cost: ${m.cost} credit${m.cost === 1 ? "" : "s"} per call. ` : "";
  const nodeNote = ` Node.js SDK: <c>${m.ns}.${m.nodeMethod}</c>.`;
  const pdecl = noParams ? "" : optional ? `${cls}? parameters = null, ` : `${cls} parameters, `;
  const sendArg = noParams ? csString(m.entry.path) : optional ? `parameters ?? new ${cls}()` : "Wire.NotNull(parameters, nameof(parameters))";
  const docs = (generic) => {
    const d = [];
    d.push(...docTag(I, "summary", xml(m.entry.desc || m.entry.path) + (generic ? " Mapped to your own type." : "")));
    d.push(...docTag(I, "remarks", `${costLine}<c>GET ${escapeXml(m.entry.path)}</c>.${nodeNote}`));
    if (generic) d.push(...docTag(I, "typeparam", "Your type, read with System.Text.Json (<see cref=\"LifeAstroApi.LifeAstroClientOptions.JsonSerializerOptions\"/>).", ' name="T"'));
    if (!noParams) {
      d.push(...docTag(I, "param", optional ? `The parameters, or null for none (<see cref="LifeAstroApi.Params.${cls}"/>).` : `The parameters (<see cref="LifeAstroApi.Params.${cls}"/>).`, ' name="parameters"'));
    }
    d.push(...docTag(I, "param", "Per-call timeout, raw body or <c>OnResponse</c> hook; null for none.", ' name="options"'));
    d.push(...docTag(I, "param", "Cancels the call.", ' name="cancellationToken"'));
    d.push(...docTag(I, "returns", generic ? "The response mapped to <typeparamref name=\"T\"/>; default for a JSON null." : "The response JSON."));
    if (!noParams && !optional) d.push(...docTag(I, "exception", "<paramref name=\"parameters\"/> is null.", ' cref="System.ArgumentNullException"'));
    d.push(...docTag(I, "exception", generic
      ? "An API error (a subclass per status), a network failure or timeout, or a response that does not map to <typeparamref name=\"T\"/> (code <c>invalid_response</c>)."
      : "An API error (a subclass per status), or a network failure or timeout.", ' cref="LifeAstroApi.LifeAstroException"'));
    d.push(...docTag(I, "exception", "<paramref name=\"cancellationToken\"/> was cancelled.", ' cref="System.OperationCanceledException"'));
    return d;
  };
  out.push(...docs(false));
  out.push(`${I}public Task<JsonElement> ${m.method}(${pdecl}RequestOptions? options = null, CancellationToken cancellationToken = default) =>`);
  out.push(`${I}    _client.SendAsync<JsonElement>(${sendArg}, options, cancellationToken);`);
  out.push("");
  out.push(...docs(true));
  out.push(`${I}public Task<T?> ${m.method}<T>(${pdecl}RequestOptions? options = null, CancellationToken cancellationToken = default) =>`);
  out.push(`${I}    _client.SendAsync<T>(${sendArg}, options, cancellationToken);`);
  return out;
}

function resourceSrc(cls, doc, methods, children) {
  const I = "        ";
  const out = [HEADER];
  if (methods.length) {
    out.push("using System.Text.Json;", "using System.Threading;", "using System.Threading.Tasks;");
    if (methods.some((m) => m.params.length)) out.push("using LifeAstroApi.Params;");
  }
  out.push("", "namespace LifeAstroApi.Resources", "{");
  out.push(...docTag("    ", "summary", xml(doc)));
  out.push(`    public sealed class ${cls}`, "    {");
  if (methods.length) out.push(`${I}private readonly LifeAstroClient _client;`, "");
  out.push(`${I}internal ${cls}(LifeAstroClient client)`, `${I}{`);
  if (methods.length) out.push(`${I}    _client = client;`);
  for (const [propName, child] of children) out.push(`${I}    ${propName} = new ${child}(client);`);
  out.push(`${I}}`);
  for (const [propName, child, childDoc] of children) {
    out.push("");
    out.push(...docTag(I, "summary", xml(childDoc)));
    out.push(`${I}public ${child} ${propName} { get; }`);
  }
  for (const m of methods) {
    out.push("");
    out.push(...methodSrc(m, I));
  }
  out.push("    }", "}", "");
  return out.join("\n");
}

function clientPartialSrc(groups) {
  const I = "        ";
  const out = [HEADER, "using LifeAstroApi.Resources;", "", "namespace LifeAstroApi", "{", "    public sealed partial class LifeAstroClient", "    {"];
  for (const [ns, cls] of groups) {
    out.push(...docTag(I, "summary", xml(`${NS_DOC[ns] ?? ns} Use \`client.${pascal(ns)}\`.`)));
    out.push(`${I}public ${cls} ${pascal(ns)} { get; private set; } = null!;`, "");
  }
  out.push(`${I}private void InitResources()`, `${I}{`);
  for (const [ns, cls] of groups) out.push(`${I}    ${pascal(ns)} = new ${cls}(this);`);
  out.push(`${I}}`, "    }", "}", "");
  return out.join("\n");
}

// ---------------------------------------------------------------------------
// Resolve, check, write
// ---------------------------------------------------------------------------
const methods = entries.map(resolveEntry);

const seenMethod = new Map();
const seenClass = new Map();
const renames = [];
for (const m of methods) {
  const key = `${m.ns}#${m.method}`;
  if (seenMethod.has(key)) throw new Error(`C# method collision: ${m.ns}.${m.method} from ${seenMethod.get(key)} and ${m.entry.path}`);
  seenMethod.set(key, m.entry.path);
  if (seenClass.has(m.className)) throw new Error(`Parameter class collision: ${m.className} from ${seenClass.get(m.className)} and ${m.entry.path}`);
  seenClass.set(m.className, m.entry.path);
  if (RESERVED_MEMBER.has(m.method) || m.method === resourceClass(m.ns)) throw new Error(`${m.ns}.${m.method} clashes with a member; add it to CS_METHOD`);
  if (m.method !== pascal(m.nodeMethod) + "Async") renames.push(`${m.ns}.${m.nodeMethod} -> ${m.method}`);
  if (m.cost === null) console.warn(`warning: no credit cost for ${m.entry.path} in tool/costs.json`);
  const said = /\((\d[\d,]*) credits?\)/.exec(m.entry.desc ?? "");
  if (said && m.cost !== null && Number(said[1].replace(/,/g, "")) !== m.cost) {
    console.warn(`warning: ${m.ns}.${m.nodeMethod} description says ${said[1]} credits, tool/costs.json says ${m.cost}`);
  }
  for (const p of m.params) {
    if (CS_KEYWORDS.has(p.name)) renames.push(`${m.className}: constructor parameter @${p.name}`);
  }
}

const byNs = new Map();
for (const m of methods) {
  if (!byNs.has(m.ns)) byNs.set(m.ns, []);
  byNs.get(m.ns).push(m);
}
for (const list of byNs.values()) list.sort((a, b) => (a.method < b.method ? -1 : a.method > b.method ? 1 : 0));

// Clean previously generated files.
for (const dir of [PARAMS_DIR, RES_DIR]) {
  mkdirSync(dir, { recursive: true });
  for (const f of readdirSync(dir)) if (f.endsWith(".g.cs")) rmSync(join(dir, f));
}
mkdirSync(SHARED, { recursive: true });
mkdirSync(TEST_RES, { recursive: true });

for (const m of methods) {
  if (!m.params.length) continue;
  writeFileSync(join(PARAMS_DIR, `${m.className}.g.cs`), paramsClassSrc(m));
}

const flatNs = [...byNs.keys()].filter((ns) => !ns.startsWith("western")).sort();
const westernChildNs = [...byNs.keys()].filter((ns) => ns.startsWith("western.")).sort();
const groups = []; // [ns, cls]
for (const ns of flatNs) {
  const cls = resourceClass(ns);
  writeFileSync(join(RES_DIR, `${cls}.g.cs`), resourceSrc(cls, `${NS_DOC[ns] ?? ns} Use \`client.${pascal(ns)}\`.`, byNs.get(ns), []));
  groups.push([ns, cls]);
}
const children = [];
for (const ns of westernChildNs) {
  const sub = ns.split(".")[1];
  const cls = resourceClass(ns);
  const lang = sub === "narrative" ? "Takes `Lang` for `En`, `Hi` and `Mr`." : "Answers in English.";
  const doc = `Western \`${sub}\` endpoints: \`client.Western.${pascal(sub)}\`. ${lang}`;
  writeFileSync(join(RES_DIR, `${cls}.g.cs`), resourceSrc(cls, doc, byNs.get(ns), []));
  children.push([pascal(sub), cls, doc]);
}
const direct = byNs.get("western") ?? [];
for (const [sub] of children) {
  if (direct.some((m) => m.method === sub)) throw new Error(`western.${sub} is both a sub-group and a method`);
}
writeFileSync(join(RES_DIR, "WesternResource.g.cs"), resourceSrc("WesternResource", NS_DOC.western, direct, children));
groups.push(["western", "WesternResource"]);
groups.sort((a, b) => (a[0] < b[0] ? -1 : 1));
writeFileSync(join(GEN, "LifeAstroClient.Resources.g.cs"), clientPartialSrc(groups));

// ---------------------------------------------------------------------------
// Samples.g.cs (tests and live sweep) and the resolved manifest
// ---------------------------------------------------------------------------
{
  const I = "        ";
  const out = [HEADER, "using System.Collections.Generic;", "using LifeAstroApi.Params;", "", "namespace LifeAstroApi.Samples", "{"];
  out.push("    /// <summary>A sample call of every method, with the inputs of tool/samples.json (as tool/node-goldens.mjs uses them).</summary>");
  out.push("    internal static partial class Samples", "    {");
  const bodies = [];
  for (const ns of [...byNs.keys()].sort()) {
    const fn = "Add" + pascal(ns.replace(/\./g, "-"));
    const lines = [`${I}private static void ${fn}(List<Sample> all)`, `${I}{`];
    for (const m of byNs.get(ns)) {
      const hasLang = m.params.some((p) => p.name === "lang");
      const chain = accessorChain(m.ns);
      let expr;
      if (!m.params.length) expr = `c.${chain}.${m.method}()`;
      else expr = `c.${chain}.${m.method}(${creation(m, sampleInput(m), hasLang ? ["Lang = lang"] : [])})`;
      lines.push(`${I}    all.Add(new Sample(${csString(`${m.ns}.${m.nodeMethod}`)}, ${csString(m.entry.path)}, ${csString(m.entry.shape)}, ${hasLang},`);
      lines.push(`${I}        (c, lang) => ${expr}));`);
    }
    lines.push(`${I}}`);
    bodies.push({ fn, lines });
  }
  out.push(`${I}/// <summary>Every method, sorted by key; <c>lang</c> is passed to the methods that take it (null for none).</summary>`);
  out.push(`${I}internal static IReadOnlyList<Sample> All()`, `${I}{`, `${I}    var all = new List<Sample>();`);
  for (const b of bodies) out.push(`${I}    ${b.fn}(all);`);
  out.push(`${I}    return all;`, `${I}}`);
  for (const b of bodies) out.push("", ...b.lines);
  out.push("    }", "}", "");
  writeFileSync(join(SHARED, "Samples.g.cs"), out.join("\n"));
}

const manifestOut = methods
  .map((m) => ({
    key: `${m.ns}.${m.nodeMethod}`,
    group: accessorChain(m.ns),
    resource: resourceClass(m.ns),
    method: m.method,
    path: m.entry.path,
    shape: m.entry.shape,
    cost: m.cost,
    params: m.params.length ? m.className : null,
    optionalParams: m.params.length > 0 && !m.params.some((p) => p.required),
    extras: (m.entry.extras ?? []).map((x) => ({ wire: x.wire, field: x.name ?? camelOf(x.wire), required: !!x.required })),
    fields: ordered(m.params).map((p) => ({
      name: p.name,
      property: prop(p),
      type: propType(p),
      required: p.required,
      kind: p.kind,
      ...(p.kind === "object" ? { prefix: p.prefix, place: p.place, window: p.window } : { wires: p.wires }),
    })),
  }))
  .sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
writeFileSync(join(TEST_RES, "manifest.json"), JSON.stringify({ node_entries: entries.length, methods: manifestOut }, null, 1) + "\n");

const paramClasses = methods.filter((m) => m.params.length).length;
console.log(
  `Generated ${groups.length} groups (+${westernChildNs.length} Western sub-groups): ` +
    `${methods.length} methods from ${entries.length} manifest entries, ${paramClasses} parameter classes.`,
);
console.log(renames.length ? `Renames forced by C#:\n  ${renames.join("\n  ")}` : "Renames forced by C#: none.");
