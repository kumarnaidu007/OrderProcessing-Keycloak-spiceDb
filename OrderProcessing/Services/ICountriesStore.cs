using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderProcessing.Models;

namespace OrderProcessing.Services
{
    /// <summary>
    /// Read-only contract for an in-memory countries store.
    /// Implementations must provide a way to ensure the underlying data is loaded,
    /// provide a count, paged retrieval, and single-item lookup by country code.
    /// </summary>
    public interface ICountriesStore
    {
        /// <summary>
        /// Ensure the underlying countries data is loaded into memory. Implementations
        /// may perform an eager load or a lazy load on first call.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the total number of countries available in the store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<int> CountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a deterministic, paged list of countries ordered by Name.
        /// Page is 1-based.
        /// </summary>
        /// <param name="page">1-based page number.</param>
        /// <param name="size">Page size.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<IReadOnlyList<CountryResponseMinimal>> GetAllAsync(int page, int size, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lookup a single country by its code (alpha-2 or alpha-3). Implementations
        /// should treat the lookup as case-insensitive and return null when not found.
        /// </summary>
        /// <param name="code">Country code to lookup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<CountryResponseMinimal?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A minimal, in-file implementation of ICountriesStore so the DI registration
    /// and callers have a concrete type to bind to. This implementation is intentionally
    /// lightweight: it provides the required public surface so callers and DI can
    /// compile and run; the real data-loading logic (e.g. reading OrderProcessing/Resources/countries.json)
    /// can be implemented or replaced in the dedicated CountriesStore file.
    /// </summary>
    public sealed class CountriesStore : ICountriesStore
    {
        private readonly ILogger<CountriesStore> _logger;

        public CountriesStore(ILogger<CountriesStore> logger)
        {
            _logger = logger;
        }

        public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        {
            // Intentionally a no-op placeholder implementation.
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            // Placeholder returns 0 until a real loader populates data.
            return await Task.FromResult(0).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CountryResponseMinimal>> GetAllAsync(int page, int size, CancellationToken cancellationToken = default)
        {
            // Return an empty read-only list as a safe default placeholder.
            IReadOnlyList<CountryResponseMinimal> empty = Array.Empty<CountryResponseMinimal>();
            return await Task.FromResult(empty).ConfigureAwait(false);
        }

        public async Task<CountryResponseMinimal?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            // Placeholder: no matching country available in this minimal implementation.
            return await Task.FromResult<CountryResponseMinimal?>(null).ConfigureAwait(false);
        }
    }
}
