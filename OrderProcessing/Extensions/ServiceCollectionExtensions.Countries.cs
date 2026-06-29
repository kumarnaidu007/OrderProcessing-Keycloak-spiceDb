using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Json;
using OrderProcessing.Services;

namespace OrderProcessing.Extensions
{
    /// <summary>
    /// Extension methods to register Countries related services into the DI container.
    /// Keeps Program/Startup code small and testable. See Jira: SCRUM-6.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register ICountriesStore and related services.
        /// - Registers ICountriesStore as a singleton (CountriesStore) if not already registered.
        /// - Configures System.Text.Json options used by MVC and minimal APIs to preserve DTO casing.
        /// - Optionally registers a hosted service to eager-load the embedded JSON on startup.
        /// </summary>
        /// <param name="services">IServiceCollection to configure.</param>
        /// <param name="eagerLoad">If true, an IHostedService will call EnsureLoadedAsync on startup.</param>
        /// <returns>The original IServiceCollection for chaining.</returns>
        public static IServiceCollection AddCountriesServices(this IServiceCollection services, bool eagerLoad = false)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // Register the ICountriesStore singleton only once as required by DI rules for this feature.
            if (!services.Any(sd => sd.ServiceType == typeof(ICountriesStore)))
            {
                services.AddSingleton<ICountriesStore, CountriesStore>();
            }

            // Ensure JSON serializer preserves DTO property casing (no camel-casing policy applied).
            // Configure for MVC controllers
            services.Configure<JsonOptions>(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = null;
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
            });

            // Configure for minimal APIs (System.Text.Json options used by the HTTP layer)
            services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(opts =>
            {
                opts.SerializerOptions.PropertyNamingPolicy = null;
                opts.SerializerOptions.PropertyNameCaseInsensitive = false;
            });

            // Optionally register an eager loader hosted service, but avoid duplicate registration if already present.
            if (eagerLoad)
            {
                var alreadyRegistered = services.Any(sd => sd.ServiceType == typeof(IHostedService)
                    && sd.ImplementationType == typeof(CountriesStoreLoaderHostedService));

                if (!alreadyRegistered)
                {
                    // Register as singleton IHostedService so the host will invoke StartAsync on startup.
                    services.AddSingleton<IHostedService, CountriesStoreLoaderHostedService>();
                }
            }

            return services;
        }

        /// <summary>
        /// Hosted service that will call EnsureLoadedAsync on ICountriesStore during application startup.
        /// This is used when callers request eager loading of the embedded JSON file.
        /// </summary>
        internal sealed class CountriesStoreLoaderHostedService : IHostedService
        {
            private readonly IServiceProvider _provider;
            private readonly ILogger<CountriesStoreLoaderHostedService> _logger;

            public CountriesStoreLoaderHostedService(IServiceProvider provider, ILogger<CountriesStoreLoaderHostedService> logger)
            {
                _provider = provider ?? throw new ArgumentNullException(nameof(provider));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                try
                {
                    _logger.LogInformation("Countries eager-load startup: attempting to load embedded countries resource.");

                    // Create a scope to resolve the store (singleton will still be the same instance).
                    using var scope = _provider.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<ICountriesStore>();

                    await store.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

                    _logger.LogInformation("Countries eager-load startup: embedded countries resource loaded successfully.");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Countries eager-load startup was canceled before completion.");
                }
                catch (Exception ex)
                {
                    // Log failure; do not throw to avoid crashing host startup. Startup logging requirement satisfied.
                    _logger.LogError(ex, "Countries eager-load startup: failed to load embedded countries resource.");
                }
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
