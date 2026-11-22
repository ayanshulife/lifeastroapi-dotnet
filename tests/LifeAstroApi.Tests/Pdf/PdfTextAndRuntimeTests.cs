using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The text helpers, and the JavaScript semantics under the templates, against Node's own answers.</summary>
    public class PdfTextAndRuntimeTests
    {
        private static readonly Lazy<JsonElement> Runtime = new Lazy<JsonElement>(() => PdfFixtures.Read("runtime.json"));

        private static JsonElement Vectors(string name) => Runtime.Value.GetProperty(name);

        [Fact]
        public void TextHelpersAnswerLikeNode()
        {
            JsonElement rows = PdfFixtures.Read("text.json");
            Assert.True(rows.GetArrayLength() > 50);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string s = row.GetProperty("s").GetString()!;
                Assert.Equal(row.GetProperty("humanize").GetString(), PdfText.Humanize(s));
                Assert.Equal(row.GetProperty("esc").GetString(), PdfText.Escape(s));
                foreach (JsonProperty clean in row.GetProperty("clean").EnumerateObject())
                {
                    Assert.Equal(clean.Value.GetString(), PdfText.CleanNarrative(s, clean.Name));
                    Assert.Equal(PdfText.Escape(clean.Value.GetString()), PdfText.EscapeBody(s, clean.Name));
                }
            }
        }

        [Fact]
        public void TextHelpersTakeNull()
        {
            Assert.Equal(string.Empty, PdfText.Escape(null));
            Assert.Equal(string.Empty, PdfText.EscapeBody(null, null));
            Assert.Equal(string.Empty, PdfText.Humanize(null));
            Assert.Equal("you win", PdfText.CleanNarrative("the native win", null));
        }

        [Theory]
        [InlineData("en", "planet in the house of gains and steady work ")]
        [InlineData("ta", "உங்கள் ஜாதகத்தில் நல்ல பலன்கள் உள்ளன ")]
        [InlineData("hi", "जातक को कार्य में लाभ और धैर्य ")]
        public void CleanNarrativeIsLinearOnLongText(string locale, string phrase)
        {
            string text = string.Concat(Enumerable.Repeat(phrase, 1000000 / phrase.Length)); // no danda
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PdfText.CleanNarrative(text, locale);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "1 MB took " + watch.Elapsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("de-DE")]
        [InlineData("fa-IR")]
        public void NumbersPrintLikeJavaScript(string culture)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                foreach (JsonElement row in Vectors("numbers").EnumerateArray())
                {
                    string text = row.GetProperty("text").GetString()!;
                    double value = JsText.ToNumber(text);
                    Assert.Equal(row.GetProperty("string").GetString(), Js.N(value).Str());
                    int digits = 0;
                    foreach (JsonElement expected in row.GetProperty("fixed").EnumerateArray())
                    {
                        Assert.Equal(text + " -> " + expected.GetString(), text + " -> " + JsText.ToFixed(value, digits++));
                    }
                }

                foreach (JsonElement row in Vectors("parsed").EnumerateArray())
                {
                    string text = row.GetProperty("text").GetString()!;
                    Assert.Equal(text + " -> " + row.GetProperty("string").GetString(), text + " -> " + Js.N(JsText.ToNumber(text)).Str());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void RoundingIsJavaScripts()
        {
            foreach (JsonElement row in Vectors("rounded").EnumerateArray())
            {
                string text = row.GetProperty("value").GetString()!;
                double rounded = JsText.Round(JsText.ToNumber(text));
                string shown = rounded == 0 && double.IsNegative(rounded) ? "-0" : Js.N(rounded).Str();
                Assert.Equal(text + " -> " + row.GetProperty("result").GetString(), text + " -> " + shown);
                Assert.Equal(row.GetProperty("tenth").GetString(), Js.Divide(Js.N(rounded), Js.N(10)).Str());
            }
        }

        [Fact]
        public void ObjectPrototypeKeysAreJavaScripts()
        {
            string[] keys = Vectors("prototypeKeys").EnumerateArray().Select(k => k.GetString()!).ToArray();
            Assert.Equal(12, keys.Length);
            Assert.All(keys, key => Assert.True(Js.IsObjectPrototypeKey(Js.S(key)) ? true : false, key));
            Assert.All(
                new[] { "en", "pa", string.Empty, "ToString", "toString ", "prototype", "length" },
                key => Assert.False(Js.IsObjectPrototypeKey(Js.S(key)) ? true : false, key));
        }

        [Fact]
        public void ReplaceWithAStringPatternTakesTheFirstOnly()
        {
            Assert.Equal("a+b-c", Js.S("a-b-c").ReplaceText("-", "+").Str());
            Assert.Equal("abc", Js.S("abc").ReplaceText("-", "+").Str());
        }

        [Fact]
        public void DatesReadLikeJavaScript()
        {
            foreach (JsonElement row in Vectors("dates").EnumerateArray())
            {
                string text = row.GetProperty("text").GetString()!;
                var date = new JsDate(JsDate.Parse(text));
                JsonElement time = row.GetProperty("time");
                if (time.ValueKind == JsonValueKind.Null)
                {
                    Assert.True(double.IsNaN(date.Time), text);
                    continue;
                }

                Assert.Equal(time.GetDouble(), date.Time);
                double[] expected = row.GetProperty("parts").EnumerateArray().Select(p => p.GetDouble()).ToArray();
                Assert.Equal(expected, new[] { date.Year, date.Month - 1, date.Day, date.Hour, date.Minute });
            }
        }

        [Fact]
        public void WhiteSpaceIsJavaScripts()
        {
            var expected = new HashSet<int>(Vectors("spaces").EnumerateArray().Select(c => c.GetInt32()));
            // The \s of the translated patterns, taken from one of them.
            Js spaces = Js.Regex(
                "^[\\t\\n\\v\\f\\r \\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000\\uFEFF]\\z", false, string.Empty);
            var different = new List<int>();
            for (int code = 0; code <= char.MaxValue; code++)
            {
                bool space = expected.Contains(code);
                bool pattern = spaces.Test(Js.S(((char)code).ToString())) ? true : false;
                if (JsText.IsSpace((char)code) != space || pattern != space)
                {
                    different.Add(code);
                }
            }

            Assert.Empty(different);
        }

        [Fact]
        public void ObjectKeysListIntegerLikeKeysFirst()
        {
            Js value = Js.Table("b", "1", "10", "2", "a", "3", "2", "4", "-1", "5", "01", "6");
            Assert.Equal("2,10,b,a,-1,01", Js.ObjectKeys(value).Str());
        }

        [Fact]
        public void SplitKeepsTheEmptyPiecesAsJavaScriptDoes()
        {
            Js spaces = Js.Regex("[ ]+", false, string.Empty);
            string Pieces(string text) => Js.S(text).Split(spaces).Join(Js.S("|")).Str();
            Assert.Equal("a|b", Pieces("a   b"));
            Assert.Equal("|a|", Pieces(" a "));
            Assert.Equal(string.Empty, Pieces(string.Empty));
            Assert.Equal("1", Js.N(Js.S(string.Empty).Split(spaces).Iterate().Count()).Str());
        }

        [Fact]
        public void MathAndParsingAreJavaScripts()
        {
            Js nan = Js.N(double.NaN);
            Assert.Equal("Infinity", Js.Min().Str());
            Assert.Equal("-Infinity", Js.Max().Str());
            Assert.Equal("NaN", Js.Min(Js.N(1), nan, Js.N(-1)).Str());
            Assert.Equal("NaN", Js.Max(Js.N(1), nan, Js.N(7)).Str());
            Assert.Equal("-3", Js.Floor(Js.N(-2.5)).Str());
            Assert.Equal("255", Js.ParseInt(Js.S("fF"), Js.N(16)).Str());
            Assert.Equal("10", Js.ParseInt(Js.S(" 10px"), Js.N(10)).Str());
            Assert.Equal("NaN", Js.ParseInt(Js.S("g1"), Js.N(16)).Str());
        }

        [Fact]
        public void TruthinessAndNullishAreJavaScripts()
        {
            Js x = Js.S("x");
            Assert.Equal("x", (Js.N(0) || x).Str());
            Assert.Equal("x", (Js.S(string.Empty) || x).Str());
            Assert.Equal("x", (Js.N(double.NaN) || x).Str());
            Assert.Equal("0", (Js.N(0).NonNullish ?? x).Str());
            Assert.Equal(string.Empty, (Js.S(string.Empty).NonNullish ?? x).Str());
            Assert.Equal("x", (Js.Null.NonNullish ?? x).Str());
            Assert.Equal("[object Object]", (Js.Obj() || x).Str());
            Assert.Equal("0", (Js.N(0) && x).Str());
            Assert.Equal("1,,x", Js.Arr(Js.N(1), Js.Null, x).Str());
            Assert.Equal("0", Js.N(-0.0).Str());
            Assert.Equal(2d, double.Parse(Js.S(char.ConvertFromUtf32(0x1F600)).Get("length").Str(), CultureInfo.InvariantCulture));
        }
    }
}
