using System.Globalization;
using System.Text.Json;

namespace BoyScout.Infrastructure.Firebase;

/// <summary>Allocates positive numeric IDs in a dedicated Firebase counter node.</summary>
public sealed class FirebaseSequenceAllocator(FirebaseRealtimeDatabaseTransport transport)
{
    private const int MaxAttempts = 5;

    public async Task<long> AllocateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (string.IsNullOrEmpty(name) || name.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_' && character != '-'))
            throw new ArgumentException("Counter name must contain only ASCII letters, digits, hyphens or underscores.", nameof(name));

        var path = "counters/" + name;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(snapshot.Json);
            var root = document.RootElement;
            long current;
            if (root.ValueKind == JsonValueKind.Null)
                current = 0;
            else if (root.ValueKind != JsonValueKind.Number || !root.TryGetInt64(out current) || current < 0)
                throw new InvalidOperationException($"Firebase counter '{name}' is not a nonnegative integer.");

            if (current == long.MaxValue)
                throw new InvalidOperationException($"Firebase counter '{name}' is exhausted.");

            var next = current + 1;
            // A 412 is a known conflict; an exception from PUT may mean it committed, so never retry it.
            if (await transport.PutIfMatchAsync(path, next.ToString(CultureInfo.InvariantCulture), snapshot.ETag, cancellationToken))
                return next;
        }

        throw new InvalidOperationException($"Firebase counter '{name}' remained contended after {MaxAttempts} attempts.");
    }
}
