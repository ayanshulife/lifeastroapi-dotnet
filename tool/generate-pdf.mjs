// Writes src/LifeAstroApi/Pdf/Generated/*.g.cs: the Node.js SDK's report templates (src/pdf/ of
// lifeastroapi-node, TypeScript) translated statement by statement to C#.
//
// The templates are written in a small subset of TypeScript (string templates, label tables, map / join).
// Translating them, rather than retyping them, keeps the Indic text code unit by code unit and the output
// byte-identical to Node. JavaScript semantics (truthiness, String(x), regex classes, key order) live in
// src/LifeAstroApi/Pdf/Runtime/. Anything outside that subset stops the script with file and line.
//
//   node tool/generate-pdf.mjs           write the files
//   node tool/generate-pdf.mjs --check   exit 1 when they are out of date
//   node tool/pdf-goldens.mjs            then re-record the test fixtures
//
// Environment: LIFEASTRO_NODE_SDK (default ../lifeastroapi-node). Needs that repo's `typescript`.
import { createRequire } from "node:module";
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { requiredLiteral, toDotNetPattern } from "./lib/js-regex.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const NODE_SDK = resolve(process.env.LIFEASTRO_NODE_SDK ?? join(ROOT, "..", "lifeastroapi-node"));
const SRC = join(NODE_SDK, "src", "pdf");
const OUT = join(ROOT, "src", "LifeAstroApi", "Pdf", "Generated");
const ts = createRequire(join(NODE_SDK, "package.json"))("typescript");
const K = ts.SyntaxKind;

// index.ts, render.ts and outline.ts (engines, outline) are hand-written C#; html.ts only re-exports.
const SOURCES = [
  "shared.ts",
  "locales.ts",
  "colors.ts",
  ...readdirSync(join(SRC, "templates"))
    .filter((f) => f.endsWith(".ts"))
    .sort()
    .map((f) => `templates/${f}`),
];
const classNameOf = (file) => basename(file, ".ts").replace(/^./, (c) => c.toUpperCase());

const program = ts.createProgram(
  SOURCES.map((f) => join(SRC, f)),
  {
    target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.NodeNext,
    moduleResolution: ts.ModuleResolutionKind.NodeNext,
    strict: true,
    noEmit: true,
    types: [],
    lib: ["lib.es2022.d.ts"],
  },
);
const checker = program.getTypeChecker();
const moduleOfFile = new Map(SOURCES.map((f) => [program.getSourceFile(join(SRC, f)), classNameOf(f)]));

/** Built-in methods: JavaScript name -> runtime method and its arity. */
const METHODS = new Map(
  Object.entries({
    join: ["Join", 1, 1],
    map: ["Map", 1, 1],
    filter: ["Filter", 1, 1],
    forEach: ["ForEach", 1, 1],
    find: ["Find", 1, 1],
    some: ["Some", 1, 1],
    push: ["Push", 1, 1],
    slice: ["Slice", 1, 2],
    indexOf: ["IndexOf", 1, 1],
    includes: ["Includes", 1, 1],
    startsWith: ["StartsWith", 1, 1],
    endsWith: ["EndsWith", 1, 1],
    charAt: ["CharAt", 1, 1],
    trim: ["Trim", 0, 0],
    toLowerCase: ["ToLowerCase", 0, 0],
    toUpperCase: ["ToUpperCase", 0, 0],
    split: ["Split", 1, 1],
    padStart: ["PadStart", 2, 2],
    charCodeAt: ["CharCodeAt", 1, 1],
    toFixed: ["ToFixed", 1, 1],
    toString: ["ToRadix", 1, 1],
    replace: ["Replace", 2, 2],
    exec: ["Exec", 1, 1],
    test: ["Test", 1, 1],
    get: ["MapGet", 1, 1],
    set: ["MapSet", 2, 2],
    getTime: ["GetTime", 0, 0],
    getUTCFullYear: ["GetUtcFullYear", 0, 0],
    getUTCMonth: ["GetUtcMonth", 0, 0],
    getUTCDate: ["GetUtcDate", 0, 0],
    getUTCHours: ["GetUtcHours", 0, 0],
    getUTCMinutes: ["GetUtcMinutes", 0, 0],
  }),
);

