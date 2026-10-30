using System.Net;
using BoyScout.Infrastructure.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseSequenceAllocatorTests
{
    [Fact]
    public async Task Initializes_missing_counter_and_allocates_monotonically()
    {
        long? stored = null;
        var version = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://example.firebaseio.com/counters/reports.json", request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(stored?.ToString() ?? "null"),
                };
                response.Headers.TryAddWithoutValidation("ETag", $"\"v{version}\"");
                return response;
            }

            Assert.Equal($"\"v{version}\"", Assert.Single(request.Headers.GetValues("if-match")));
            stored = long.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            version++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var allocator = CreateAllocator(client);

        Assert.Equal(1, await allocator.AllocateAsync("reports", TestContext.Current.CancellationToken));
        Assert.Equal(2, await allocator.AllocateAsync("reports", TestContext.Current.CancellationToken));
        Assert.Equal(2, stored);
    }

    [Fact]
    public async Task Rereads_after_conflict_and_stops_after_bounded_contention()
    {
        var reads = 0;
        var writes = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reads.ToString()) };
                response.Headers.TryAddWithoutValidation("ETag", $"\"v{reads}\"");
                return response;
            }

            writes++;
            Assert.Equal($"\"v{reads}\"", Assert.Single(request.Headers.GetValues("if-match")));
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAllocator(client).AllocateAsync("tasks", TestContext.Current.CancellationToken));
        Assert.Equal(5, reads);
        Assert.Equal(5, writes);
    }

    [Fact]
    public async Task Successful_retry_uses_fresh_value_after_conflict()
    {
        var reads = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reads == 1 ? "0" : "41"),
                };
                response.Headers.TryAddWithoutValidation("ETag", $"\"v{reads}\"");
                return response;
            }

            Assert.Equal(reads == 1 ? "1" : "42", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(reads == 1 ? HttpStatusCode.PreconditionFailed : HttpStatusCode.OK);
        }));

        Assert.Equal(42, await CreateAllocator(client).AllocateAsync("tasks", TestContext.Current.CancellationToken));
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("{}")]
    [InlineData("9223372036854775807")]
    public async Task Refuses_invalid_or_exhausted_counter(string json)
    {
        var writes = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
            return response;
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAllocator(client).AllocateAsync("reports", TestContext.Current.CancellationToken));
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Ambiguous_put_is_not_retried()
    {
        var reads = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Put) throw new HttpRequestException("Connection lost after write");
            reads++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("0") };
            response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
            return response;
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateAllocator(client).AllocateAsync("reports", TestContext.Current.CancellationToken));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Rejects_unsafe_counter_name_before_network_access()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            throw new Exception("Unexpected network access");
        }));
        var allocator = CreateAllocator(client);

        await Assert.ThrowsAsync<ArgumentException>(() => allocator.AllocateAsync("../reports", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => allocator.AllocateAsync("", TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    private static FirebaseSequenceAllocator CreateAllocator(HttpClient client) => new(
        new FirebaseRealtimeDatabaseTransport(
            new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
            client, _ => Task.FromResult("test-token")));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
