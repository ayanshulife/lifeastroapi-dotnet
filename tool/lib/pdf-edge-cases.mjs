// Inputs for tool/pdf-goldens.mjs that exercise what is easy to get wrong in a port: JavaScript truthiness
// against `??`, String(number), dates, key order, UTF-16 lengths, escaping and hostile branding.

import { API_DEFAULTS, BLANK, CALLERS, REPORT_BRANDING, WATERMARKS } from "./pdf-branding-vectors.mjs";

export const LOCALES = ["en", "hi", "mr", "te", "ta", "kn", "ml", "gu", "bn", "pa"];

export const BRANDING = {
  companyName: "Acme Astro",
  logoUrl: "https://cdn.example/acme/logo.png",
  primaryColor: "#6b21a8",
  secondaryColor: "#9333ea",
  footerText: "Acme Astro · confidential",
  watermarkText: "SAMPLE",
};

const ALL = [
  "KundliLite", "KundliDetailed", "KundliBrihad", "MatchMaking", "DashaAnalysis",
  "Numerology", "SadeSati", "MangalDosha", "Varshaphal", "Horoscope",
];

const GENERATED = [
  "2026-11-22T05:42:10.123456789Z", "2026-11-22T23:59:59+05:30", "2026-01-01T00:10:00-0700", "2024-02-29",
  "2026-02-30T00:00:00Z", "2026-11-22T24:00:00Z", "2026-13-40", "not a date", "2026-11-22 10:05:00Z",
  "0099-03-04T05:06:07Z", "", 0,
  "2026-11-22T10:05", "2026-11-22T10:05:59.999999999", "2026-11-22T24:00", "2026-11-22T25:00", "2026-11T10:05Z",
  "2026-11-22T10:05:00,5Z", true, "2026-11-22 10:05", "2026-11-22t10:05:59.5", "2026-11-22 24:00", "0099-03-04 05:06",
];