/** Built-in functions, called on a global. */
const STATICS = new Map(
  Object.entries({
    "Array.isArray": "IsArray",
    "Array.from": "ArrayFrom",
    "Object.keys": "ObjectKeys",
    "Object.values": "ObjectValues",
    "Object.entries": "ObjectEntries",
    "Number.isNaN": "IsNaN",
    "Math.sin": "Sin",
    "Math.cos": "Cos",
    "Math.round": "Round",
    "Math.min": "Min",
    "Math.max": "Max",
    "Math.floor": "Floor",
    "Math.pow": "Pow",
    "Math.sqrt": "Sqrt",
  }),
);

const BINARY = new Map([
  [K.EqualsEqualsEqualsToken, "StrictEquals"],
  [K.ExclamationEqualsEqualsToken, "StrictNotEquals"],
  [K.LessThanToken, "Less"],
  [K.LessThanEqualsToken, "LessOrEqual"],
  [K.GreaterThanToken, "Greater"],
  [K.GreaterThanEqualsToken, "GreaterOrEqual"],
  [K.PlusToken, "Add"],
  [K.MinusToken, "Subtract"],
  [K.AsteriskToken, "Multiply"],
  [K.SlashToken, "Divide"],
  [K.PercentToken, "Remainder"],
]);

// C# reserved words (a trailing underscore is added).
const RESERVED = new Set(
  `abstract as base bool break byte case catch char checked class const continue decimal default delegate do double
  else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is
  lock long namespace new null object operator out override params private protected public readonly ref return sbyte
  sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe
  ushort using virtual void volatile while`.split(/\s+/),
);
const safeName = (name) => (RESERVED.has(name) ? `${name}_` : name);

/** A C# string literal: printable text as it is, invisible characters escaped. Never normalised. */
function csString(value) {
  let out = '"';
  for (let i = 0; i < value.length; i++) {
    const c = value[i];
    const code = c.charCodeAt(0);
    if (c === '"') out += '\\"';
    else if (c === "\\") out += "\\\\";
    else if (c === "\n") out += "\\n";
    else if (c === "\r") out += "\\r";
    else if (c === "\t") out += "\\t";
    else if (code >= 0xd800 && code <= 0xdfff) out += `\\u${code.toString(16).toUpperCase()}`;
    else if (code !== 0x20 && /[\p{C}\p{Z}]/u.test(c)) out += `\\u${code.toString(16).toUpperCase().padStart(4, "0")}`;
    else out += c;
  }
  return `${out}"`;
}

const literalText = (n) => (ts.isStringLiteral(n) || ts.isNoSubstitutionTemplateLiteral(n) ? n.text : null);

function fail(node, why) {
  const file = node.getSourceFile();
  const { line } = file.getLineAndCharacterOfPosition(node.getStart());
  throw new Error(`${file.fileName}:${line + 1}: ${why}\n    ${node.getText().slice(0, 160)}`);
}

function symbolOf(id) {
  let symbol =
    ts.isShorthandPropertyAssignment(id.parent) && id.parent.name === id
      ? checker.getShorthandAssignmentValueSymbol(id.parent)
      : checker.getSymbolAtLocation(id);
  if (symbol && symbol.flags & ts.SymbolFlags.Alias) symbol = checker.getAliasedSymbol(symbol);
  return symbol;
}

/** A top-level declaration of the templates (`Shared.esc`), or null. */
function topLevel(declaration) {
  if (!declaration) return null;
  let statement = null;
  if (ts.isFunctionDeclaration(declaration)) statement = declaration;
  else if (ts.isVariableDeclaration(declaration)) statement = declaration.parent.parent;
  if (!statement || !ts.isSourceFile(statement.parent) || !moduleOfFile.has(statement.parent)) return null;
  return `${moduleOfFile.get(statement.parent)}.${safeName(declaration.name.text)}`;
}

