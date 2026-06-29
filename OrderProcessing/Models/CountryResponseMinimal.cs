using System.Text.Json.Serialization;

namespace OrderProcessing.Models
{
    /// <summary>
    /// Dedicated DTO for country responses containing minimal fields.
    /// Properties use PascalCase but are serialized with snake-case keys as defined by JsonPropertyName attributes.
    /// </summary>
    public sealed class CountryResponseMinimal
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
