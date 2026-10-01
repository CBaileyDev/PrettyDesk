using System.Net;

namespace PrettyDesk.Core.Tests.Support;

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Ok(byte[] body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}