/** Whether control cannot fall out of the end of this statement. */
function endsFlow(statement) {
  if (!statement) return false;
  if (ts.isReturnStatement(statement) || ts.isContinueStatement(statement)) return true;
  if (ts.isBlock(statement)) return endsFlow(statement.statements[statement.statements.length - 1]);
  if (ts.isIfStatement(statement)) return endsFlow(statement.thenStatement) && endsFlow(statement.elseStatement);
  return false;
}

class Translator {
  constructor(file) {
    this.file = file;
    this.source = program.getSourceFile(join(SRC, file));
    this.className = classNameOf(file);
    this.regexes = new Map();
    this.members = [];
    this.lambdaDepth = 0;
    this.newScope();
  }

  // ---- names ----

  /** C# does not let a nested block reuse a name of its method, so every local gets its own. */
  newScope() {
    this.locals = new Map();
    this.used = new Set();
    this.temporaries = 0;
  }

  declare(name) {
    const wanted = safeName(name.text);
    let unique = wanted;
    for (let i = 2; this.used.has(unique); i++) unique = `${wanted}${i}`;
    this.used.add(unique);
    this.locals.set(symbolOf(name), unique);
    return unique;
  }

  temporary() {
    return `t${++this.temporaries}_`;
  }

  identifier(id) {
    const symbol = symbolOf(id);
    const local = this.locals.get(symbol);
    if (local) return local;
    const declaration = symbol?.valueDeclaration;
    const top = topLevel(declaration);
    if (top) {
      if (ts.isFunctionDeclaration(declaration)) fail(id, "a declared function used as a value");
      return top;
    }
    if (id.text === "undefined") return "Js.Undefined";
    if (id.text === "Boolean") return "Js.BooleanFunction";
    if (id.text === "String") return "Js.StringFunction";
    return fail(id, "an unknown identifier");
  }

  regex(literal) {
    const at = literal.text.lastIndexOf("/");
    const { source, flags } = new RegExp(literal.text.slice(1, at), literal.text.slice(at + 1));
    const key = `${source}/${flags}`;
    if (!this.regexes.has(key)) {
      this.regexes.set(key, {
        name: `Regex${this.regexes.size + 1}`,
        pattern: toDotNetPattern(source, flags),
        global: flags.includes("g"),
        required: requiredLiteral(source, flags),
        comment: `/${literal.text.slice(1, at)}/${flags}`,
      });
    }
    return this.regexes.get(key).name;
  }

  // ---- expressions ----

  /** A property key as a C# argument: a raw string when it is a literal. */
  key(node) {
    const text = literalText(node);
    return text === null ? this.expr(node) : csString(text);
  }

  /** One piece of a string concatenation. */
  piece(node) {
    const text = literalText(node);
    return text === null ? `${this.expr(node)}.Str()` : csString(text);
  }

  template(node) {
    const parts = [];
    if (node.head.text) parts.push(csString(node.head.text));
    for (const span of node.templateSpans) {
      parts.push(this.piece(span.expression));
      if (span.literal.text) parts.push(csString(span.literal.text));
    }
    return `Js.Cat(${parts.join(", ")})`;
  }

  receiver(node) {
    const inner = this.expr(node.expression);
    if (node.flags & ts.NodeFlags.OptionalChain && !node.questionDotToken) {
      fail(node, "an optional chain that continues past `?.`");
    }
    return inner;
  }

  propertyName(name) {
    if (ts.isIdentifier(name) || ts.isStringLiteral(name)) return name.text;
    if (ts.isNumericLiteral(name)) return String(Number(name.text));
    return fail(name, "a computed property name");
  }

