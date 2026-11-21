using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using LifeAstroApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Extension methods for IServiceCollection live in this namespace by convention (as AddHttpClient does),
// so `services.AddLifeAstro(...)` needs no extra using.
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers <see cref="LifeAstroClient"/> in the service collection.
    /// <code>
    /// // appsettings.json: "LifeAstroApi": { "ApiKey": "dv_live_...", "Timeout": "00:00:30", "MaxRetries": 2 }
    /// builder.Services.AddLifeAstro(builder.Configuration.GetSection("LifeAstroApi"));
    ///
    /// app.MapGet("/panchang", (LifeAstroClient astro, CancellationToken ct) =>
    ///     astro.Panchang.BasicAsync(new PanchangBasicParams(19.076, 72.8777, "Asia/Kolkata") { Lang = Lang.Gu }, cancellationToken: ct));
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is registered as a singleton: it is immutable and thread-safe, and one instance serves the
    /// whole application. It sends its requests through the named <see cref="HttpClient"/>
    /// <see cref="HttpClientName"/> from <see cref="IHttpClientFactory"/>. Because the singleton keeps that
    /// client, its primary handler (on .NET 8 and later a <c>SocketsHttpHandler</c>) recycles pooled
    /// connections every 5 minutes, so DNS changes are still picked up, and the factory's handler rotation
    /// is switched off. The returned <see cref="IHttpClientBuilder"/> lets you add a proxy, a delegating
    /// handler or your own primary handler.
    /// </para>
    /// <para>
    /// Registration uses <c>TryAdd</c>: a <see cref="LifeAstroClient"/> you register yourself wins, whether
    /// it is registered before or after this call. Calling <c>AddLifeAstro</c> twice registers one client.
    /// </para>
    /// <para>
    /// The API key comes from <see cref="LifeAstroClientOptions.ApiKey"/>, else the <c>LIFEASTRO_API_KEY</c>
    /// environment variable; without either, resolving the client throws an
    /// <see cref="InvalidOperationException"/> that says so.
    /// </para>
    /// </remarks>
    public static class LifeAstroServiceCollectionExtensions
    {
        /// <summary>The name of the <see cref="HttpClient"/> the client sends its requests with.</summary>
        public const string HttpClientName = "LifeAstroApi";

        /// <summary>
        /// Registers <see cref="LifeAstroClient"/> with options bound from a configuration section:
        /// <c>ApiKey</c>, <c>BaseUrl</c>, <c>Timeout</c> (a <see cref="TimeSpan"/> such as <c>00:00:30</c>),
        /// <c>MaxRetries</c> and <c>DefaultHeaders</c>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The section, e.g. <c>builder.Configuration.GetSection("LifeAstroApi")</c>.</param>
        /// <returns>The builder of the named <see cref="HttpClient"/>, to configure it further.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static IHttpClientBuilder AddLifeAstro(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            services.Configure<LifeAstroClientOptions>(configuration);
            return AddCore(services);
        }

        /// <summary>Registers <see cref="LifeAstroClient"/> with options set in code.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Sets the options, e.g. <c>o =&gt; o.ApiKey = key</c>.</param>
        /// <returns>The builder of the named <see cref="HttpClient"/>, to configure it further.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static IHttpClientBuilder AddLifeAstro(this IServiceCollection services, Action<LifeAstroClientOptions> configure)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            services.Configure(configure);
            return AddCore(services);
        }

        /// <summary>
        /// Registers <see cref="LifeAstroClient"/> with options bound from a configuration section, then
        /// changed in code (for example an <c>OnResponse</c> hook, which configuration cannot hold).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The section, e.g. <c>builder.Configuration.GetSection("LifeAstroApi")</c>.</param>
        /// <param name="configure">Changes the bound options.</param>
        /// <returns>The builder of the named <see cref="HttpClient"/>, to configure it further.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static IHttpClientBuilder AddLifeAstro(this IServiceCollection services, IConfiguration configuration, Action<LifeAstroClientOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            IHttpClientBuilder builder = services.AddLifeAstro(configuration);
            services.Configure(configure);
            return builder;
        }

        private static IHttpClientBuilder AddCore(IServiceCollection services)
        {
            IHttpClientBuilder builder = services
                .AddHttpClient(HttpClientName)
                .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan) // the SDK times every call itself
                .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

            services.TryAddSingleton(sp => new LifeAstroClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                sp.GetRequiredService<IOptions<LifeAstroClientOptions>>().Value));
            return builder;
        }

        private static HttpMessageHandler CreatePrimaryHandler()
        {
#if NET8_0_OR_GREATER
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
#else
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }

            return handler;
#endif
        }
    }
}
