namespace DCF.Api.Scraping;

public interface IHtmlFetcher
{
    Task<string> FetchAsync(string url);
}