  objectLiteral(node) {
    const properties = node.properties;
    if (properties.length === 0) return "Js.Obj()";
    // Label tables become flat string arrays: much less IL and stack in the static constructor.
    if (properties.every((p) => ts.isPropertyAssignment(p) && literalText(p.initializer) !== null)) {
      const pairs = properties.map(
        (p) => `\n    ${csString(this.propertyName(p.name))}, ${csString(literalText(p.initializer))}`,
      );
      return `Js.Table(${pairs.join(",")})`;
    }
    const parts = properties.map((p) => {
      if (ts.isPropertyAssignment(p)) {
        return `\n    Js.Prop(${csString(this.propertyName(p.name))}, ${this.expr(p.initializer)})`;
      }
      if (ts.isShorthandPropertyAssignment(p)) {
        return `\n    Js.Prop(${csString(p.name.text)}, ${this.identifier(p.name)})`;
      }
      if (ts.isSpreadAssignment(p)) return `\n    Js.Spread(${this.expr(p.expression)})`;
      return fail(p, "an object member");
    });
    return `Js.Obj(${parts.join(",")})`;
  }

  arrayLiteral(node) {
    const elements = node.elements;
    if (elements.some(ts.isSpreadElement)) {
      const parts = elements.map((e) =>
        ts.isSpreadElement(e) ? this.expr(e.expression) : `Js.Arr(${this.expr(e)})`,
      );
      return `Js.Concat(${parts.join(", ")})`;
    }
    if (elements.length && elements.every((e) => literalText(e) !== null)) {
      return `Js.List(${elements.map((e) => csString(literalText(e))).join(", ")})`;
    }
    return `Js.Arr(${elements.map((e) => this.expr(e)).join(", ")})`;
  }

  lambda(node) {
    const argv = `args${++this.lambdaDepth}_`;
    const lines = [];
    node.parameters.forEach((p, i) => {
      let value = `Js.Arg(${argv}, ${i})`;
      if (p.dotDotDotToken) value = `Js.Rest(${argv}, ${i})`;
      else if (p.initializer) value = `(${value}).OrDefault(${this.expr(p.initializer)})`;
      lines.push(...this.bind(p.name, value));
    });
    if (ts.isBlock(node.body)) {
      const statements = node.body.statements;
      for (const s of statements) lines.push(...this.statement(s));
      if (!endsFlow(statements[statements.length - 1])) lines.push("return Js.Undefined;");
    } else {
      lines.push(`return ${this.expr(node.body)};`);
    }
    this.lambdaDepth--;
    return `Js.Fn(${argv} =>\n{\n${lines.join("\n")}\n})`;
  }

