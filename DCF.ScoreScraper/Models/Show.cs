namespace DCF.ScoreScraper.Models
{
    public class Show
    {
        public Guid Id { get; private set; }
        public string Name { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public DateTime Date { get; set; }

        public Show()
        { 
            Id = Guid.NewGuid();
        }

        public Show(string name, string url, DateTime date)
        {
            Id = Guid.NewGuid();
            Name = name;
            URL = url;
            Date = date;
        }

        public Show(Guid id, string name, string url, DateTime date)
        {
            Id = id;
            Name = name;
            URL = url;
            Date = date;
        }
    }
}
