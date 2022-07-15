using System.Collections;
using System.Collections.Specialized;
using System.Net;
using ConsoleTables;
using HtmlAgilityPack;

namespace WebCrawler;

internal static class Program
{
    private static readonly Queue<string?>? UrlsToParse = new();
    private static readonly List<Url> ParsedUrls = new();
    private static string _domain = null!;

    public static void Main(string?[] args)
    {
        if (args.Length <= 0) throw new Exception("Missing URL to parse, unable to proceed");

        UrlsToParse?.Enqueue(args[0]);
        _domain = args[0] ?? throw new InvalidOperationException();

        while (UrlsToParse!.Count > 0)
        {
            var url = UrlsToParse.Dequeue();

            if (url == null)
            {
                throw new Exception("Unable to dequeue url");
            }

            FetchUrl(url);
            Console.Write("\r{0}%", url);
        }
        
        Console.WriteLine("");
        Console.WriteLine("");
        
        Console.WriteLine("Parsed " + ParsedUrls.Count + " Urls total");

        var statusCodes = new OrderedDictionary();
        
        foreach (var parsedUrl in ParsedUrls)
        {
            if (!statusCodes.Contains(parsedUrl.statusCode.ToString()))
            {
                statusCodes[parsedUrl.statusCode.ToString()] = 1;
            }
            else
            {
                var currentCount = Convert.ToInt32(statusCodes[parsedUrl.statusCode.ToString()]!.ToString());
                statusCodes[parsedUrl.statusCode.ToString()] =  currentCount + 1 ;
            }
        }

        
        var table = new ConsoleTable("Status Code", "Quantity");
        foreach (DictionaryEntry statusCode in statusCodes)
        {
            table.AddRow(statusCode.Key, statusCode.Value);
        }
        
        table.Write();
    }

    private static void FetchUrl(string url)
    {
        url = NormalizeUrl(url);

        var web = new HtmlWeb();
        var statusCode = HttpStatusCode.OK;
        web.PostResponse += (_, response) =>
        {
            if (response != null) statusCode = response.StatusCode;
        };

        try
        {
            var htmlDoc = web.Load(url);
            var hrefTags = htmlDoc.DocumentNode.SelectNodes("//a[@href]")?
                .Select(link => link.Attributes["href"])
                .Select(att => att.Value).ToList();

            // Add parsed Url
            ParsedUrls.Add(new Url(url, statusCode));

            if (hrefTags != null)
                foreach (var tag in hrefTags)
                {
                    var normalizedUrl = NormalizeUrl(tag);

                    if (IsValid(normalizedUrl) &&
                        !ParsedUrls.Exists(u => u.url == normalizedUrl) &&
                        UrlsToParse != null &&
                        !UrlsToParse.Contains(normalizedUrl))
                    {
                        UrlsToParse?.Enqueue(normalizedUrl);
                    }
                }
        }
        catch (UriFormatException _)
        {
            Console.WriteLine(url);
        }
    }

    private static bool IsValid(string? url)
    {
        return url != null && 
               !url.Contains('#') &&
               !url.Contains("tel:") &&
               !url.Contains("mailto:") &&
               !url.StartsWith("javascript:") &&
               url.StartsWith(_domain) &&
               url.Length > 0;
    }

    private static string NormalizeUrl(string url)
    {
        if (!url.StartsWith(_domain))
        {
            // External URL
            if (url.StartsWith("http"))
            {
                return url;
            }
            return _domain + url;
        }

        return url;
    }
}