  call(node) {
    const callee = node.expression;
    const args = node.arguments;
    const values = () => args.map((a) => this.expr(a)).join(", ");
    if (node.questionDotToken || args.some(ts.isSpreadElement)) fail(node, "an optional or spread call");

    if (ts.isIdentifier(callee)) {
      const declaration = symbolOf(callee)?.valueDeclaration;
      const top = topLevel(declaration);
      if (top && ts.isFunctionDeclaration(declaration)) {
        const parameters = declaration.parameters;
        const variadic = parameters.some((p) => p.dotDotDotToken);
        const arity = parameters.length - (variadic ? 1 : 0);
        if (!variadic && args.length > arity) fail(node, "too many arguments");
        const given = args.map((a) => this.expr(a));
        while (given.length < arity) given.push("Js.Undefined");
        return `${top}(${given.join(", ")})`;
      }
      if (callee.text === "Number" && !top && args.length === 1) return `Js.ToNumberValue(${values()})`;
      if (callee.text === "String" && !top && args.length === 1) return `Js.ToStringValue(${values()})`;
      if (callee.text === "parseInt" && !top && args.length === 2) return `Js.ParseInt(${values()})`;
      return `${this.identifier(callee)}.Call(${values()})`;
    }

    if (ts.isPropertyAccessExpression(callee)) {
      if (callee.questionDotToken || callee.flags & ts.NodeFlags.OptionalChain) fail(node, "an optional method call");
      const name = callee.name.text;
      const target = callee.expression;
      if (name === "call" && target.getText() === "Object.prototype.hasOwnProperty" && args.length === 2) {
        return `Js.HasOwn(${values()})`;
      }
      if (ts.isIdentifier(target) && STATICS.has(`${target.text}.${name}`)) {
        return `Js.${STATICS.get(`${target.text}.${name}`)}(${values()})`;
      }
      const declaration = checker.getSymbolAtLocation(callee.name)?.declarations?.[0];
      // A function kept in one of the templates' own objects (SOUTH_PROSE.fromLagna).
      if (declaration && moduleOfFile.has(declaration.getSourceFile())) {
        return `${this.expr(target)}.Get(${csString(name)}).Call(${values()})`;
      }
      const method = METHODS.get(name);
      if (!method || args.length < method[1] || args.length > method[2]) fail(node, `the method ${name}`);
      if (name === "split" && !ts.isRegularExpressionLiteral(args[0])) fail(node, "split by anything but a regex");
      if (name === "replace") {
        const replacement = literalText(args[1]);
        if (replacement === null && !ts.isArrowFunction(args[1])) fail(node, "a computed replacement");
        const searched = literalText(args[0]);
        if (searched !== null) {
          // A string pattern: the first occurrence, as it is written (no groups).
          if (replacement === null || replacement.includes("$")) fail(node, "a replacement for a string pattern");
          return `${this.expr(target)}.ReplaceText(${csString(searched)}, ${csString(replacement)})`;
        }
        if (replacement !== null) {
          if (/\$(?![1-9&])|\$\d\d/.test(replacement.replaceAll("$&", ""))) {
            fail(node, "a replacement pattern other than $1..$9 and $&"); // $& is the whole match in .NET too
          }
          return `${this.expr(target)}.Replace(${this.expr(args[0])}, ${csString(replacement)})`;
        }
      }
      return `${this.expr(target)}.${method[0]}(${values()})`;
    }

    return `${this.expr(callee)}.Call(${values()})`;
  }

  construct(node) {
    const type = node.expression.getText();
    const args = node.arguments ?? [];
    if (type === "Date" && args.length === 1) return `Js.NewDate(${this.expr(args[0])})`;
    if (type === "Map" && args.length <= 1) return `Js.NewMap(${args.length ? this.expr(args[0]) : ""})`;
    return fail(node, "a constructor");
  }

  assign(node, compound) {
    const value = this.expr(node.right);
    const target = node.left;
    if (ts.isIdentifier(target)) {
      const name = this.identifier(target);
      if (name.includes(".")) fail(node, "an assignment to a module variable");
      return compound ? `${name} = Js.Add(${name}, ${value})` : `${name} = ${value}`;
    }
    if (compound) fail(node, "+= on a member");
    if (ts.isElementAccessExpression(target)) {
      return `${this.expr(target.expression)}.Set(${this.key(target.argumentExpression)}, ${value})`;
    }
    return fail(node, "an assignment target");
  }

  /** `name ??= value`: C#'s ?? evaluates the value only when it is needed, as JavaScript does. */
  assignIfNullish(node) {
    if (!ts.isIdentifier(node.left)) fail(node, "??= on a member");
    const name = this.identifier(node.left);
    if (name.includes(".")) fail(node, "an assignment to a module variable");
    return `${name} = (${name}.NonNullish ?? ${this.expr(node.right)})`;
  }

  binary(node) {
    const op = node.operatorToken.kind;
    if (op === K.EqualsToken) return this.assign(node, false);
    if (op === K.PlusEqualsToken) return this.assign(node, true);
    if (op === K.QuestionQuestionEqualsToken) return this.assignIfNullish(node);
    // Js defines true / false / & / |, so C#'s own operators short-circuit and return an operand, as JS does.
    if (op === K.BarBarToken) return `(${this.expr(node.left)} || ${this.expr(node.right)})`;
    if (op === K.AmpersandAmpersandToken) return `(${this.expr(node.left)} && ${this.expr(node.right)})`;
    if (op === K.QuestionQuestionToken) return `(${this.expr(node.left)}.NonNullish ?? ${this.expr(node.right)})`;
    if (op === K.InKeyword) {
      if (node.right.getText() !== "Object.prototype") fail(node, "`in` on anything but Object.prototype");
      return `Js.IsObjectPrototypeKey(${this.expr(node.left)})`;
    }
    const method = BINARY.get(op);
    if (!method) fail(node, "an operator");
    return `Js.${method}(${this.expr(node.left)}, ${this.expr(node.right)})`;
  }

