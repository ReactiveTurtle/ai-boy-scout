using BoyScout.Api.Ai;
using BoyScout.Core.Automation;
using BoyScout.Core.Storage;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class AiProcessingControlTests : IDisposable
{
    private readonly string directoryPath = Path.Combine(
        Path.GetTempPath(),
        $"boy-scout-ai-control-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CancelCurrentGeneration_cancels_active_lease_without_disabling_processing()
    {
        var database = new BoyScoutDatabase(Path.Combine(directoryPath, "reports.db"));
        database.Migrate();
        var options = new OpenCodeOptions(
            true,
            TimeSpan.FromMinutes(3),
            "opencode",
            "unused.json",
            directoryPath);
        using var control = new AiProcessingControl(options, new AutomationStore(database));
        using var lease = Assert.IsType<AiProcessingControl.GenerationLease>(
            control.TryAcquireGeneration(CancellationToken.None));

        control.CancelCurrentGeneration();

        Assert.True(lease.Token.IsCancellationRequested);
        Assert.True(control.Status.Enabled);
        Assert.True(new AutomationStore(database).GetAiProcessingEnabled());
    }

    public void Dispose()
    {
        SqliteConnectionCleanup();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void SqliteConnectionCleanup() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}
