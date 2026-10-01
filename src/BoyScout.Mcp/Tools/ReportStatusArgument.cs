using System.Text.Json.Serialization;

namespace BoyScout.Mcp.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<ReportStatusArgument>))]
public enum ReportStatusArgument
{
    [JsonStringEnumMemberName("open")]
    Open,

    [JsonStringEnumMemberName("accepted")]
    Accepted,

    [JsonStringEnumMemberName("planned")]
    Planned,

    [JsonStringEnumMemberName("resolved")]
    Resolved,

    [JsonStringEnumMemberName("dismissed")]
    Dismissed,
}
