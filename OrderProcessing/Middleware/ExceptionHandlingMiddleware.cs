using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace OrderProcessing.Middleware
{
    /// <summary>
    /// Global exception handling middleware that converts unhandled exceptions to RFC7807 ProblemDetails
    /// and ensures a correlation id is propagated and included in ProblemDetails.extensions.
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex).ConfigureAwait(false);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Ensure we have a correlation id: prefer incoming X-Correlation-Id header, otherwise generate one
            string correlationId = GetOrCreateCorrelationId(context);

            // Log the exception with correlation id for troubleshooting
            _logger.LogError(exception, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

            // Build ProblemDetails according to RFC7807 with required fields
            var problem = new ProblemDetails
            {
                Type = "about:blank",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred while processing the request. Please provide the correlation id to support.",
                Instance = context.Request?.Path.Value
            };

            // Include correlation id in extensions for tracing
            problem.Extensions["correlationId"] = correlationId;

            // Ensure response is RFC7807 JSON
            context.Response.Clear();
            context.Response.StatusCode = problem.Status.GetValueOrDefault(StatusCodes.Status500InternalServerError);
            context.Response.ContentType = "application/problem+json";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            // Serialize ProblemDetails to JSON and write to response
            var payload = JsonSerializer.Serialize(problem, jsonOptions);
            await context.Response.WriteAsync(payload).ConfigureAwait(false);
        }

        private static string GetOrCreateCorrelationId(HttpContext context)
        {
            const string headerName = "X-Correlation-Id";

            if (context.Request.Headers.TryGetValue(headerName, out StringValues values) && !StringValues.IsNullOrEmpty(values))
            {
                var val = values.ToString().Trim();
                if (!string.IsNullOrEmpty(val))
                {
                    // ensure header value is present in response for propagation
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Headers[headerName] = val;
                    }

                    return val;
                }
            }

            var generated = Guid.NewGuid().ToString("D");
            if (!context.Response.HasStarted)
            {
                context.Response.Headers[headerName] = generated;
            }

            return generated;
        }
    }

    /// <summary>
    /// Extension to register the ExceptionHandlingMiddleware in the pipeline.
    /// Example usage in Program.cs: app.UseExceptionHandling();
    /// </summary>
    public static class ExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            return app.UseMiddleware<ExceptionHandlingMiddleware>();
        }
    }
}
