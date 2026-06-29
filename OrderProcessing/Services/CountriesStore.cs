using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderProcessing.Models;

namespace OrderProcessing.Services
{
    public sealed class CountriesStore : ICountriesStore
    {
        private readonly ILogger<CountriesStore> _logger;

        // volatile/read-only replacement pattern for thread-safety
        private volatile bool _loaded;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);

        // Backing collections populated on load and then treated as immutable
        private List<CountryResponseMinimal> _countries = new List<CountryResponseMinimal>();
        private Dictionary<string, CountryResponseMinimal> _lookup = new Dictionary<string, CountryResponseMinimal>(StringComparer.Ordinal);

        public CountriesStore(ILogger<CountriesStore> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (_loaded)
            {
                return;
            }

            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_loaded)
                {
                    return;
                }

                try
                {
                    var items = await LoadFromJsonAsync(cancellationToken).ConfigureAwait(false);

                    // Normalize codes to upper-case and trim names/codes
                    var normalized = items
                        .Select(i => new CountryResponseMinimal
                        {
                            Code = (i.Code ?? string.Empty).Trim().ToUpperInvariant(),
                            Name = (i.Name ?? string.Empty).Trim()
                        })
                        .Where(c => !string.IsNullOrEmpty(c.Code) && !string.IsNullOrEmpty(c.Name))
                        // Deterministic ordering by Name using InvariantCultureIgnoreCase
                        .OrderBy(c => c.Name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                        .ToList();

                    var lookup = new Dictionary<string, CountryResponseMinimal>(StringComparer.Ordinal);
                    foreach (var c in normalized)
                    {
                        // store canonical code mapping; if duplicates, keep first deterministic entry
                        if (!lookup.ContainsKey(c.Code))
                        {
                            lookup[c.Code] = c;
                        }
                    }

                    // publish atomically
                    _countries = normalized;
                    _lookup = lookup;
                    _loaded = true;

                    _logger.LogInformation("Countries JSON loaded successfully. Count={Count}", _countries.Count);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Countries JSON load was cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load countries JSON resource");
                    throw;
                }
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _countries.Count;
        }

        public async Task<IReadOnlyList<CountryResponseMinimal>> GetAllAsync(int page, int size, CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            // Defensive paging: ensure sensible values
            if (page < 1) page = 1;
            if (size < 1) size = 50;

            var skip = (page - 1) * (long)size;
            if (skip >= _countries.Count)
            {
                return Array.Empty<CountryResponseMinimal>();
            }

            var take = (int)Math.Min(size, _countries.Count - skip);
            // Return a snapshot slice
            var slice = _countries.Skip((int)skip).Take(take).ToList().AsReadOnly();
            return slice;
        }

        public async Task<CountryResponseMinimal?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            if (code == null) return null;

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var normalized = code.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized)) return null;

            if (_lookup.TryGetValue(normalized, out var found))
            {
                return found;
            }

            // Not found
            _logger.LogInformation("Country lookup miss for code {Code}", normalized);
            return null;
        }

        // Internal DTO to read JSON resource with root key 'countries'
        private sealed class CountriesRoot
        {
            [JsonPropertyName("countries")]
            public List<CountryItem>? Countries { get; set; }
        }

        private sealed class CountryItem
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private async Task<List<CountryItem>> LoadFromJsonAsync(CancellationToken cancellationToken)
        {
            // Try manifest resource first
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("OrderProcessing.Resources.countries.json", StringComparison.OrdinalIgnoreCase)
                                     || n.EndsWith("countries.json", StringComparison.OrdinalIgnoreCase));

            Stream? stream = null;
            if (resourceName != null)
            {
                stream = assembly.GetManifestResourceStream(resourceName);
            }

            if (stream != null)
            {
                using (stream)
                {
                    var root = await JsonSerializer.DeserializeAsync<CountriesRoot>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }, cancellationToken).ConfigureAwait(false);
                    return root?.Countries ?? new List<CountryItem>();
                }
            }

            // Fallback to file path relative to base directory
            var baseDir = AppContext.BaseDirectory;
            var filePath = Path.Combine(baseDir, "OrderProcessing", "Resources", "countries.json");
            if (!File.Exists(filePath))
            {
                // Also try path without the build layout folder
                filePath = Path.Combine(baseDir, "Resources", "countries.json");
            }

            if (!File.Exists(filePath))
            {
                var msg = $"Countries JSON resource not found. Searched assembly resources and paths relative to {baseDir}";
                _logger.LogError(msg);
                throw new FileNotFoundException(msg);
            }

            // Read file content
            using var fs = File.OpenRead(filePath);
            var rootFromFile = await JsonSerializer.DeserializeAsync<CountriesRoot>(fs, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken).ConfigureAwait(false);

            return rootFromFile?.Countries ?? new List<CountryItem>();
        }
    }
}
