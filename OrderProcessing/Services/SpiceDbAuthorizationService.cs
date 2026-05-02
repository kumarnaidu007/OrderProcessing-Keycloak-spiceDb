using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderProcessing.Common;

namespace OrderProcessing.Services;

public interface ISpiceDbAuthorizationService
{
    Task<bool> CheckOrderPermissionAsync(string subjectId, long orderId, string permission, CancellationToken ct);
    Task WriteOrderOwnerAsync(string subjectId, long orderId, CancellationToken ct);
}

public sealed class SpiceDbAuthorizationService : ISpiceDbAuthorizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SpiceDbOptions _options;
    private readonly ILogger<SpiceDbAuthorizationService> _logger;

    public SpiceDbAuthorizationService(
        HttpClient httpClient,
        IOptions<SpiceDbOptions> options,
        ILogger<SpiceDbAuthorizationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> CheckOrderPermissionAsync(string subjectId, long orderId, string permission, CancellationToken ct)
    {
        if (!_options.Enabled)
            return true;

        var body = new
        {
            consistency = new { fullyConsistent = true },
            resource = new { objectType = "order", objectId = orderId.ToString() },
            permission,
            subject = new { @object = new { objectType = "user", objectId = subjectId } }
        };

        using var req = BuildRequest("/v1/permissions/check", body);
        using var res = await _httpClient.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("SpiceDB check failed with status {StatusCode}.", (int)res.StatusCode);
            return false;
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var permissionValue = doc.RootElement.TryGetProperty("permissionship", out var value)
            ? value.GetString()
            : null;

        return string.Equals(permissionValue, "PERMISSIONSHIP_HAS_PERMISSION", StringComparison.Ordinal);
    }

    public async Task WriteOrderOwnerAsync(string subjectId, long orderId, CancellationToken ct)
    {
        if (!_options.Enabled)
            return;

        var body = new
        {
            updates = new[]
            {
                new
                {
                    operation = "OPERATION_TOUCH",
                    relationship = new
                    {
                        resource = new { objectType = "order", objectId = orderId.ToString() },
                        relation = "owner",
                        subject = new { @object = new { objectType = "user", objectId = subjectId } }
                    }
                }
            }
        };

        using var req = BuildRequest("/v1/relationships/write", body);
        using var res = await _httpClient.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var payload = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to write SpiceDB relationship: {(int)res.StatusCode} {payload}");
        }
    }

    private HttpRequestMessage BuildRequest(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PreSharedKey);
        return request;
    }
}
