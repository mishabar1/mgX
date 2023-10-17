using MG.Server.Services;
using System.Text;

namespace MG.Server.Entities
{
    public class BaseData<T>
    {
        public string Id { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string> Attributes { get; set; }

        public BaseData() {
            Id = Guid.NewGuid().ToString();
            Name = Utils.RandomName();
            Attributes = new Dictionary<string, string>();
        }

        public override string ToString()
        {
            return GetType().GetProperties()
                .Select(info => (info.Name, Value: info.GetValue(this, null) ?? "(null)"))
                .Aggregate(
                    new StringBuilder(),
                    (sb, pair) => sb.AppendLine($"{pair.Name}: {pair.Value}"),
                    sb => sb.ToString());
        }
    }
}
