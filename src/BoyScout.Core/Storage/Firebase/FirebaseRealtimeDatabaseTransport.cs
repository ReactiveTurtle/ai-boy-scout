using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace BoyScout.Core.Storage.Firebase;

public sealed record FirebaseSnapshot(string Json, string ETag, DateTimeOffset? ServerDate = null);

public sealed class FirebaseRealtimeDatabaseTransport : IDisposable
{
    private const int MaxReadAttempts = 3;
    private readonly FirebaseConnection connection;
    private readonly HttpClient httpClient;
    private readonly Func<CancellationToken, Task<string>> getAccessToken;
    private readonly bool ownsClient;

    public FirebaseRealtimeDatabaseTransport(FirebaseConnection connection)
        : this(connection, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), CreateTokenProvider(connection))
    {
        ownsClient = true;
    }

    internal FirebaseRealtimeDatabaseTransport(FirebaseConnection connection, HttpClient httpClient,
        Func<CancellationToken, Task<string>> getAccessToken)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
    }

    public async Task<FirebaseSnapshot> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(path);
        for (var attempt = 1; ; attempt++)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, uri, cancellationToken);
            request.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (IsTransient(response.StatusCode) && attempt < MaxReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))), cancellationToken);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Firebase read failed (HTTP {(int)response.StatusCode}).");

            var etag = ReadEtag(response);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return new FirebaseSnapshot(json, etag, response.Headers.Date);
        }
    }

    public async Task<string> GetKeyPageAsync(string path, string? endAt, int limitToLast,
        CancellationToken cancellationToken = default)
    {
        if (limitToLast is < 1 or > 102) throw new ArgumentOutOfRangeException(nameof(limitToLast));
        var query = "?orderBy=" + Uri.EscapeDataString(JsonSerializer.Serialize("$key")) +
            "&limitToLast=" + limitToLast;
        if (endAt is not null)
            query += "&endAt=" + Uri.EscapeDataString(JsonSerializer.Serialize(endAt));
        var uri = new Uri(BuildUri(path).AbsoluteUri + query);
        for (var attempt = 1; ; attempt++)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, uri, cancellationToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (IsTransient(response.StatusCode) && attempt < MaxReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))), cancellationToken);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Firebase key query failed (HTTP {(int)response.StatusCode}).");
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    public async Task<string> GetShallowAsync(string path, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(BuildUri(path).AbsoluteUri + "?shallow=true");
        for (var attempt = 1; ; attempt++)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, uri, cancellationToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (IsTransient(response.StatusCode) && attempt < MaxReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))), cancellationToken);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Firebase shallow read failed (HTTP {(int)response.StatusCode}).");
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    // A failed or ambiguous PUT is never retried: the caller must re-read before trying again.
    public async Task<bool> PutIfMatchAsync(string path, string json, string etag,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(path);
        if (string.IsNullOrWhiteSpace(etag) || etag == "*" || etag.Contains('\r') || etag.Contains('\n'))
            throw new ArgumentException("A specific ETag from a read is required.", nameof(etag));
        ArgumentNullException.ThrowIfNull(json);
        using (var document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Null)
                throw new ArgumentException("Writing null would delete the node.", nameof(json));
        }

        using var request = await CreateRequestAsync(HttpMethod.Put, uri, cancellationToken);
        request.Headers.TryAddWithoutValidation("if-match", etag);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;
        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.NoContent)
            throw new HttpRequestException($"Firebase conditional write failed (HTTP {(int)response.StatusCode}); re-read before retrying.");
        return true;
    }

    private static Func<CancellationToken, Task<string>> CreateTokenProvider(FirebaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var credential = new ServiceAccountCredential(new ServiceAccountCredential.Initializer(connection.ClientEmail)
        {
            Scopes = ["https://www.googleapis.com/auth/firebase.database", "https://www.googleapis.com/auth/userinfo.email"],
        }.FromPrivateKey(connection.PrivateKey));
        return cancellationToken => credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        var token = await getAccessToken(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Firebase access token is unavailable.");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private Uri BuildUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Split('/').Any(segment =>
                segment.Length == 0 || segment.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '_' && character != '-')))
            throw new ArgumentException("Path must contain nonempty ASCII alphanumeric, hyphen or underscore segments.", nameof(path));
        return new Uri(connection.DatabaseUrl, path + ".json");
    }

    private static string ReadEtag(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ETag", out var values) || string.IsNullOrWhiteSpace(values.SingleOrDefault()))
            throw new HttpRequestException("Firebase read response has no ETag; refusing an unprotected write.");
        return values.Single();
    }

    private static bool IsTransient(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    public void Dispose()
    {
        if (ownsClient) httpClient.Dispose();
    }
}
