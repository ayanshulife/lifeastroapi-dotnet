// What JavaScript itself answers, for the tests of src/LifeAstroApi/Pdf/Runtime.

/** A small deterministic generator, so that the fixture never changes between runs. */
function random(seed) {
  let a = seed;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function runtimeVectors() {
  const next = random(20261122);
  const numbers = [
    "0", "-0", "1", "-1", "0.5", "1.5", "2.5", "-2.5", "0.25", "0.125", "1.005", "2.675", "8.345", "-0.001", "-0.005",
    "1e21", "999999999999999900000", "1e-7", "0.000001", "5e-324", "1.7976931348623157e308", "123456789012345680",
    "0.30000000000000004", "999999999999999", "-999999999999999.1", "19.076", "72.8777", "0.045", "0.95", "9.999",
  ];
  for (let i = 0; i < 400; i++) {
    const magnitude = Math.floor(next() * 12) - 4;
    numbers.push(String((next() - 0.3) * 10 ** magnitude));
    numbers.push(String(Math.round((next() - 0.2) * 100000) / 1000));
  }
  const parsed = [
    "", " ", "5", " 5 ", "-0", "+3", ".5", "5.", "1e3", "1E-2", "0x1F", "0o17", "0b101", "-0x1F", "Infinity",
    "-Infinity", "infinity", "1,5", "12abc", "NaN", "1e400", "-1e400", "١٢", "\u00A07\uFEFF",
  ];
  const dates = [
    "2026-11-22T05:42:10.123456789Z", "2026-11-22T05:42:10.1Z", "2026-11-22T10:00:00+05:30", "2026-11-22T10:00:00+0530",
    "2026-11-22T10:00:00-12:00", "2026-11-22 10:00:00Z", "2026-11-22t10:00:00z", "2026-11-22T10:00Z", "2026-11-22",
    "2026-11", "2026", "2026-02-30", "2026-04-31T12:00:00Z", "2026-11-22T24:00:00Z", "2026-13-01", "2026-11-32",
    "2026-11-22T10:00:60Z", "2026-04-30T25:00:00Z", " 2026-11-22T10:00:00Z", "2026-11-22T10:00:00+05", "soon", "",
    "2026-11-22T10:00:00,5Z", "2026-11T10:05Z", "2026T10:05Z", "2026-11T10:05+05:30",
    "0001-01-01T00:00:00Z", "1969-12-31T23:59:59.999Z", "1900-02-29T00:00:00Z", "2000-02-29T00:00:00Z", "9999-12-31T23:59:59Z",
  ];
  const spaces = [];
  for (let code = 0; code <= 0xffff; code++) {
    const c = String.fromCharCode(code);
    if (/\s/.test(c)) spaces.push(code);
    if (/\s/.test(c) !== (c.trim() === "")) throw new Error(`trim and \\s differ at ${code}`);
  }
  const halves = [
    0, -0, 0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 0.49999999999999994, -0.49999999999999994, -0.5000000000000001,
    4503599627370495.5, 4503599627370497, -4503599627370497, 1e21, -1e-20, 102.49999999999999, 154.5, -45.5,
    NaN, Infinity, -Infinity,
  ];
  for (let tenths = -20; tenths <= 40; tenths++) halves.push(tenths / 2);
  for (let i = 0; i < 300; i++) halves.push((next() - 0.2) * 1200);
  // Every coordinate the chart can compute before it rounds.
  for (const centre of [8, 9, 25, 50, 75, 91, 92]) {
    for (const d of [0, 0.2, 0.5, 0.7, 0.8, 1, 1.2, 1.5, 2.8, 3, 3.2, 3.9, 4.4, 4.9, 5.6, 6, 7.5, 8, 8.5, 8.6]) {
      halves.push((centre + d) * 10, (centre - d) * 10);
    }
  }
  return {
    prototypeKeys: Object.getOwnPropertyNames(Object.prototype),
    rounded: halves.map((value) => {
      const r = Math.round(value);
      return { value: Object.is(value, -0) ? "-0" : String(value), result: Object.is(r, -0) ? "-0" : String(r), tenth: String(r / 10) };
    }),
    numbers: numbers.map((text) => {
      const n = Number(text);
      return { text, string: String(n), fixed: [0, 1, 2].map((digits) => n.toFixed(digits)) };
    }),
    parsed: parsed.map((text) => ({ text, string: String(Number(text)) })),
    dates: dates.map((text) => {
      const d = new Date(text);
      if (Number.isNaN(d.getTime())) return { text, time: null };
      return {
        text,
        time: d.getTime(),
        parts: [d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), d.getUTCHours(), d.getUTCMinutes()],
      };
    }),
    spaces,
  };
}
