using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeAstroApi
{
    /// <summary>
    /// The System.Text.Json settings the SDK maps responses with, for the generic methods
    /// (<c>client.Panchang.BasicAsync&lt;MyDay&gt;(...)</c>).
    /// </summary>
    /// <remarks>
    /// The API's JSON uses snake_case names (<c>sunrise_local</c>, <c>sign_index</c>). With these options
    /// a C# property <c>SunriseLocal</c> reads <c>sunrise_local</c> without an attribute; use
    /// <c>[JsonPropertyName("...")]</c> for anything else. Unknown JSON properties are ignored, so your
    /// classes only need the fields you use.
    /// </remarks>
    public static class LifeAstroJson
    {
        /// <summary>
        /// The default options (read-only): <see cref="JsonNamingPolicy.SnakeCaseLower"/> property names,
        /// case-insensitive matching, numbers also read from JSON strings, unknown properties ignored.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions { get; } = CreateReadOnly();

        /// <summary>A new, writable copy of <see cref="DefaultOptions"/>, to change and pass as <see cref="LifeAstroClientOptions.JsonSerializerOptions"/>.</summary>
        /// <returns>New options.</returns>
        public static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };

        private static JsonSerializerOptions CreateReadOnly()
        {
            JsonSerializerOptions options = CreateOptions();
            options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            options.MakeReadOnly();
            return options;
        }
    }
}
