using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LifeAstroApi.Samples;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>
    /// For every method, the .NET SDK sends the same path and query string as the Node.js SDK with the same
    /// inputs, without and with <c>lang=bn</c>. The Node side is recorded by <c>tool/node-goldens.mjs</c> into
    /// <c>Resources/node-requests.json</c> (rerun it after <c>node tool/generate.mjs</c>).
    /// </summary>
    public class NodeParityTests
    {
        private static readonly Lazy<Dictionary<string, (string Url, string? LangUrl)>> Golden = new Lazy<Dictionary<string, (string, string?)>>(() =>
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Resources", "node-requests.json");
            Assert.True(File.Exists(file), "node-requests.json missing; run `node tool/node-goldens.mjs`");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            var map = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
            foreach (JsonProperty p in doc.RootElement.GetProperty("methods").EnumerateObject())
            {
                string? langUrl = p.Value.TryGetProperty("lang_url", out JsonElement l) ? l.GetString() : null;
                map[p.Name] = (p.Value.GetProperty("url").GetString()!, langUrl);
            }

            return map;
        });

        public static IEnumerable<object[]> AllSamples() => Samples.Samples.All().Select(s => new object[] { s.Key });

        [Fact]
        public void GoldensCoverEveryMethod()
        {
            Assert.Equal(
                Samples.Samples.All().Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal),
                Golden.Value.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Theory]
        [MemberData(nameof(AllSamples))]
        public async Task SendsTheSameRequestAsNode(string key)
        {
            Sample s = Samples.Samples.All().Single(x => x.Key == key);
            Assert.True(Golden.Value.TryGetValue(key, out var g), "no Node golden for " + key);
            var stub = new StubHandler();
            using LifeAstroClient client = Fixtures.Client(stub);

            await s.Call(client, null);
            Assert.Equal(Normalize(s, g.Url), Normalize(s, stub.Last.PathAndQuery));
            if (s.HasLang)
            {
                Assert.NotNull(g.LangUrl);
                stub.Reset();
                await s.Call(client, Lang.Bn);
                Assert.Equal(Normalize(s, g.LangUrl!), Normalize(s, stub.Last.PathAndQuery));
            }
            else
            {
                Assert.Null(g.LangUrl);
            }
        }

        /// <summary>Location-less moments default to today's date, which differs between recording and today.</summary>
        private static string Normalize(Sample s, string url) =>
            s.Shape == "instant" ? Regex.Replace(url, "([?&])date=\\d{4}-\\d{2}-\\d{2}", "$1date=TODAY") : url;
    }

    /// <summary>
    /// The client has one method per entry of the Node SDK's manifest (323), reachable as
    /// <c>client.Group.MethodAsync(params)</c>, with a generic overload each.
    /// </summary>
    public class ManifestTests
    {
        private static JsonDocument Manifest()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Resources", "manifest.json");
            Assert.True(File.Exists(file), "manifest.json missing; run `node tool/generate.mjs`");
            return JsonDocument.Parse(File.ReadAllText(file));
        }

        [Fact]
        public void OneMethodPerNodeManifestEntry()
        {
            using JsonDocument manifest = Manifest();
            Assert.Equal(323, manifest.RootElement.GetProperty("node_entries").GetInt32());
            Assert.Equal(323, manifest.RootElement.GetProperty("methods").GetArrayLength());
            Assert.Equal(323, Samples.Samples.All().Count);
        }

        [Fact]
        public void EveryMethodIsReachableWithItsParameterClass()
        {
            using JsonDocument manifest = Manifest();
            using var client = new LifeAstroClient("dv_live_test");
            foreach (JsonElement m in manifest.RootElement.GetProperty("methods").EnumerateArray())
            {
                object target = client;
                foreach (string part in m.GetProperty("group").GetString()!.Split('.'))
                {
                    target = target.GetType().GetProperty(part)!.GetValue(target)!;
                }

                string name = m.GetProperty("method").GetString()!;
                Type[] args = m.GetProperty("params").ValueKind == JsonValueKind.Null
                    ? new[] { typeof(RequestOptions), typeof(System.Threading.CancellationToken) }
                    : new[] { typeof(LifeAstroClient).Assembly.GetType("LifeAstroApi.Params." + m.GetProperty("params").GetString(), throwOnError: true)!, typeof(RequestOptions), typeof(System.Threading.CancellationToken) };
                MethodInfo? method = target.GetType().GetMethod(name, 0, args);
                Assert.True(method != null, m.GetProperty("key").GetString());
                Assert.Equal(typeof(Task<JsonElement>), method!.ReturnType);
                Assert.Contains(target.GetType().GetMethods(), x => x.Name == name && x.IsGenericMethodDefinition);
            }
        }

        [Fact]
        public void TheResourceClassesHaveExactly323Methods()
        {
            var names = new HashSet<string>();
            foreach (Type group in ResourceTypes())
            {
                foreach (MethodInfo m in group.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.ReturnType == typeof(Task<JsonElement>))
                    {
                        Assert.EndsWith("Async", m.Name);
                        Assert.True(names.Add(group.Name + "." + m.Name), "two overloads of " + m.Name);
                    }
                }
            }

            Assert.Equal(323, names.Count);
        }

        [Fact]
        public void NoMethodNeededARename()
        {
            using JsonDocument manifest = Manifest();
            foreach (JsonElement m in manifest.RootElement.GetProperty("methods").EnumerateArray())
            {
                string node = m.GetProperty("key").GetString()!.Split('.').Last();
                Assert.Equal(char.ToUpperInvariant(node[0]) + node.Substring(1) + "Async", m.GetProperty("method").GetString());
            }

            using var client = new LifeAstroClient("dv_live_test");
            Assert.NotNull(typeof(LifeAstroApi.Resources.DashaResource).GetMethod("CharAsync", 0, new[] { typeof(LifeAstroApi.Params.DashaCharParams), typeof(RequestOptions), typeof(System.Threading.CancellationToken) }));
        }

        private static List<Type> ResourceTypes()
        {
            var types = new List<Type>();
            foreach (PropertyInfo p in typeof(LifeAstroClient).GetProperties())
            {
                if (p.PropertyType.Namespace == "LifeAstroApi.Resources")
                {
                    types.Add(p.PropertyType);
                    types.AddRange(p.PropertyType.GetProperties().Where(x => x.PropertyType.Namespace == "LifeAstroApi.Resources").Select(x => x.PropertyType));
                }
            }

            Assert.Equal(23, typeof(LifeAstroClient).GetProperties().Count(p => p.PropertyType.Namespace == "LifeAstroApi.Resources"));
            Assert.Equal(43, types.Count);
            return types;
        }
    }
}