  expr(node) {
    const text = literalText(node);
    if (text !== null) return `Js.S(${csString(text)})`;
    switch (node.kind) {
      case K.NumericLiteral:
        return `Js.N(${Number(node.text)}d)`;
      case K.TrueKeyword:
        return "Js.True";
      case K.FalseKeyword:
        return "Js.False";
      case K.NullKeyword:
        return "Js.Null";
      case K.Identifier:
        return this.identifier(node);
      case K.TemplateExpression:
        return this.template(node);
      case K.RegularExpressionLiteral:
        return this.regex(node);
      case K.ObjectLiteralExpression:
        return this.objectLiteral(node);
      case K.ArrayLiteralExpression:
        return this.arrayLiteral(node);
      case K.ArrowFunction:
        return this.lambda(node);
      case K.CallExpression:
        return this.call(node);
      case K.NewExpression:
        return this.construct(node);
      case K.BinaryExpression:
        return this.binary(node);
      case K.ConditionalExpression:
        return `(${this.expr(node.condition)} ? ${this.expr(node.whenTrue)} : ${this.expr(node.whenFalse)})`;
      case K.ParenthesizedExpression:
      case K.AsExpression:
      case K.NonNullExpression:
        return this.expr(node.expression);
      case K.TypeOfExpression:
        return `Js.TypeOf(${this.expr(node.expression)})`;
      case K.PrefixUnaryExpression:
        if (node.operator === K.ExclamationToken) return `(!${this.expr(node.operand)})`;
        if (node.operator === K.MinusToken) {
          return ts.isNumericLiteral(node.operand)
            ? `Js.N(-${Number(node.operand.text)}d)`
            : `Js.Negate(${this.expr(node.operand)})`;
        }
        return fail(node, "a prefix operator");
      case K.PropertyAccessExpression: {
        if (ts.isIdentifier(node.expression) && node.expression.text === "Math") {
          if (node.name.text === "PI") return "Js.Pi";
        }
        const method = node.questionDotToken ? "GetOptional" : "Get";
        return `${this.receiver(node)}.${method}(${csString(node.name.text)})`;
      }
      case K.ElementAccessExpression: {
        const method = node.questionDotToken ? "GetOptional" : "Get";
        return `${this.receiver(node)}.${method}(${this.key(node.argumentExpression)})`;
      }
      default:
        return fail(node, `the expression ${K[node.kind]}`);
    }
  }

  // ---- statements ----

  /** `const name = value`, also for the two destructuring forms. */
  bind(name, value) {
    if (ts.isIdentifier(name)) return [`Js ${this.declare(name)} = ${value};`];
    const temp = this.temporary();
    const lines = [`Js ${temp} = ${value};`];
    name.elements.forEach((e, i) => {
      if (ts.isOmittedExpression(e)) return;
      if (e.dotDotDotToken) fail(e, "a rest element");
      let from;
      if (ts.isArrayBindingPattern(name)) from = `${temp}.Get(Js.N(${i}d))`;
      else from = `${temp}.Get(${csString(this.propertyName(e.propertyName ?? e.name))})`;
      if (e.initializer) from += `.OrDefault(${this.expr(e.initializer)})`;
      lines.push(...this.bind(e.name, from));
    });
    return lines;
  }

  declarations(list) {
    return list.declarations.flatMap((d) =>
      this.bind(d.name, d.initializer ? this.expr(d.initializer) : "Js.Undefined"),
    );
  }