export function edgeCases() {
  const out = [];
  const add = (renderer, data, branding) => out.push({ renderer, data, branding });

  for (const renderer of ALL) {
    add(renderer, {});
    add(renderer, {}, { companyName: "  ", primaryColor: "red}body{display:none", logoUrl: "javascript:alert(1)" });
    add(renderer, { Locale: "xx", language: "xx" }, {
      companyName: "<b>\"Acme\" & 'Co'</b> — Pune",
      logoUrl: " DATA:IMAGE/png;base64,AAAA ",
      primaryColor: " RGB(1, 2, 3) ",
      secondaryColor: "hsl(10deg 20% 30%)",
      footerText: "back\\slash </style> \"quote\"\nnext",
      watermarkText: "<DRAFT>\u0007\\",
    });
    for (const logoUrl of ["/logo.png", "//cdn.example/l.png", "file:///etc/passwd", "data:text/html,x"]) {
      if (renderer === "KundliLite") add(renderer, {}, { logoUrl, secondaryColor: "hsla(1,2%,3%,.5)" });
    }
  }
  for (const GeneratedAt of GENERATED) add("KundliLite", { GeneratedAt });
  for (const SolarReturnISO of GENERATED) add("Varshaphal", { SolarReturnISO });
  add("Varshaphal", { SolarReturnISO: "2027-01-15T16:39:17Z", SolarReturnLocal: "2027-01-15 22:09 IST" });
  add("Varshaphal", { SolarReturnISO: "2027-01-15T16:39:17Z", SolarReturnLocal: "" });
  for (const Subject of [undefined, {}, { Name: "" }, { Name: 0 }, { Name: "From Subject" }]) {
    for (const name of ["Arjun Mehta", "", 7]) add("Numerology", { Subject, name, driver: 6 });
  }
  add("KundliLite", {}, {
    logoUrl: "https://cdn.example/a,,b->c—d.png?x=1&y=2",
    secondaryColor: "rgb(1 2 3 / 50%)",
    watermarkText: "A/*B*/C",
  });
  // One to twelve planets in every cell of the chart: each layout, and more than the last one holds.
  const names = ["Sun", "Moon", "Mars", "Rahu", "Saturn", "Mercury", "Jupiter", "Venus", "Ketu", "Uranus", "Neptune", "Pluto"];
  for (let count = 1; count <= names.length; count++) {
    const Planets = [];
    for (let house = 1; house <= 12; house++) {
      for (const name of names.slice(0, count)) Planets.push({ name, house_num: house });
    }
    add("KundliBrihad", { Ascendant: { sign: "Leo" }, Planets });
  }
  // Marathi spells five planets unlike Hindi: charts and glyph cards must still find them.
  const marathi = ["सूर्य", "चंद्र", "मंगळ", "बुध", "बृहस्पती", "शुक्र", "शनी", "राहू", "केतू"];
  add("KundliBrihad", {
    Locale: "mr",
    Ascendant: { sign: "सिंह" },
    Planets: marathi.map((name, i) => ({ name, house_num: (i % 4) + 1 })),
    DivisionalCharts: [{ varga: "D2", lagna_sign: "कर्क", planets: marathi.map((name, i) => ({ name, house: (i % 2) + 1 })) }],
  });
  // A locale that names something every JavaScript object has is English.
  for (const renderer of ALL) {
    const key = renderer === "Horoscope" ? "language" : "Locale";
    for (const name of Object.getOwnPropertyNames(Object.prototype)) add(renderer, { [key]: name });
  }
  add("KundliBrihad", {
    rajyoga_power: { percent: 5, planets: ["<img src=x>", "Sun — Moon"] },
    kp: { cusps: [{ house: 1 }], planet_significators: [{ key: "Su", values: ["<b>1</b>", 2] }] },
  });
  add("KundliBrihad", {
    Locale: "ml",
    Branding: { CompanyName: "From the report", FooterText: "Report footer" },
    Houses: [{ HouseNum: 1, Body: "a marker look-alike ⟦T7⟧ in the text" }],
  });

  add("KundliLite", {
    Branding: { CompanyName: "From the report", FooterText: " ", PrimaryColor: "#abc" },
    Subject: { Name: "अर्जुन → Mehta", BirthDate: 0, BirthTime: false, BirthPlace: ["Mumbai", null, 7] },
    Ascendant: { sign: "", pada: 0, nak_lord: 1e21, dms_within: -0.000001, nakshatra: 1e-7 },
    LagnaSummary: 0,
    Planets: [
      { name: "Sun", retro: "yes", house_num: 4.5, placement: null },
      { name: { odd: true }, retro: 0, house_num: 0.30000000000000004, placement: { pada: true } },
    ],
  }, { companyName: "Override" });

  for (const Locale of ["en", "hi", "ta", "pa"]) {
    add("KundliBrihad", {
      Locale,
      Subject: { Name: "Arjun Mehta" },
      Ascendant: { sign: Locale === "hi" ? "कुंभ" : "Leo" },
      Planets: [
        { name: "Sun", house_num: "5", retro: 1 },
        { name: "Moon", name_en: "Moon", house_num: 5 },
        { name: "Mars", house_num: 5 }, { name: "Mercury", house_num: 5 }, { name: "Ketu", house_num: 2 },
        { name: "Pluto", house_num: 13 }, { name: "Rahu", house_num: "" }, { name: "चंद्र", house_num: 12 },
      ],
      DivisionalCharts: [
        { varga: "D9", lagna_sign: "Pisces", planets: [{ name: "Venus", house: 2 }, { name: "Unknown", house: 3 }] },
        { lagna_sign: "no such sign", planets: [] },
      ],
      Houses: [{ HouseNum: 0, LordEN: "Mars", LordHI: "मंगल" }, { HouseNum: 12, LordSign: "Aries", Body: "the native — wins" }],
      CurrentDasha: { Lord: "Venus", DurationY: 0 },
      Doshas: { Mangal: { Severity: "high" }, Pitra: null },
      kp: {
        cusps: [{ house: 1 }],
        planet_significators: [{ key: "Su", values: [1, 5, null, 2.5] }],
        ruling_planets: { day_lord: "Sun", 10: "ten", 2: "two", "-1": "minus", moon_sign: "" },
      },
      ghata_chakra: { prahar: 0, weekday: "" },
      shadbala: [
        { planet: "A", total_rupas: 1.005 }, { planet: "B", total_rupas: 2.675 }, { planet: "C", total_rupas: -0.001 },
        { planet: "D" }, { planet: "E", total_rupas: 1e21 }, { planet: "F", total_rupas: 0.125 },
      ],
      bhava_bala: [{ house: 1, total: 8.345, bhavadhipati: -2.5 }],
      ashtakavarga: { sav: [28, 0, 31], sav_total: 0 },
      rajyoga_power: { percent: 0, planets: ["Su", "Mo"] },
      mudda_timeline: { year: 0, periods: [{ lord: "Sun", do: "Walk", areas: [{ text: "" }, { text: "Go" }] }] },
      varshaphal: [{ year: 2027, panchavargeeya: [{ vishwa: 12.25 }] }, { year: 2028 }],
      narrative_chapters: [
        { key: "lagna", title: "", sections: [{ Body: "The native leads.", Bullets: ["native's luck"] }] },
        { key: "yogas", title: "Yogas", sections: [{ Heading: "Gaja", Body: "x" }] },
        { key: "lagna", title: "Last one wins", sections: [{ Body: "y" }] },
      ],
      Yogas: [{ NameEN: "Gaja Kesari", NameHI: "गजकेसरी", Effect: "a -> b" }],
    });
    add("MangalDosha", { Locale, HasDosha: true, HousesOccupied: [1, -4, 0, "7", 12], MarsPlacement: { sign: "Leo" } });
    add("MangalDosha", { Locale, HasDosha: true, Status: "purn", Cancelled: true, CancelReasons: ["own sign"] });
    add("MatchMaking", {
      Locale,
      Ashtakoota: { Total: 24, Max: 36, Verdict: "fair", Kootas: [{ name: "Nadi", score: 0, Max: 8 }] },
      MangalDoshaBoy: { Computed: true, HasDosha: true, Type: "other", HousesOccupied: [-7, 1], Cancelled: true },
      NadiDosha: { Severity: "exempted", Exceptions: ["same rashi", null] },
    });
    add("SadeSati", {
      Locale,
      CurrentStatus: { status: "peak", moon_sign: "Leo" },
      UpcomingWindows: [{ DurationYears: 2.25 }, { DurationYears: "3" }, { DurationYears: 7.45, SignsTraversed: ["A", "B"] }],
      PastWindows: [],
    });
    for (const house of [0, 1, 6, 9, 13, "4"]) add("Varshaphal", { Locale, TargetYear: 2027, Muntha: { house } });
    add("Numerology", { Locale, driver: 1, conductor: 0, soul: "7", driver_factors: { planet: "Sun", day: "Sunday" } });
    add("DashaAnalysis", { Locale, ActivePath: {}, AllMahadashas: [{ DurationY: 0, Lord: "Moon" }], YoginiDashas: [{ Lord: "Mangala" }] });
    add("Horoscope", {
      language: Locale, period: "weekly", rashi: "leo", lucky: { number: 0 }, panchang: { tithi: "Panchami" },
      slow_movers: [{ planet: "Saturn", current_sign: "Pisces", body: "a–b" }],
    });
  }
  for (const renderer of ALL) {
    add(renderer, { Branding: REPORT_BRANDING }, BLANK); // only white space is not set
    add(renderer, { Branding: API_DEFAULTS });
    add(renderer, {}, { primaryColor: "#ffe14d", secondaryColor: "hsla(60, 100%, 80%, .5)" }); // too pale to read
    add(renderer, {}, { companyName: "Acme — Astro -> Pune", footerText: "© Acme — <private> & 'co'" });
  }
  for (const watermarkText of WATERMARKS) add("KundliLite", {}, { watermarkText });
  for (const caller of CALLERS) {
    for (const Branding of [undefined, API_DEFAULTS, REPORT_BRANDING]) {
      add("KundliBrihad", { Branding }, caller ?? undefined);
      add("KundliBrihad", { Branding }, { ...caller, watermarkText: "SAMPLE" });
    }
  }
  const categories = [
    "graha_shanti", "charity", "a_b_c", "a__b", "_a", "a_", "Graha_shanti", "graha_shanti\n", "ijtihad", "\u00e9t\u00e9",
    "graha shanti", "constructor", "gr\u0131ha", "",
  ];
  for (const Locale of ["en", "hi"]) {
    add("KundliBrihad", {
      Locale,
      Remedies: categories.map((Category) => ({ Title: "T", Category, Description: "D" })),
      maitri: {
        order: ["Sun", "Moon", "constructor", 3],
        tatkalik: [{ planet: "Sun", relations: { Sun: "TempFriend", Moon: "TempEnemy", constructor: "toString", 3: "Neutral" } }, { planet: "Moon" }],
      },
    });
  }
  for (const rows of [0, 1, 2, 3]) {
    add("KundliBrihad", {
      rajyoga_power: { percent: 50, swarnim_kaal: Array.from({ length: rows }, () => ({ lord: "Moon" })) },
      ishta_devata: { deity: "Vishnu", body: rows % 2 ? "A few words." : "" },
    });
  }
  // A field that should be a list and is not: a lone value prints as it is, anything else loses that block.
  for (const v of ["1, 5", { a: 1 }, 7, 0, -2.5, true, false, null, "", [], ["1", 5, null, [2, 3]]]) {
    add("KundliBrihad", {
      narrative_chapters: v,
      DivisionalCharts: [{ varga: "D9", lagna_sign: "Leo", planets: v }],
      Ascendant: { sign: "Leo" },
      gems: { stones: v },
      chalit: { houses: [{ house: 1 }], planets: v },
      kp: { cusps: [{ house: 1 }], planet_significators: [{ key: "Sun", values: v }] },
      jaimini: { charakarakas: v, arudha_padas: v },
      lal_kitab: { planets: v, rins: v },
      ashtakavarga: { sav: [28, 30], prastar: v },
      varshaphal: [{ year: 2027, sahams: v, panchavargeeya: v }],
      mudda_timeline: { periods: [{ lord: "Sun", areas: v }] },
      rajyoga_power: { percent: 5, planets: v, swarnim_kaal: v },
    });
    add("KundliBrihad", { kp: { cusps: [{ house: 1 }], planet_significators: v } });
    for (const Locale of ["en", "ta"]) {
      add("MatchMaking", { Locale, MangalDoshaBoy: { Computed: true, HasDosha: true, Type: "purn", HousesOccupied: v } });
      add("SadeSati", { Locale, CurrentStatus: { status: "peak", SignsTraversed: v }, UpcomingWindows: [{ SignsTraversed: v }] });
    }
  }
  const gaps = ["A", null, undefined, 3]; // join prints nothing for these
  add("KundliBrihad", { rajyoga_power: { planets: gaps }, kp: { cusps: [{ house: 1 }], planet_significators: [{ values: gaps }] } });
  // A null inside a list: the cases of the Node SDK's "a null inside a list", and lists whose places must hold.
  for (const [renderer, data] of [
    ["KundliBrihad", { Planets: [null] }],
    ["KundliBrihad", { Planets: ["x", null] }],
    ["KundliBrihad", { Planets: [7, null] }],
    ["KundliBrihad", { narrative_chapters: [{ key: "outlook" }, null] }],
    ["KundliDetailed", { Planets: [{}, null] }],
    ["DashaAnalysis", { AllMahadashas: [null] }],
    ["Horoscope", { slow_movers: [null] }],
    ["KundliBrihad", { Planets: [null, { name: "Sun", house_num: 1 }], Ascendant: { sign: "Leo" } }],
    ["KundliBrihad", { ashtakavarga: { sav: [28, null, 31] }, maitri: { order: ["Sun", null, "Moon"], naisargik: [null, { planet: "Sun", relations: { Sun: "Mitra" } }] } }],
    ["KundliBrihad", { gems: { stones: [[null], null, { stone: "x" }] }, rajyoga_power: { planets: [true, null, 0] } }],
    ["MangalDosha", { HasDosha: true, HousesOccupied: [7, null, -2], CancelReasons: [null] }],
    ["MatchMaking", { Ashtakoota: { Kootas: [null, { name: "Nadi", score: 0 }] }, MangalDoshaGirl: { Computed: true, HasDosha: true, HousesOccupied: [null, 1] } }],
    ["SadeSati", { UpcomingWindows: [null, { SignsTraversed: ["Aries", null] }], PastWindows: [null] }],
    ["KundliLite", { Planets: [null], Subject: { BirthPlace: [null, null] } }],
  ]) add(renderer, data);
  return out;
}
