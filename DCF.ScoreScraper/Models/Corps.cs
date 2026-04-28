namespace DCF.ScoreScraper.Models
{
    public class Corps
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; } = string.Empty;

        public Corps(string name)
        {
            Id= Guid.NewGuid();
            Name = name;
        }

        public Corps(Guid id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
