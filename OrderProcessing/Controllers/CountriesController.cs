using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OrderProcessing.Helpers;
using OrderProcessing.Models;
using OrderProcessing.Services;

namespace OrderProcessing.Controllers
{
    [ApiController]
    [Route("api/v1/countries")]
    public sealed class CountriesController : ControllerBase
    {
        private readonly ICountriesStore _store;
        private readonly ILogger<CountriesController> _logger;

        public CountriesController(ICountriesStore store, ILogger<CountriesController> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// GET /api/v1/countries
        /// Returns a paged list of CountryResponseMinimal.
        /// </summary>
        [HttpGet]
        [Produces("application/json")]
        public async Task<IActionResult> GetAll([FromQuery] int? page, [FromQuery] int? size, CancellationToken cancellationToken)
        {
            string correlationId = EnsureCorrelationId();

            if (!CountryValidation.TryValidatePaging(page, size, out var validatedPage, out var validatedSize, out var pagingError))
            {
                _logger.LogWarning("Validation failed for paging parameters. page={Page} size={Size} correlationId={CorrelationId}", page, size, correlationId);

                var pd = new ProblemDetails
                {
                    Type = "https://example.com/probs/invalid-query-parameters",
                    Title = "Invalid query parameters",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = pagingError ?? "One or more query parameters are invalid.",
                    Instance = HttpContext.Request.Path + HttpContext.Request.QueryString
                };
                pd.Extensions["correlationId"] = correlationId;

                return BadRequest(pd);
            }

            try
            {
                await _store.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

                var totalItems = await _store.CountAsync(cancellationToken).ConfigureAwait(false);

                var items = await _store.GetAllAsync(validatedPage, validatedSize, cancellationToken).ConfigureAwait(false);

                var totalPages = validatedSize <= 0 ? 0 : (int)Math.Ceiling(totalItems / (double)validatedSize);

                var envelope = new
                {
                    items,
                    page = validatedPage,
                    size = validatedSize,
                    totalItems,
                    totalPages
                };

                _logger.LogInformation("Returned countries page {Page} size {Size} itemsReturned={Count} totalItems={Total} correlationId={CorrelationId}",
                    validatedPage, validatedSize, items?.Count ?? 0, totalItems, correlationId);

                // Echo correlation id header for clients/tracing
                if (!Response.Headers.ContainsKey("X-Correlation-Id"))
                {
                    Response.Headers.Add("X-Correlation-Id", correlationId);
                }

                return Ok(envelope);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while listing countries. correlationId={CorrelationId}", correlationId);

                var pd = new ProblemDetails
                {
                    Type = "https://example.com/probs/internal-error",
                    Title = "Internal Server Error",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = "An unexpected error occurred while processing the request.",
                    Instance = HttpContext.Request.Path + HttpContext.Request.QueryString
                };
                pd.Extensions["correlationId"] = correlationId;

                return StatusCode(StatusCodes.Status500InternalServerError, pd);
            }
        }

        /// <summary>
        /// GET /api/v1/countries/{code}
        /// Returns a single CountryResponseMinimal by code (2 or 3 letters).
        /// </summary>
        [HttpGet("{code}")]
        [Produces("application/json")]
        public async Task<IActionResult> GetByCode([FromRoute] string code, CancellationToken cancellationToken)
        {
            string correlationId = EnsureCorrelationId();

            if (!CountryValidation.TryNormalizeAndValidateCode(code, out var normalized, out var validationError))
            {
                _logger.LogWarning("Invalid country code format. rawValue={Raw} correlationId={CorrelationId}", code, correlationId);

                var pd = new ProblemDetails
                {
                    Type = "https://example.com/probs/invalid-route-parameter",
                    Title = "Invalid country code",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = validationError ?? "The specified country code is invalid.",
                    Instance = HttpContext.Request.Path + HttpContext.Request.QueryString
                };
                pd.Extensions["correlationId"] = correlationId;

                return BadRequest(pd);
            }

            try
            {
                await _store.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

                var country = await _store.GetByCodeAsync(normalized, cancellationToken).ConfigureAwait(false);

                if (country == null)
                {
                    _logger.LogInformation("Country not found for code {Code}. correlationId={CorrelationId}", normalized, correlationId);

                    var pd = new ProblemDetails
                    {
                        Type = "https://example.com/probs/not-found",
                        Title = "Country not found",
                        Status = StatusCodes.Status404NotFound,
                        Detail = $"Country with code '{normalized}' was not found.",
                        Instance = HttpContext.Request.Path + HttpContext.Request.QueryString
                    };
                    pd.Extensions["correlationId"] = correlationId;

                    return NotFound(pd);
                }

                _logger.LogInformation("Country found for code {Code}. correlationId={CorrelationId}", normalized, correlationId);

                if (!Response.Headers.ContainsKey("X-Correlation-Id"))
                {
                    Response.Headers.Add("X-Correlation-Id", correlationId);
                }

                return Ok(country);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while looking up country code {Code}. correlationId={CorrelationId}", normalized ?? code, correlationId);

                var pd = new ProblemDetails
                {
                    Type = "https://example.com/probs/internal-error",
                    Title = "Internal Server Error",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = "An unexpected error occurred while processing the request.",
                    Instance = HttpContext.Request.Path + HttpContext.Request.QueryString
                };
                pd.Extensions["correlationId"] = correlationId;

                return StatusCode(StatusCodes.Status500InternalServerError, pd);
            }
        }

        private string EnsureCorrelationId()
        {
            const string headerName = "X-Correlation-Id";
            if (Request.Headers.TryGetValue(headerName, out var values))
            {
                var first = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first!;
                }
            }

            var generated = Guid.NewGuid().ToString("D");
            // Do not modify request headers, but echo back to response
            if (!Response.Headers.ContainsKey(headerName))
            {
                Response.Headers.Add(headerName, generated);
            }

            return generated;
        }
    }
}
