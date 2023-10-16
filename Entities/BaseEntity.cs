using MG.Server.Services;

namespace MG.Server.Entities
{
    public class BaseEntity<T>
    {
        public string Id { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string> Attributes { get; set; }

        public BaseEntity() {
            Id = Guid.NewGuid().ToString();
            Name = Utils.RandomName();
            Attributes = new Dictionary<string, string>();
        }
    }
}
