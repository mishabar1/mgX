namespace MG.Server.Entities
{
    public class Asset
    {
        public string Id { get; set; }
        public string? Name { get; set; }
        public Asset()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
