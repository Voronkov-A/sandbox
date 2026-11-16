namespace Picshare.Services;

/// <summary>All live AI providers must use this guarded network client.</summary>
public static class AiHttpClientFactory
{
    public const string DisableLiveRequestsSwitch = "Picshare.DisableLiveAiRequests";

    public static HttpClient Create() => new(new LiveAiRequestHandler()) { Timeout = TimeSpan.FromMinutes(5) };

    private sealed class LiveAiRequestHandler : DelegatingHandler
    {
        public LiveAiRequestHandler() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EnsureLiveRequestsAllowed();
            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EnsureLiveRequestsAllowed();
            return base.Send(request, cancellationToken);
        }

        private static void EnsureLiveRequestsAllowed()
        {
            if (AppContext.TryGetSwitch(DisableLiveRequestsSwitch, out var disabled) && disabled)
                throw new InvalidOperationException("Live AI requests are disabled in this process. Tests must use a fake AI provider or HTTP handler.");
        }
    }
}
