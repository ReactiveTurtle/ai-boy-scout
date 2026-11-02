using System.Text.Json.Serialization;

namespace TodoGraph.Domain.Processes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessRunStatus
{
    Running,
    WaitingForUser,
    Completed,
    Blocked,
}