  body(statement) {
    const inner = ts.isBlock(statement) ? statement.statements : [statement];
    return ["{", ...inner.flatMap((s) => this.statement(s)), "}"];
  }

  ifStatement(node) {
    const lines = [`if (${this.expr(node.expression)})`, ...this.body(node.thenStatement)];
    const otherwise = node.elseStatement;
    if (!otherwise) return lines;
    if (ts.isIfStatement(otherwise)) {
      const [first, ...rest] = this.ifStatement(otherwise);
      return [...lines, `else ${first}`, ...rest];
    }
    return [...lines, "else", ...this.body(otherwise)];
  }

  switchStatement(node) {
    const clauses = node.caseBlock.clauses;
    const tested = clauses.filter(ts.isCaseClause).map((c) => c.expression);
    const numeric = tested.every(ts.isNumericLiteral);
    if (!numeric && tested.some((e) => literalText(e) === null)) fail(node, "cases that are not all strings or all numbers");
    const key = numeric ? "CaseNumber" : "CaseKey";
    const lines = [`switch (${this.expr(node.expression)}.${key}())`, "{"];
    clauses.forEach((clause, index) => {
      if (ts.isDefaultClause(clause)) {
        lines.push("default:");
      } else {
        const label = numeric ? `${Number(clause.expression.text)}d` : csString(literalText(clause.expression));
        lines.push(`case ${label}:`);
      }
      const inner = clause.statements;
      for (const s of inner) lines.push(...this.statement(s));
      if (inner.length && !endsFlow(inner[inner.length - 1])) {
        if (index < clauses.length - 1) fail(clause, "a fall-through case");
        lines.push("break;");
      }
    });
    lines.push("}");
    return lines;
  }

  forStatement(node) {
    const init = node.initializer;
    const step = node.incrementor;
    if (!init || !ts.isVariableDeclarationList(init) || init.declarations.length !== 1 || !node.condition || !step) {
      fail(node, "a for statement");
    }
    const first = this.declarations(init)[0].slice(0, -1);
    const condition = this.expr(node.condition);
    let next;
    if (ts.isPostfixUnaryExpression(step)) {
      const name = this.expr(step.operand);
      const method = step.operator === K.PlusPlusToken ? "Add" : "Subtract";
      next = `${name} = Js.${method}(${name}, Js.N(1d))`;
    } else {
      next = this.expr(step);
    }
    return [`for (${first}; ${condition}; ${next})`, ...this.body(node.statement)];
  }

  forOfStatement(node) {
    if (!ts.isVariableDeclarationList(node.initializer)) fail(node, "a for-of target");
    const over = this.expr(node.expression);
    const item = this.temporary();
    const bound = this.bind(node.initializer.declarations[0].name, item);
    const [open, ...rest] = this.body(node.statement);
    return [`foreach (Js ${item} in ${over}.Iterate())`, open, ...bound, ...rest];
  }

  statement(node) {
    switch (node.kind) {
      case K.VariableStatement:
        return this.declarations(node.declarationList);
      case K.ExpressionStatement: {
        const e = node.expression;
        if (ts.isVoidExpression(e)) return []; // `void name;` marks a variable as used
        if (!ts.isCallExpression(e) && !ts.isBinaryExpression(e)) fail(node, "a statement");
        return [`${this.expr(e)};`];
      }
      case K.IfStatement:
        return this.ifStatement(node);
      case K.SwitchStatement:
        return this.switchStatement(node);
      case K.ForStatement:
        return this.forStatement(node);
      case K.ForOfStatement:
        return this.forOfStatement(node);
      case K.ReturnStatement:
        return [`return ${node.expression ? this.expr(node.expression) : "Js.Undefined"};`];
      case K.ContinueStatement:
        return ["continue;"];
      case K.Block:
        return this.body(node);
      default:
        return fail(node, `the statement ${K[node.kind]}`);
    }
  }

  // ---- the module ----

