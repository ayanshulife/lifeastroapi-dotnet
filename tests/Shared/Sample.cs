using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace LifeAstroApi.Samples
{
    /// <summary>
    /// One generated sample call (see <c>Samples.g.cs</c>): a method with the inputs of
    /// <c>tool/samples.json</c>, and the language passed to it when it takes one. Shared by the tests
    /// (Node parity) and <c>tools/LiveSweep</c>.
    /// </summary>
    internal sealed class Sample
    {
        internal Sample(string key, string path, string shape, bool hasLang, Func<LifeAstroClient, Lang?, Task<JsonElement>> call)
        {
            Key = key;
            Path = path;
            Shape = shape;
            HasLang = hasLang;
            Call = call;
        }

        /// <summary>The Node.js SDK's <c>group.method</c>, e.g. <c>panchang.basic</c>.</summary>
        internal string Key { get; }

        /// <summary>The path template.</summary>
        internal string Path { get; }

        /// <summary>The manifest shape.</summary>
        internal string Shape { get; }

        /// <summary>Whether the method takes <c>Lang</c>.</summary>
        internal bool HasLang { get; }

        /// <summary>Calls the method; the language is ignored by methods without one.</summary>
        internal Func<LifeAstroClient, Lang?, Task<JsonElement>> Call { get; }

        public override string ToString() => Key;
    }
}
