using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services
{
    public interface ICorpsSevice
    {
        Dictionary<string, Corps> GetCorps();
    }

    public class CorpsSevice : ICorpsSevice
    {
        public Dictionary<string, Corps> GetCorps()
        {
            throw new NotImplementedException();
        }
    }
}
