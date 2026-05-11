using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public class CorpsService : ICorpsService
{
    private readonly Dictionary<string, Corps> _corps;

    public CorpsService(IEnumerable<Corps> corps)
    {
        _corps = corps.ToDictionary(c => c.Name, c => c);
    }

    public IReadOnlyDictionary<string, Corps> GetCorps() => _corps;
}
