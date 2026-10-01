using System.Diagnostics;
using System.Text;

namespace BoyScout.Api.Ai;

internal interface IOpenCodeCommandRunner
{
    Task<OpenCodeCommandResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class OpenCodeCommandRunner : IOpenCodeCommandRunner
{
    private const int MaximumStandardErrorLength = 2_000;

    public async Task<OpenCodeCommandResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("OpenCode process could not be started.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardErrorTask = ReadBoundedAsync(process.StandardError, MaximumStandardErrorLength);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var executionSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var cancelled = false;
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(executionSource.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return new OpenCodeCommandResult(
            process.ExitCode,
            standardOutput,
            standardError,
            timedOut,
            cancelled);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumLength)
    {
        var result = new StringBuilder(capacity: maximumLength);
        var buffer = new char[512];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = maximumLength - result.Length;
            if (remaining > 0)
            {
                result.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return result.ToString();
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }
}

internal sealed record OpenCodeCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Cancelled = false);
