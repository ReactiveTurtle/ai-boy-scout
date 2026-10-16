namespace BoyScout.Core.Storage.Firebase;

public sealed class FirebaseConnection
{
    public Uri DatabaseUrl { get; }
    public string ProjectId { get; }
    public string ClientEmail { get; }
    internal string PrivateKey { get; }

    public FirebaseConnection(string databaseUrl, string projectId, string clientEmail, string privateKey)
        : this(databaseUrl, projectId, clientEmail, privateKey, allowLoopbackEmulatorForTests: false)
    {
    }

    internal FirebaseConnection(string databaseUrl, string projectId, string clientEmail, string privateKey,
        bool allowLoopbackEmulatorForTests)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var url)
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query)
            || !string.IsNullOrEmpty(url.Fragment)
            || url.AbsolutePath != "/"
            || (!allowLoopbackEmulatorForTests &&
                !url.DnsSafeHost.EndsWith(".firebaseio.com", StringComparison.OrdinalIgnoreCase) &&
                !url.DnsSafeHost.EndsWith(".firebasedatabase.app", StringComparison.OrdinalIgnoreCase))
            || (url.Scheme != Uri.UriSchemeHttps
                && !(allowLoopbackEmulatorForTests && url.Scheme == Uri.UriSchemeHttp && url.IsLoopback)))
        {
            throw new ArgumentException("Database URL must be an HTTPS origin without credentials, path, query or fragment.", nameof(databaseUrl));
        }

        DatabaseUrl = url;
        ProjectId = string.IsNullOrWhiteSpace(projectId) ? throw new ArgumentException("Project ID is required.", nameof(projectId)) : projectId;
        ClientEmail = string.IsNullOrWhiteSpace(clientEmail) ? throw new ArgumentException("Client email is required.", nameof(clientEmail)) : clientEmail;
        PrivateKey = string.IsNullOrWhiteSpace(privateKey) ? throw new ArgumentException("Private key is required.", nameof(privateKey)) : privateKey;
    }
}
