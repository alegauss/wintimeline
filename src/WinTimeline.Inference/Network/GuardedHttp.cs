using System.Net;
using WinTimeline.Diagnostics;

namespace WinTimeline.Inference.Network;

/// <summary>Thrown when the app asks for a destination the local-only promise does not allow.</summary>
public sealed class NetworkRefusedException : HttpRequestException
{
    /// <summary>Creates the exception.</summary>
    public NetworkRefusedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public NetworkRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">The cause.</param>
    public NetworkRefusedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// WT103: the app's only way onto the network. Media, transcripts and prompts never leave the
/// PC because nothing can carry them: a request goes out only to a model host the catalog names
/// or to the update feed, every redirect hop is checked the same way, and air-gapped mode refuses
/// everything. A refusal is logged with the host it named, never with what it carried.
/// </summary>
public sealed class GuardedHttp : DelegatingHandler
{
    /// <summary>The hosts model files come from: Hugging Face and the CDNs its resolve URLs redirect to.</summary>
    public static readonly IReadOnlyList<string> ModelHosts = ["huggingface.co", "*.huggingface.co", "*.hf.co"];

    /// <summary>The hosts the update feed and its release assets come from.</summary>
    public static readonly IReadOnlyList<string> UpdateHosts = ["github.com", "api.github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    private const int MostRedirects = 5;
    private readonly IReadOnlyList<string> _allowed;
    private readonly Func<bool> _airGapped;

    /// <summary>Guards a handler that must not follow redirects itself.</summary>
    /// <param name="allowed">The hosts allowed, a leading "*." matching any subdomain.</param>
    /// <param name="airGapped">Whether every request is refused, read per request so the setting applies at once.</param>
    /// <param name="inner">What sends the request; its own redirects off, so each hop comes back here.</param>
    public GuardedHttp(IReadOnlyList<string> allowed, Func<bool> airGapped, HttpMessageHandler inner)
        : base(inner)
    {
        _allowed = allowed ?? throw new ArgumentNullException(nameof(allowed));
        _airGapped = airGapped ?? throw new ArgumentNullException(nameof(airGapped));
    }

    /// <summary>The app's client: model hosts and the update feed, nothing else.</summary>
    /// <param name="airGapped">Whether downloads are off, from the settings.</param>
    /// <returns>The client; one per app, kept for its life.</returns>
    public static HttpClient CreateClient(Func<bool> airGapped) =>
        new(new GuardedHttp([.. ModelHosts, .. UpdateHosts], airGapped, new SocketsHttpHandler { AllowAutoRedirect = false }))
        {
            DefaultRequestHeaders = { { "User-Agent", "WinTimeline" } },
        };

    /// <summary>Whether a host is one the guard lets through.</summary>
    /// <param name="host">The host.</param>
    /// <param name="allowed">The allowed patterns.</param>
    /// <returns>True when a pattern names it.</returns>
    public static bool Allows(string host, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(allowed);
        return allowed.Any(pattern => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (int hop = 0; ; hop++)
        {
            Check(request.RequestUri!);
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
            {
                return response;
            }

            if (hop >= MostRedirects)
            {
                response.Dispose();
                throw new HttpRequestException($"{request.RequestUri!.Host} redirected more than {MostRedirects} times");
            }

            Uri next = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
            response.Dispose();
            HttpRequestMessage follow = new(request.Method == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get, next);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers.Where(h => h.Key is "Range" or "User-Agent"))
            {
                follow.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request = follow;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private void Check(Uri uri)
    {
        string? refusal = _airGapped() ? "downloads are off (air-gapped mode)"
            : uri.Scheme != Uri.UriSchemeHttps ? $"{uri.Scheme} is not https"
            : !Allows(uri.Host, _allowed) ? $"{uri.Host} is neither a model host nor the update feed"
            : null;
        if (refusal is null)
        {
            return;
        }

        Log.Write(LogLevel.Warning, LogCategory.App, "A network request was refused.", new Dictionary<string, string>(StringComparer.Ordinal) { ["host"] = uri.Host, ["why"] = refusal });
        throw new NetworkRefusedException($"WinTimeline does not send anything to {uri.Host}: {refusal}.");
    }
}
