using BoyScout.Application.Automation;

namespace BoyScout.Api.Ai;

internal sealed class AiProcessingControl : IDisposable
{
    private readonly Lock sync = new();
    private readonly IAutomationStore automationStore;
    private Epoch? epoch;
    private CancellationTokenSource? activeGenerationSource;
    private bool enabled;
    private bool disposed;

    public AiProcessingControl(OpenCodeOptions options, IAutomationStore automationStore)
    {
        this.automationStore = automationStore;
        Available = options.Enabled;
        enabled = Available && automationStore.GetAiProcessingEnabled();
        if (enabled)
        {
            epoch = new Epoch();
        }
    }

    public bool Available { get; }

    public AiProcessingStatus Status
    {
        get
        {
            lock (sync)
            {
                return new AiProcessingStatus(Available, enabled);
            }
        }
    }

    public AiProcessingStatus SetEnabled(bool requestedEnabled)
    {
        Epoch? retiredEpoch = null;
        AiProcessingStatus status;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requestedEnabled && !Available)
            {
                throw new InvalidOperationException("AI processing is disabled by environment configuration.");
            }

            automationStore.SetAiProcessingEnabled(requestedEnabled);
            var effectiveEnabled = Available && requestedEnabled;
            if (effectiveEnabled != enabled)
            {
                enabled = effectiveEnabled;
                if (enabled)
                {
                    epoch = new Epoch();
                }
                else
                {
                    retiredEpoch = epoch;
                    epoch = null;
                    if (retiredEpoch is not null)
                    {
                        retiredEpoch.Retired = true;
                    }
                }
            }

            status = new AiProcessingStatus(Available, enabled);
        }

        if (retiredEpoch is not null)
        {
            CancelRetiredEpoch(retiredEpoch);
        }

        return status;
    }

    public GenerationLease? TryAcquireGeneration(CancellationToken stoppingToken)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled || epoch is null)
            {
                return null;
            }

            var acquiredEpoch = epoch;
            acquiredEpoch.Leases++;
            try
            {
                var source = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    acquiredEpoch.Source.Token);
                activeGenerationSource = source;
                return new GenerationLease(this, acquiredEpoch, source);
            }
            catch
            {
                Release(acquiredEpoch, source: null);
                throw;
            }
        }
    }

    public void CancelCurrentGeneration()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeGenerationSource?.Cancel();
        }
    }

    public void Dispose()
    {
        Epoch? retiredEpoch;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            enabled = false;
            retiredEpoch = epoch;
            epoch = null;
            if (retiredEpoch is not null)
            {
                retiredEpoch.Retired = true;
            }
        }

        if (retiredEpoch is not null)
        {
            CancelRetiredEpoch(retiredEpoch);
        }
    }

    private void CancelRetiredEpoch(Epoch retiredEpoch)
    {
        retiredEpoch.Source.Cancel();
        lock (sync)
        {
            retiredEpoch.CancellationIssued = true;
            if (retiredEpoch.Leases == 0)
            {
                retiredEpoch.Source.Dispose();
            }
        }
    }

    private void Release(Epoch releasedEpoch, CancellationTokenSource? source)
    {
        lock (sync)
        {
            if (ReferenceEquals(activeGenerationSource, source))
            {
                activeGenerationSource = null;
            }

            releasedEpoch.Leases--;
            if (releasedEpoch.Retired && releasedEpoch.CancellationIssued && releasedEpoch.Leases == 0)
            {
                releasedEpoch.Source.Dispose();
            }
        }
    }

    internal sealed class Epoch
    {
        public CancellationTokenSource Source { get; } = new();

        public int Leases { get; set; }

        public bool Retired { get; set; }

        public bool CancellationIssued { get; set; }
    }

    internal sealed class GenerationLease(
        AiProcessingControl owner,
        Epoch epoch,
        CancellationTokenSource source) : IDisposable
    {
        private CancellationTokenSource? linkedSource = source;

        public CancellationToken Token => linkedSource?.Token ?? CancellationToken.None;

        public void Dispose()
        {
            var sourceToDispose = Interlocked.Exchange(ref linkedSource, null);
            if (sourceToDispose is null)
            {
                return;
            }

            owner.Release(epoch, sourceToDispose);
            sourceToDispose.Dispose();
        }
    }
}

internal sealed record AiProcessingStatus(bool Available, bool Enabled);