  functionDeclaration(node) {
    this.newScope();
    const lines = [];
    const signature = node.parameters.map((p) => {
      if (!ts.isIdentifier(p.name)) fail(p, "a destructured parameter");
      const name = this.declare(p.name);
      if (p.dotDotDotToken) {
        lines.push(`Js ${name} = Js.Arr(${name}Args_);`);
        return `params Js[] ${name}Args_`;
      }
      if (p.initializer) lines.push(`${name} = ${name}.OrDefault(${this.expr(p.initializer)});`);
      return `Js ${name}`;
    });
    const statements = node.body.statements;
    for (const s of statements) lines.push(...this.statement(s));
    if (!endsFlow(statements[statements.length - 1])) lines.push("return Js.Undefined;");
    const name = safeName(node.name.text);
    this.members.push([`internal static Js ${name}(${signature.join(", ")})`, "{", ...lines, "}"].join("\n"));
  }

  translate() {
    for (const statement of this.source.statements) {
      switch (statement.kind) {
        case K.ImportDeclaration:
        case K.ExportDeclaration:
        case K.InterfaceDeclaration:
        case K.TypeAliasDeclaration:
          break;
        case K.FunctionDeclaration:
          this.functionDeclaration(statement);
          break;
        case K.VariableStatement:
          for (const d of statement.declarationList.declarations) {
            this.newScope();
            if (!ts.isIdentifier(d.name) || !d.initializer) fail(d, "a module variable");
            this.members.push(`internal static readonly Js ${safeName(d.name.text)} = ${this.expr(d.initializer)};`);
          }
          break;
        default:
          fail(statement, `the module member ${K[statement.kind]}`);
      }
    }
    // Static fields initialise in textual order, so the regexes come first.
    const regexes = [...this.regexes.values()].map(
      (r) =>
        `// ${r.comment.replace(/[\u0000-\u001F\u2028\u2029]/g, " ")}\n` +
        `private static readonly Js ${r.name} = Js.Regex(${csString(r.pattern)}, ${r.global}, ${csString(r.required)});`,
    );
    return `// <auto-generated>
//     Written by tool/generate-pdf.mjs from the Node.js SDK's src/pdf/${this.file}.
//     Do not edit: run \`node tool/generate-pdf.mjs\` again.
// </auto-generated>
#pragma warning disable CS0162 // unreachable code: the source's control flow is kept as it is

namespace LifeAstroApi.Pdf.Templates
{
    internal static class ${this.className}
    {
${indent([...regexes, ...this.members].join("\n\n"))}
    }
}
`;
  }
}

/** Indents by braces: the code carries no strings with real line breaks, so a line scan is enough. */
function indent(code) {
  let depth = 2;
  return code
    .split("\n")
    .map((line) => {
      if (line === "") return line;
      const closes = line.startsWith("}");
      if (closes) depth--;
      const out = "    ".repeat(Math.max(0, depth)) + line;
      if (line === "{") depth++;
      return out;
    })
    .join("\n");
}

const outputs = SOURCES.map((file) => [`${classNameOf(file)}.g.cs`, new Translator(file).translate()]);

if (process.argv.includes("--check")) {
  const stale = outputs.filter(([name, code]) => {
    const path = join(OUT, name);
    return !existsSync(path) || readFileSync(path, "utf8").replace(/\r\n/g, "\n") !== code;
  });
  const known = new Set(outputs.map(([name]) => name));
  const extra = existsSync(OUT) ? readdirSync(OUT).filter((f) => !known.has(f)) : [];
  if (stale.length || extra.length) {
    console.error(`Out of date: ${[...stale.map(([name]) => name), ...extra].join(", ")}. Run node tool/generate-pdf.mjs.`);
    process.exit(1);
  }
  console.log(`${outputs.length} generated PDF template files are up to date.`);
} else {
  mkdirSync(OUT, { recursive: true });
  for (const [name, code] of outputs) writeFileSync(join(OUT, name), code);
  console.log(`${outputs.length} files written to src/LifeAstroApi/Pdf/Generated.`);
}
