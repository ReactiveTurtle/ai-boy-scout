using System.Net;
using BoyScout.Core.Storage.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseRealtimeDatabaseTransportTests
{
    private static FirebaseConnection Connection() => new(
        "https://example-default-rtdb.firebaseio.com/", "project", "service@example.com", "test-key");

    [Fact]
    public async Task Read_then_conditional_write_reports_contention_without_overwriting()
    {
        var reads = 0;
        var writes = 0;
        using var client = new HttpClient(new FakeHandler(request =>
        {
            Assert.Equal("https://example-default-rtdb.firebaseio.com/reports/item.json", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Firebase-ETag")));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":1}") };
                response.Headers.TryAddWithoutValidation("ETag", "\"version-1\"");
                return response;
            }
            writes++;
            Assert.Equal("\"version-1\"", Assert.Single(request.Headers.GetValues("if-match")));
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        }));
        var transport = new FirebaseRealtimeDatabaseTransport(Connection(), client, _ => Task.FromResult("test-token"));

        var snapshot = await transport.GetAsync("reports/item", TestContext.Current.CancellationToken);
        Assert.Equal("{\"value\":1}", snapshot.Json);
        Assert.False(await transport.PutIfMatchAsync("reports/item", "{\"value\":2}", snapshot.ETag, TestContext.Current.CancellationToken));
        Assert.Equal(1, reads);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Missing_etag_or_invalid_payload_fails_before_any_write()
    {
        var calls = 0;
        using var client = new HttpClient(new FakeHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        var transport = new FirebaseRealtimeDatabaseTransport(Connection(), client, _ => Task.FromResult("test-token"));

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.GetAsync("reports/item", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => transport.PutIfMatchAsync("reports/item", "{}", "*", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => transport.PutIfMatchAsync("reports/item", "null", "\"v1\"", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => transport.PutIfMatchAsync("../reports", "{}", "\"v1\"", TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transient_reads_are_bounded_and_put_is_not_retried()
    {
        var calls = 0;
        using var client = new HttpClient(new FakeHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        var transport = new FirebaseRealtimeDatabaseTransport(Connection(), client, _ => Task.FromResult("test-token"));

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.GetAsync("reports/item", TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.PutIfMatchAsync("reports/item", "{}", "\"v1\"", TestContext.Current.CancellationToken));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Conditional_write_succeeds_only_with_supplied_etag()
    {
        var writes = 0;
        using var client = new HttpClient(new FakeHandler(request =>
        {
            writes++;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("\"v1\"", Assert.Single(request.Headers.GetValues("if-match")));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var transport = new FirebaseRealtimeDatabaseTransport(Connection(), client, _ => Task.FromResult("test-token"));

        Assert.True(await transport.PutIfMatchAsync("reports/item", "{\"value\":2}", "\"v1\"", TestContext.Current.CancellationToken));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Key_query_uses_encoded_firebase_parameters_without_etag()
    {
        var calls = 0;
        using var client = new HttpClient(new FakeHandler(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/report-projects/7/Notes.json", request.RequestUri!.AbsolutePath);
            Assert.Equal("?orderBy=%22%24key%22&limitToLast=101&endAt=%22BSR-7-0000000000000000010%22",
                request.RequestUri.Query);
            Assert.False(request.Headers.Contains("X-Firebase-ETag"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        var transport = new FirebaseRealtimeDatabaseTransport(Connection(), client, _ => Task.FromResult("test-token"));

        Assert.Equal("{}", await transport.GetKeyPageAsync("report-projects/7/Notes", "BSR-7-0000000000000000010", 101,
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.GetKeyPageAsync("report-projects/7/Notes", null, 103,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://localhost:9000/")]
    [InlineData("https://example.com/")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("https://example.com/nested/")]
    [InlineData("https://example.com/?auth=secret")]
    public void Rejects_unsafe_production_urls(string url)
    {
        Assert.Throws<ArgumentException>(() => new FirebaseConnection(url, "project", "service@example.com", "key"));
    }

    [Fact]
    public void Loopback_emulator_requires_explicit_test_opt_in()
    {
        var connection = new FirebaseConnection("http://localhost:9000/", "project", "service@example.com", "key",
            allowLoopbackEmulatorForTests: true);
        Assert.Equal("http://localhost:9000/", connection.DatabaseUrl.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => new FirebaseConnection("http://example.com/", "project", "service@example.com", "key",
            allowLoopbackEmulatorForTests: true));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
