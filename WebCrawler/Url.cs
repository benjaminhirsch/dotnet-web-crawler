using System.Net;

namespace WebCrawler;

internal struct Url
{
    public readonly string? url;
    public HttpStatusCode statusCode;

    public Url(string? url, HttpStatusCode statusCode)
    {
        this.url = url;
        this.statusCode = statusCode;
    }
}