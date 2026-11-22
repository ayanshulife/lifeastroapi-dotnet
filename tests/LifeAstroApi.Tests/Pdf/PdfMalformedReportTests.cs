using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Templates;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>A field that should be a list and is not, or a null inside a list, never stops a report.</summary>
    public class PdfMalformedReportTests
    {
        [Fact]
        public void EveryBrokenListRendersWhatNodeRenders()
        {
            JsonElement fixture = PdfFixtures.Read("lists.json");
            JsonElement[] inputs = PdfFixtures.Read("cases.json").GetProperty("inputs").EnumerateArray().ToArray();
            JsonElement[] edits = fixture.GetProperty("edits").EnumerateArray().ToArray();
            Assert.Equal(
                new[]
                {
                    "replace \"1, 5\"", "replace {\"a\":1}", "replace 7", "push null", "push \"x\"", "push 7",
                    "fill [null]", "fill [\"x\",null]", "fill [7,null]", "fill [[null]]",
                },
                edits.Select(edit => edit.GetProperty("how").GetString() + " " + edit.GetProperty("value").GetRawText()));

            var failed = new List<string>();
            var renderers = new HashSet<string>();
            int count = 0;
            foreach (JsonElement row in fixture.GetProperty("lists").EnumerateArray())
            {
                string renderer = row.GetProperty("renderer").GetString()!;
                JsonElement set = row.GetProperty("set");
                JsonElement report = PdfFixtures.With(
                    inputs[row.GetProperty("input").GetInt32()],
                    set.GetProperty("key").GetString()!,
                    set.GetProperty("value").GetString()!);
                JsonElement[] path = row.GetProperty("path").EnumerateArray().ToArray();
                renderers.Add(renderer);
                for (int i = 0; i < edits.Length; i++)
                {
                    JsonElement value = edits[i].GetProperty("value");
                    if (edits[i].GetProperty("how").GetString() == "push")
                    {
                        value = JsonSerializer.SerializeToElement(PdfFixtures.At(report, path).EnumerateArray().Append(value).ToArray());
                    }

                    string what = renderer + " " + row.GetProperty("path").GetRawText() + " " + edits[i].GetRawText();
                    count++;
                    try
                    {
                        string html = PdfFixtures.Renderers[renderer](PdfFixtures.Replace(report, path, value), null);
                        if (PdfFixtures.Sha256(html) != row.GetProperty("sha256")[i].GetString())
                        {
                            failed.Add(what + ": not the HTML of Node");
                        }
                    }
                    catch (ArgumentException e)
                    {
                        failed.Add(what + ": " + e.Message);
                    }
                }
            }

            Assert.Empty(failed);
            Assert.True(count >= 1000, count + " cases");
            Assert.Equal(10, renderers.Count);
        }

        [Fact]
        public void WithoutNullItemsIsNodes()
        {
            JsonElement rows = PdfFixtures.Read("lists.json").GetProperty("withoutNullItems");
            Assert.True(rows.GetArrayLength() > 25);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                Js value = JsJson.Convert(row.GetProperty("value"));
                string before = Show(value);
                Js result = Shared.withoutNullItems(value, Js.Undefined);

                string given = row.GetProperty("value").GetRawText();
                Assert.Equal(given + " -> " + Show(JsJson.Convert(row.GetProperty("result"))), given + " -> " + Show(result));
                Assert.Equal(before, Show(value)); // the caller's value is never edited
                if (row.GetProperty("same").GetBoolean())
                {
                    Assert.True(Js.StrictEquals(result, value) ? true : false, given + ": a copy where Node returns the value itself");
                }
            }
        }

        [Theory]
        [InlineData("KundliBrihad", "{\"Planets\":[null]}")]
        [InlineData("KundliBrihad", "{\"Planets\":[\"x\",null]}")]
        [InlineData("KundliBrihad", "{\"Planets\":[7,null]}")]
        [InlineData("KundliBrihad", "{\"narrative_chapters\":[{\"key\":\"outlook\"},null]}")]
        [InlineData("KundliDetailed", "{\"Planets\":[{},null]}")]
        [InlineData("DashaAnalysis", "{\"AllMahadashas\":[null]}")]
        [InlineData("Horoscope", "{\"slow_movers\":[null]}")]
        public async Task ANullInsideAListStopsNeitherTheHtmlNorThePdf(string renderer, string json)
        {
            using JsonDocument report = JsonDocument.Parse(json);
            Assert.Contains("</html>", PdfFixtures.Renderers[renderer](report.RootElement, null), StringComparison.Ordinal);
            var engine = new RecordingEngine();
            Assert.NotEmpty(await PdfFixtures.Printers[renderer](report.RootElement, null, null, engine, CancellationToken.None));
        }

        [Fact]
        public void ANullInsideAListIsSkippedWithoutEditingTheCallersReport()
        {
            using JsonDocument document = JsonDocument.Parse("{\"Planets\":[null,{\"name\":\"Sun\"}]}");
            Js data = JsJson.Convert(document.RootElement);
            string before = Show(data);
            string html = KundliBrihad.renderKundliBrihadHtml(data, Js.Undefined).Str();
            Assert.Equal(before, Show(data));
            Assert.Equal(PdfReports.RenderKundliBrihadHtml("{\"Planets\":[{\"name\":\"Sun\"}]}"), html);
        }

        private static string Show(Js value)
        {
            if (Js.IsArray(value))
            {
                return "[" + string.Join(",", value.Iterate().Select(Show)) + "]";
            }

            string type = Js.TypeOf(value).Str();
            if (type == "object" && !(Js.StrictEquals(value, Js.Null) ? true : false))
            {
                return "{" + string.Join(",", Js.ObjectKeys(value).Iterate().Select(key => key.Str() + ":" + Show(value.Get(key)))) + "}";
            }

            return type == "string" ? "'" + value.Str() + "'" : value.Str();
        }

        [Fact]
        public void ALoneValuePrintsWhereAListWasExpectedAndAnObjectPrintsNothing()
        {
            string html = PdfReports.RenderKundliBrihadHtml(
                "{\"kp\":{\"cusps\":[{\"house\":1}],\"planet_significators\":["
                + "{\"key\":\"Sun\",\"values\":\"1, 5\"},{\"key\":\"Moon\",\"values\":{}},{\"key\":\"Mars\",\"values\":7}]}}");
            Assert.Contains("<td>Sun</td><td>1, 5</td>", html, StringComparison.Ordinal);
            Assert.Contains("<td>Moon</td><td></td>", html, StringComparison.Ordinal);
            Assert.Contains("<td>Mars</td><td>7</td>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[object Object]", html, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("{\"gems\":{\"stones\":\"x\"}}")]
        [InlineData("{\"narrative_chapters\":\"x\"}")]
        [InlineData("{\"chalit\":{\"houses\":[{\"house\":1}],\"planets\":\"x\"}}")]
        [InlineData("{\"kp\":{\"cusps\":[{\"house\":1}],\"planet_significators\":\"x\"}}")]
        public async Task AMalformedListStopsNeitherTheHtmlNorThePdf(string json)
        {
            Assert.Contains("</html>", PdfReports.RenderKundliBrihadHtml(json), StringComparison.Ordinal);
            var engine = new RecordingEngine();
            Assert.NotEmpty(await PdfReports.KundliBrihadPdfAsync(json, engine: engine));
        }
    }
}
