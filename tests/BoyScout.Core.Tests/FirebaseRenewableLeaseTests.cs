using System.Net;
using System.Text.Json;
using BoyScout.Core.Storage.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseRenewableLeaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Acquire_renew_force_release_and_fence_stale_worker()
    {
        using var server = new LeaseServer();
        var lease = CreateLease(server);
        var first = (await lease.AcquireAsync("installation-1", "worker 1", TestContext.Current.CancellationToken))!;
        Assert.Equal(1, first.Generation);
        Assert.Equal(Start.AddMinutes(2), first.ExpiresAt);
        Assert.Null(await lease.AcquireAsync("installation-2", "worker 2", TestContext.Current.CancellationToken));
        Assert.True(await lease.VerifyFencingAsync(first, TestContext.Current.CancellationToken));

        server.Now = Start.AddMinutes(1);
        var renewed = (await lease.RenewAsync(first, TestContext.Current.CancellationToken))!;
        Assert.Equal(first.Token, renewed.Token);
        Assert.Equal(first.Generation, renewed.Generation);
        Assert.False(await lease.VerifyFencingAsync(first, TestContext.Current.CancellationToken));
        Assert.True(await lease.VerifyFencingAsync(renewed, TestContext.Current.CancellationToken));

        await lease.ForceReleaseAsync(TestContext.Current.CancellationToken);
        Assert.False(await lease.VerifyFencingAsync(renewed, TestContext.Current.CancellationToken));
        Assert.Null(await lease.RenewAsync(renewed, TestContext.Current.CancellationToken));
        var second = (await lease.AcquireAsync("installation-2", "worker 2", TestContext.Current.CancellationToken))!;
        Assert.Equal(3, second.Generation);
        Assert.NotEqual(first.Token, second.Token);
        Assert.False(await lease.VerifyFencingAsync(renewed, TestContext.Current.CancellationToken));
        Assert.Equal("installation-2", second.OwnerInstallationId);
    }

    [Fact]
    public async Task Expired_lease_cannot_renew_and_takeover_advances_generation()
    {
        using var server = new LeaseServer();
        var lease = CreateLease(server);
        var old = (await lease.AcquireAsync("one", "name", TestContext.Current.CancellationToken))!;
        server.Now = old.ExpiresAt;
        Assert.Null(await lease.RenewAsync(old, TestContext.Current.CancellationToken));
        Assert.False(await lease.VerifyFencingAsync(old, TestContext.Current.CancellationToken));
        var next = (await lease.AcquireAsync("two", "name", TestContext.Current.CancellationToken))!;
        Assert.Equal(2, next.Generation);
        Assert.Null(await lease.RenewAsync(old, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Conflict_rereads_and_never_reuses_stale_etag()
    {
        using var server = new LeaseServer { ConflictOnce = true };
        var handle = await CreateLease(server).AcquireAsync("one", "name", TestContext.Current.CancellationToken);
        Assert.NotNull(handle);
        Assert.Equal(2, server.Reads);
        Assert.Equal(2, server.Writes);
    }

    [Fact]
    public async Task Force_release_conflict_fences_old_handle()
    {
        using var server = new LeaseServer();
        var lease = CreateLease(server);
        var old = (await lease.AcquireAsync("one", "name", TestContext.Current.CancellationToken))!;
        server.ConflictOnce = true;
        await lease.ForceReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Null(await lease.RenewAsync(old, TestContext.Current.CancellationToken));
        Assert.False(await lease.VerifyFencingAsync(old, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Force_release_between_renew_read_and_put_blocks_stale_write()
    {
        using var server = new LeaseServer();
        var lease = CreateLease(server);
        var old = (await lease.AcquireAsync("one", "name", TestContext.Current.CancellationToken))!;
        server.ReleaseBeforeNextPut = true;

        Assert.Null(await lease.RenewAsync(old, TestContext.Current.CancellationToken));
        Assert.False(await lease.VerifyFencingAsync(old, TestContext.Current.CancellationToken));
        Assert.Equal(4, server.Reads); // acquire, renew, retry after 412, plus fencing check
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad")]
    [InlineData("Wed, 31 Dec 1969 23:59:59 GMT")]
    public async Task Missing_or_invalid_server_date_refuses_all_operations(string? date)
    {
        using var server = new LeaseServer { DateOverride = date ?? "" };
        var lease = CreateLease(server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.AcquireAsync("one", "name", TestContext.Current.CancellationToken));
        Assert.Equal(0, server.Writes);
        server.DateOverride = null;
        var handle = (await lease.AcquireAsync("one", "name", TestContext.Current.CancellationToken))!;
        server.DateOverride = date ?? "";
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.RenewAsync(handle, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.VerifyFencingAsync(handle, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ForceReleaseAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, server.Writes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Generation\":\"1\"}")]
    [InlineData("{\"Generation\":1,\"ExpiresAt\":\"not a date\",\"Token\":null,\"OwnerInstallationId\":null,\"OwnerName\":null}")]
    [InlineData("{\"Generation\":9223372036854775807,\"ExpiresAt\":\"2026-01-01T00:00:00+00:00\",\"Token\":null,\"OwnerInstallationId\":null,\"OwnerName\":null}")]
    public async Task Invalid_or_exhausted_state_is_not_overwritten(string json)
    {
        using var server = new LeaseServer { Json = json };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateLease(server).AcquireAsync("one", "name", TestContext.Current.CancellationToken));
        Assert.Equal(0, server.Writes);
    }

    [Fact]
    public async Task Ambiguous_put_is_not_retried()
    {
        using var server = new LeaseServer { ThrowOnPut = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => CreateLease(server).AcquireAsync("one", "name", TestContext.Current.CancellationToken));
        Assert.Equal(1, server.Reads);
        Assert.Equal(1, server.Writes);
    }

    private static FirebaseRenewableLease CreateLease(LeaseServer server) => new(
        new FirebaseRealtimeDatabaseTransport(
            new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
            server.Client, _ => Task.FromResult("test-token")), "leases/worker", TimeSpan.FromMinutes(2));

    private sealed class LeaseServer : IDisposable
    {
        private int version;
        public string Json { get; set; } = "null";
        public DateTimeOffset Now { get; set; } = Start;
        public string? DateOverride { get; set; }
        public bool ConflictOnce { get; set; }
        public bool ReleaseBeforeNextPut { get; set; }
        public bool ThrowOnPut { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public HttpClient Client { get; }

        public LeaseServer() => Client = new HttpClient(new Handler(Respond));

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Assert.Equal("https://example.firebaseio.com/leases/worker.json", request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get)
            {
                Reads++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) };
                response.Headers.TryAddWithoutValidation("ETag", $"\"v{version}\"");
                if (DateOverride is null) response.Headers.Date = Now;
                else if (DateOverride.Length > 0) response.Headers.TryAddWithoutValidation("Date", DateOverride);
                return response;
            }
            Writes++;
            if (ReleaseBeforeNextPut)
            {
                ReleaseBeforeNextPut = false;
                Json = JsonSerializer.Serialize(new
                {
                    OwnerInstallationId = (string?)null,
                    OwnerName = (string?)null,
                    Token = (string?)null,
                    Generation = 2,
                    ExpiresAt = Now,
                });
                version++;
                return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            }
            Assert.Equal($"\"v{version}\"", Assert.Single(request.Headers.GetValues("if-match")));
            if (ThrowOnPut) throw new HttpRequestException("Ambiguous write");
            if (ConflictOnce)
            {
                ConflictOnce = false;
                version++;
                return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            }
            Json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(Json);
            version++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public void Dispose() => Client.Dispose();

        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(respond(request));
        }
    }
}
