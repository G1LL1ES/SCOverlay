using System.Net.Http.Headers;
using System.Text.Json;

namespace SCOverlay.Core.Application;

public interface IReleaseUpdateService
{
    ValueTask<ReleaseUpdateInfo> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

public sealed record ReleaseUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    Uri ReleaseUri,
    bool IsUpdateAvailable);

public static class ReleaseUpdatePolicy
{
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    public static bool ShouldCheckAutomatically(AppSettings settings, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.AutomaticUpdateChecksEnabled &&
            (!settings.LastUpdateCheckUtc.HasValue ||
             nowUtc - settings.LastUpdateCheckUtc.Value >= AutomaticCheckInterval);
    }
}

public static class ReleaseVersion
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        int metadataIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        if (normalized.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Version.TryParse(normalized, out Version? parsedVersion) ||
            parsedVersion is null ||
            parsedVersion.Minor < 0 ||
            parsedVersion.Build < 0)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    public static string Normalize(string value)
    {
        if (!TryParse(value, out Version version))
        {
            throw new FormatException($"'{value}' is not a stable release version.");
        }

        return version.Build < 0
            ? version.ToString(2)
            : version.Revision < 0
                ? version.ToString(3)
                : version.ToString(4);
    }
}

public sealed class GitHubReleaseUpdateService : IReleaseUpdateService, IDisposable
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/G1LL1ES/SCOverlay/releases/latest";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TimeSpan timeout;

    public GitHubReleaseUpdateService()
        : this(new HttpClient(), ownsHttpClient: true, DefaultTimeout)
    {
    }

    public GitHubReleaseUpdateService(HttpMessageHandler handler, TimeSpan? timeout = null)
        : this(new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))), ownsHttpClient: true, timeout ?? DefaultTimeout)
    {
    }

    public GitHubReleaseUpdateService(HttpClient httpClient, TimeSpan? timeout = null)
        : this(httpClient, ownsHttpClient: false, timeout ?? DefaultTimeout)
    {
    }

    private GitHubReleaseUpdateService(HttpClient httpClient, bool ownsHttpClient, TimeSpan timeout)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        this.timeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async ValueTask<ReleaseUpdateInfo> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out Version parsedCurrentVersion))
        {
            throw new FormatException($"Current app version '{currentVersion}' is invalid.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("SCOverlay-UpdateCheck/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedCancellation.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(linkedCancellation.Token).ConfigureAwait(false);
        GitHubReleaseResponse? release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
            stream,
            cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
        if (release is null || release.Draft || release.Prerelease)
        {
            throw new InvalidDataException("GitHub did not return a stable published release.");
        }

        if (!ReleaseVersion.TryParse(release.TagName, out Version parsedLatestVersion))
        {
            throw new InvalidDataException($"GitHub returned an invalid release tag '{release.TagName}'.");
        }

        if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? releaseUri) ||
            releaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub returned an invalid release URL.");
        }

        return new ReleaseUpdateInfo(
            ReleaseVersion.Normalize(currentVersion),
            ReleaseVersion.Normalize(release.TagName),
            releaseUri,
            parsedLatestVersion > parsedCurrentVersion);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private sealed record GitHubReleaseResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("html_url")] string HtmlUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("draft")] bool Draft,
        [property: System.Text.Json.Serialization.JsonPropertyName("prerelease")] bool Prerelease);
}
