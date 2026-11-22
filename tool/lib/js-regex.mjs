// A JavaScript regular expression (without the `u` flag) as a .NET pattern with the same matches, for
// tool/generate-pdf.mjs. The pattern uses no RegexOptions: case-insensitivity is spelled out, so neither
// the culture (tr-TR) nor .NET's own case tables (Kelvin sign ~ "k") can change a match.

/** JavaScript's `\s` (WhiteSpace + LineTerminator), as the inside of a character class. */
const SPACE = String.raw`\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF`;
const WORD = "A-Za-z0-9_";
const ANY = String.raw`[\u0000-\uFFFF]`;
const BOUNDARY = `(?:(?<=[${WORD}])(?![${WORD}])|(?<![${WORD}])(?=[${WORD}]))`;
const NOT_BOUNDARY = `(?:(?<=[${WORD}])(?=[${WORD}])|(?<![${WORD}])(?![${WORD}]))`;
const OUTSIDE = {
  s: `[${SPACE}]`,
  S: `[^${SPACE}]`,
  d: "[0-9]",
  D: "[^0-9]",
  w: `[${WORD}]`,
  W: `[^${WORD}]`,
  b: BOUNDARY,
  B: NOT_BOUNDARY,
};
const INSIDE = { s: SPACE, d: "0-9", w: WORD };
const KEPT_ESCAPE = /^[tnrvf]$/;
const LETTER = /^[A-Za-z]$/;

function otherCase(c) {
  return c === c.toUpperCase() ? c.toLowerCase() : c.toUpperCase();
}

/**
 * @param {string} source `RegExp#source`
 * @param {string} flags `RegExp#flags`
 * @returns {string} the .NET pattern
 */
export function toDotNetPattern(source, flags) {
  const fail = (why) => {
    throw new Error(`cannot translate /${source}/${flags}: ${why}`);
  };
  if (/[^gi]/.test(flags)) fail("only the g and i flags are supported");
  const fold = flags.includes("i");
  const single = (c, inClass) => {
    if (!fold) return c;
    if (LETTER.test(c)) return inClass ? c + otherCase(c) : `[${c}${otherCase(c)}]`;
    if (c.toUpperCase() !== c || c.toLowerCase() !== c) fail("a non-ASCII letter with case under the i flag");
    return c;
  };

  let out = "";
  let inClass = false;
  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (c === "\\") {
      const e = source[++i];
      if (e === "u" || e === "x") {
        const length = e === "u" ? 4 : 2;
        const hex = source.slice(i + 1, i + 1 + length);
        if (hex.length !== length || !/^[0-9a-fA-F]+$/.test(hex)) fail("a malformed escape");
        if (fold && LETTER.test(String.fromCharCode(parseInt(hex, 16)))) fail("an escaped letter under the i flag");
        out += `\\${e}${hex}`;
        i += length;
      } else if (KEPT_ESCAPE.test(e)) {
        out += `\\${e}`;
      } else if (/[A-Za-z0-9]/.test(e)) {
        const known = inClass ? INSIDE[e] : OUTSIDE[e];
        if (known === undefined) fail(`the escape \\${e}`);
        out += known;
      } else {
        out += e === "/" ? "/" : `\\${e}`;
      }
    } else if (inClass) {
      if (c === "]") {
        inClass = false;
        out += c;
      } else if (fold && LETTER.test(c) && source[i + 1] === "-" && LETTER.test(source[i + 2] ?? "")) {
        const last = source[i + 2];
        out += `${c}-${last}${otherCase(c)}-${otherCase(last)}`;
        i += 2;
      } else if (c === "[") {
        out += "\\[";
      } else {
        out += single(c, true);
      }
    } else if (c === "[") {
      const all = ["[\\s\\S]", "[\\S\\s]", "[\\d\\D]", "[\\w\\W]"].find((x) => source.startsWith(x, i));
      if (all) {
        out += ANY;
        i += all.length - 1;
        continue;
      }
      if (source.startsWith("[]", i) || source.startsWith("[^]", i)) fail("an empty class");
      inClass = true;
      out += c;
      if (source[i + 1] === "^") {
        out += "^";
        i++;
      }
    } else if (c === "(" && source[i + 1] === "?") {
      if (!":=!".includes(source[i + 2])) fail("a named group or lookbehind");
      out += source.slice(i, i + 3);
      i += 2;
    } else if (c === "$") {
      out += "\\z";
    } else if (c === ".") {
      out += String.raw`[^\n\r\u2028\u2029]`;
    } else {
      out += single(c, false);
    }
  }
  return out;
}

/**
 * The longest literal that every match must contain, or "". The runtime looks for it first, so the
 * patterns that backtrack (`[^।]*इन...`) cost nothing on the many texts that cannot match.
 */
export function requiredLiteral(source, flags) {
  if (flags.includes("i")) return "";
  let best = "";
  let run = "";
  let depth = 0;
  const end = () => {
    if (run.length > best.length) best = run;
    run = "";
  };
  for (let i = 0; i < source.length; i++) {
    let c = source[i];
    if (c === "[") {
      end();
      while (++i < source.length && source[i] !== "]") if (source[i] === "\\") i++;
      continue;
    }
    if (c === "\\") {
      c = source[++i];
      if (/[A-Za-z0-9]/.test(c)) {
        end();
        if (c === "u") i += 4;
        else if (c === "x") i += 2;
        continue;
      }
    } else if ("()|^$.+*?{}".includes(c)) {
      if (depth === 0 && c === "|") return "";
      if (c === "(") depth++;
      if (c === ")") depth--;
      if (c === "{") while (i < source.length && source[i] !== "}") i++;
      end();
      continue;
    }
    if (depth > 0) continue;
    const next = source[i + 1];
    // An optional character is not required.
    if (next === "?" || next === "*" || next === "{") {
      end();
      continue;
    }
    run += c;
    if (next === "+") end();
  }
  end();
  return best;
}
