using System.Net;
using BoyScout.Core.Storage.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseProjectCatalogTests
{
    [Fact]
    public async Task Get_reads_only_metadata_child_and_returns_null_when_absent()
    {
        var paths = new List<string>();
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Firebase-ETag")));
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            return Task.FromResult(Ok(request.RequestUri.AbsolutePath.EndsWith("/5/Project.json", StringComparison.Ordinal)
                ? "{\"Id\":5,\"Name\":\"Alpha\"}" : "null", etag: true));
        }));
        var repository = Repository(client);

        Assert.Equal(new FirebaseReportProject(5, "Alpha"), await repository.GetProjectAsync(5, TestContext.Current.CancellationToken));
        Assert.Null(await repository.GetProjectAsync(6, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "/report-projects/5/Project.json", "/report-projects/6/Project.json" }, paths);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetProjectAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public async Task List_uses_shallow_keys_and_bounded_metadata_reads_without_downloading_roots()
    {
        var active = 0;
        var peak = 0;
        var children = 0;
        using var client = new HttpClient(new FakeHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            if (request.RequestUri!.PathAndQuery == "/report-projects.json?shallow=true")
            {
                Assert.False(request.Headers.Contains("X-Firebase-ETag"));
                return Ok("{" + string.Join(",", Enumerable.Range(1, 20).Reverse().Select(id => $"\"{id}\":true")) + "}");
            }
            var path = request.RequestUri.PathAndQuery;
            Assert.Matches("^/report-projects/[0-9]+/Project\\.json$", path);
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Firebase-ETag")));
            var count = Interlocked.Increment(ref active);
            InterlockedExtensions.UpdateMax(ref peak, count);
            try
            {
                await Task.Delay(10, token);
                Interlocked.Increment(ref children);
                var id = long.Parse(path.Split('/')[2]);
                return Ok($"{{\"Id\":{id},\"Name\":\"Project {id}\"}}", etag: true);
            }
            finally { Interlocked.Decrement(ref active); }
        }));

        var projects = await Repository(client).ListProjectsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(1, 20).Select(id => new FirebaseReportProject(id, $"Project {id}")), projects);
        Assert.Equal(20, children);
        Assert.InRange(peak, 1, 8);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Empty_or_missing_remote_root_returns_empty_catalog(string root)
    {
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            Assert.Equal("/report-projects.json?shallow=true", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Ok(root));
        }));
        Assert.Empty(await Repository(client).ListProjectsAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"abc\":true}")]
    [InlineData("{\"0\":true}")]
    [InlineData("{\"01\":true}")]
    [InlineData("{\"9223372036854775808\":true}")]
    [InlineData("{\"1\":{\"Notes\":{}}}")]
    [InlineData("[]")]
    public async Task Malformed_shallow_listing_fails_before_child_reads(string root)
    {
        var calls = 0;
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("/report-projects.json?shallow=true", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Ok(root));
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(client).ListProjectsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{\"Id\":2,\"Name\":\"Wrong\"}")]
    [InlineData("{\"Id\":1,\"Name\":\" \"}")]
    [InlineData("{\"Name\":\"Missing ID\"}")]
    [InlineData("{\"Id\":1}")]
    [InlineData("null")]
    public async Task Listed_project_with_invalid_or_missing_metadata_fails_closed(string metadata)
    {
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri!.PathAndQuery switch
            {
                "/report-projects.json?shallow=true" => Ok("{\"1\":true}"),
                "/report-projects/1/Project.json" => Ok(metadata, etag: true),
                _ => throw new InvalidOperationException("Full project root was requested."),
            });
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(client).ListProjectsAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"Id\":8,\"Name\":\"Wrong\"}")]
    [InlineData("{\"Id\":7,\"Name\":\" \"}")]
    [InlineData("{\"Name\":\"Missing ID\"}")]
    public async Task Get_rejects_invalid_metadata(string metadata)
    {
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            Assert.Equal("/report-projects/7/Project.json", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Ok(metadata, etag: true));
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(client).GetProjectAsync(7, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shallow_read_retries_transient_failure_without_exposing_credentials()
    {
        var calls = 0;
        using var client = new HttpClient(new FakeHandler((request, _) =>
        {
            Assert.Equal("/report-projects.json?shallow=true", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Repository(client).ListProjectsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
        Assert.DoesNotContain("token", error.Message);
        Assert.DoesNotContain("service@example.com", error.Message);
    }

    private static FirebaseReportRepository Repository(HttpClient client)
    {
        var transport = new FirebaseRealtimeDatabaseTransport(
            new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "key"),
            client, _ => Task.FromResult("token"));
        return new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
    }

    private static HttpResponseMessage Ok(string json, bool etag = false)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        if (etag) response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }

    private static class InterlockedExtensions
    {
        public static void UpdateMax(ref int location, int value)
        {
            int previous;
            do
            {
                previous = Volatile.Read(ref location);
                if (previous >= value) return;
            } while (Interlocked.CompareExchange(ref location, value, previous) != previous);
        }
    }
}
