using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace LifeAstroApi.Pdf
{
    /// <summary>Report JSON as template values, with no reflection: what JSON.parse would give.</summary>
    internal static class JsJson
    {
        /// <summary>Far beyond a report, well short of what recursion can take on a 256 KB thread stack.</summary>
        internal const int MaxDepth = 128;

        internal static Js Convert(JsonElement element) => Convert(element, 0);

        private static Js Convert(JsonElement element, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new JsTypeException("JSON nested more than 128 levels deep");
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var o = new JsObject();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        // A repeated key keeps its first position and its last value, as in JSON.parse.
                        o.Set(property.Name, Convert(property.Value, depth + 1));
                    }

                    return Js.FromObject(o);
                case JsonValueKind.Array:
                    var list = new List<Js>(element.GetArrayLength());
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        list.Add(Convert(item, depth + 1));
                    }

                    return Js.FromArray(list);
                case JsonValueKind.String:
                    return Js.S(element.GetString());
                case JsonValueKind.Number:
                    return Js.N(element.TryGetDouble(out double value) ? value : JsText.ToNumber(element.GetRawText()));
                case JsonValueKind.True:
                    return Js.True;
                case JsonValueKind.False:
                    return Js.False;
                case JsonValueKind.Null:
                    return Js.Null;
                default:
                    return Js.Undefined;
            }
        }
    }
}
