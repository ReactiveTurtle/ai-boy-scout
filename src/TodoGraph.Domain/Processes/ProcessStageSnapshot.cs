using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoGraph.Domain.Processes;

public sealed class ProcessStageSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private ProcessStageSnapshot(string hash, string instructions, ProcessStageBackwardLink? backwardLink)
    {
        Hash = hash;
        Instructions = instructions;
        BackwardLink = backwardLink;
    }

    public string Hash { get; }
    public string Instructions { get; }
    public ProcessStageBackwardLink? BackwardLink { get; }

    internal bool HasSameContent(ProcessStageSnapshot other) =>
        Instructions == other.Instructions && BackwardLink == other.BackwardLink;

    internal static ProcessStageSnapshot Create(ProcessStage stage)
    {
        var hash = ComputeHash(stage.Instructions, stage.BackwardLink);
        return new ProcessStageSnapshot(hash, stage.Instructions, stage.BackwardLink);
    }

    public static ProcessStageSnapshot Rehydrate(string hash, string instructions, ProcessStageBackwardLink? backwardLink)
    {
        if (string.IsNullOrWhiteSpace(hash) || ComputeHash(instructions, backwardLink) != hash)
            throw new ArgumentException("Stage snapshot hash does not match its content.", nameof(hash));

        return new ProcessStageSnapshot(hash, instructions, backwardLink);
    }

    private static string ComputeHash(string instructions, ProcessStageBackwardLink? backwardLink)
    {
        var content = new StageBehavior(
            instructions,
            backwardLink is { } link ? new BackwardLinkBehavior(link.TargetStageId, link.MaxRepeats) : null);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(content, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(serialized)).ToLowerInvariant();
    }

    private sealed record StageBehavior(
        [property: JsonPropertyOrder(0)] string Instructions,
        [property: JsonPropertyOrder(1)] BackwardLinkBehavior? BackwardLink);

    private sealed record BackwardLinkBehavior(
        [property: JsonPropertyOrder(0)] Guid TargetStageId,
        [property: JsonPropertyOrder(1)] int MaxRepeats);
}
