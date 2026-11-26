using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuste.Api.Updates;

internal sealed record UpdateInstallation(Version? Version, string? Rid, bool Enabled)
{
    public static UpdateInstallation FromAssembly(Assembly assembly, bool enabled)
    {
        var rid = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "FusteReleaseRid")?.Value;
        var version = rid is null ? null : assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+', 2)[0];
        return new UpdateInstallation(ParseReleaseVersion(version), rid, enabled);
    }

    internal static Version? ParseReleaseVersion(string? value) =>
        Version.TryParse(value, out var version) && version.Build >= 0 && version.Revision < 0
            && version.ToString(3) == value ? version : null;
}

internal sealed record UpdateStatus(
    string Status,
    string? InstalledVersion,
    string? Rid,
    DateTimeOffset? LastCheckedAt = null,
    string? AvailableVersion = null,
    string? ReleaseUrl = null,
    string? DownloadUrl = null);

internal sealed class UpdateChecker(IHttpClientFactory clients, UpdateInstallation installation) : BackgroundService
{
    private const string Repository = "rosa-gus/fuste-confere";
    private const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private const string LatestRelease = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly bool _canCheck = installation.Enabled && installation.Version is not null
        && installation.Rid is "linux-x64" or "win-x64";
    private volatile UpdateStatus? _snapshot;
    private UpdateStatus? _lastRelease;
    private EntityTagHeaderValue? _etag;
    private DateTimeOffset _retryNotBefore;

    public UpdateStatus Current => _snapshot ?? ForInstallation(_canCheck ? "checking" : "disabled");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_canCheck) return;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await CheckAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!_canCheck || DateTimeOffset.UtcNow < _retryNotBefore) return;

        try
        {
            using var client = clients.CreateClient(nameof(UpdateChecker));
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            if (_etag is not null) request.Headers.IfNoneMatch.Add(_etag);

            using var response = await client.SendAsync(request, cancellationToken);
            var checkedAt = DateTimeOffset.UtcNow;
            if (response.StatusCode == HttpStatusCode.NotModified && _lastRelease is not null)
            {
                _snapshot = _lastRelease with { LastCheckedAt = checkedAt };
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    _retryNotBefore = RetryNotBefore(response, checkedAt);
                _snapshot = ForInstallation("unavailable", checkedAt);
                return;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(body, JsonOptions, cancellationToken);
            var tag = release?.Tag;
            var version = tag?.StartsWith('v') == true ? UpdateInstallation.ParseReleaseVersion(tag[1..]) : null;
            if (version is null || release!.Draft || release.Prerelease || release.Assets is null)
            {
                _snapshot = ForInstallation("unavailable", checkedAt);
                return;
            }

            _lastRelease = VersionStatus(tag!, version, release.Assets, checkedAt);
            _etag = response.Headers.ETag;
            _snapshot = _lastRelease;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            _snapshot = ForInstallation("unavailable", DateTimeOffset.UtcNow);
        }
    }

    private UpdateStatus VersionStatus(string tag, Version version, GitHubAsset[] assets, DateTimeOffset checkedAt)
    {
        if (version <= installation.Version)
            return ForInstallation("upToDate", checkedAt);

        var extension = installation.Rid == "win-x64" ? "zip" : "tar.gz";
        var expected = $"fuste-confere-{tag}-{installation.Rid}.{extension}";
        var hasPackage = assets.Any(asset => asset is { State: "uploaded", Size: > 0 } && asset.Name == expected);
        return ForInstallation(hasPackage ? "updateAvailable" : "incompatible", checkedAt) with
        {
            AvailableVersion = version.ToString(3),
            ReleaseUrl = $"{ReleasesPage}/tag/{tag}",
            DownloadUrl = hasPackage ? $"{ReleasesPage}/download/{tag}/{expected}" : null,
        };
    }

    private UpdateStatus ForInstallation(string status, DateTimeOffset? checkedAt = null) =>
        new(status, installation.Version?.ToString(3), installation.Rid, checkedAt);

    private static DateTimeOffset RetryNotBefore(HttpResponseMessage response, DateTimeOffset checkedAt)
    {
        var retry = response.Headers.RetryAfter;
        var nextCheck = retry?.Date ?? checkedAt + (retry?.Delta ?? TimeSpan.Zero);
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")
            && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
            && long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            var limitReset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (limitReset > nextCheck) nextCheck = limitReset;
        }
        return nextCheck;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? Tag, bool Draft, bool Prerelease, GitHubAsset[]? Assets);

    private sealed record GitHubAsset(string? Name, string? State, long Size);
}
