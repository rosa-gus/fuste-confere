using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fuste.Api.Updates;

const string repository = "https://github.com/rosa-gus/fuste-confere/releases";
var linux = new UpdateInstallation(new Version(0, 1, 9), "linux-x64", Enabled: true);
var windows = linux with { Rid = "win-x64" };
var linuxAsset = "fuste-confere-v0.1.10-linux-x64.tar.gz";
var windowsAsset = "fuste-confere-v0.1.10-win-x64.zip";

await ExpectStatus(linux, Release("v0.1.9"), "upToDate");
await ExpectStatus(linux, Release("v0.1.8"), "upToDate");
await ExpectStatus(linux, Release("v0.1.10-beta"), "unavailable");
await ExpectStatus(linux, Release("v0.01.10"), "unavailable");
await ExpectStatus(linux, Release("v0.1.10", assets: [windowsAsset]), "incompatible");
await ExpectStatus(linux, Release("v0.1.10", assets: [linuxAsset + ".sha256"]), "incompatible");
await ExpectStatus(linux, Release("v0.1.10", state: "new", assets: [linuxAsset]), "incompatible");
var windowsStatus = await ExpectStatus(windows, Release("v0.1.10", assets: [linuxAsset, windowsAsset]), "updateAvailable");
Check(windowsStatus.DownloadUrl == $"{repository}/download/v0.1.10/{windowsAsset}", "Wrong Windows download");

var conditionalRelease = Release("v0.1.10", assets: [linuxAsset, windowsAsset]);
conditionalRelease.Headers.ETag = new EntityTagHeaderValue("\"release-10\"");
var cache = new Responses(_ => conditionalRelease, request =>
{
    Check(request.Headers.IfNoneMatch.Single().Tag == "\"release-10\"", "Missing conditional request header");
    return new HttpResponseMessage(HttpStatusCode.NotModified);
});
var updates = new UpdateChecker(cache, linux);
Check(updates.Current.Status == "checking", "Incorrect initial status");
await updates.CheckAsync(default);
var available = updates.Current;
Check(available is { Status: "updateAvailable", LastCheckedAt: not null, InstalledVersion: "0.1.9", AvailableVersion: "0.1.10" }
    && available.ReleaseUrl == $"{repository}/tag/v0.1.10"
    && available.DownloadUrl == $"{repository}/download/v0.1.10/{linuxAsset}", "Incorrect Linux release metadata");
await updates.CheckAsync(default);
Check(updates.Current with { LastCheckedAt = available.LastCheckedAt } == available, "304 lost cached release");

await ExpectStatus(linux, new HttpResponseMessage(HttpStatusCode.NotFound), "unavailable");
await ExpectStatus(linux, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") }, "unavailable");
var failures = new UpdateChecker(new Responses(_ => throw new HttpRequestException("offline")), linux);
await failures.CheckAsync(default);
Check(failures.Current is { Status: "unavailable", DownloadUrl: null }, "Network failure was not isolated");
var timeout = new UpdateChecker(new Responses(_ => throw new TaskCanceledException("timeout")), linux);
await timeout.CheckAsync(default);
Check(timeout.Current.Status == "unavailable", "Timeout was not isolated");

var limitedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
limitedResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
await CheckRateLimit(limitedResponse);
var primaryLimit = new HttpResponseMessage(HttpStatusCode.Forbidden);
primaryLimit.Headers.Add("X-RateLimit-Remaining", "0");
primaryLimit.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());
await CheckRateLimit(primaryLimit);

foreach (var installation in new[] { linux with { Enabled = false }, linux with { Rid = null }, linux with { Version = null } })
{
    var factory = new Responses();
    var disabled = new UpdateChecker(factory, installation);
    await disabled.CheckAsync(default);
    Check(disabled.Current.Status == "disabled" && factory.RequestCount == 0, "Disabled checks contacted the feed");
}
Console.WriteLine("PASS update checks: version, platform, links, ETag, HTTP errors, rate limits and disabled checks.");

async Task CheckRateLimit(HttpResponseMessage response)
{
    var factory = new Responses(_ => response);
    var checker = new UpdateChecker(factory, linux);
    await checker.CheckAsync(default);
    await checker.CheckAsync(default);
    Check(checker.Current.Status == "unavailable" && factory.RequestCount == 1, "Rate limit was not respected");
}

static async Task<UpdateStatus> ExpectStatus(UpdateInstallation installation, HttpResponseMessage response, string status)
{
    var checker = new UpdateChecker(new Responses(_ => response), installation);
    await checker.CheckAsync(default);
    Check(checker.Current.Status == status && checker.Current.LastCheckedAt is not null,
        $"Expected {status}, got {checker.Current.Status}");
    return checker.Current;
}

static HttpResponseMessage Release(string tag, string state = "uploaded", params string[] assets) =>
    new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            tag_name = tag,
            draft = false,
            prerelease = false,
            assets = assets.Select(name => new { name, state, size = 10 }),
        }),
    };

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Responses(params Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler, IHttpClientFactory
{
    public int RequestCount { get; private set; }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri != "https://api.github.com/repos/rosa-gus/fuste-confere/releases/latest")
            throw new InvalidOperationException("Unexpected update feed");
        return Task.FromResult(replies[RequestCount++](request));
    }
}
