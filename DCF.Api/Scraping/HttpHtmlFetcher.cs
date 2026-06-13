namespace DCF.Api.Scraping;

public class HttpHtmlFetcher : IHtmlFetcher
{
    private readonly HttpClient _httpClient;

    public HttpHtmlFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> FetchAsync(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Show URL must use HTTPS or http://localhost: {url}");
        }

        return await _httpClient.GetStringAsync(url);
    }
}
