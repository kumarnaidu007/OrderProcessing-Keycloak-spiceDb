namespace OrderProcessing.Common;

public sealed class SpiceDbOptions
{
    public const string SectionName = "SpiceDb";

    public bool Enabled { get; set; } = false;
    public string HttpEndpoint { get; set; } = "";
    public string PreSharedKey { get; set; } = "";
}
