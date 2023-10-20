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

        internal BaseData<T> AddAttribute(string key)
        {
            return AddAttribute(key, "TRUE");
        }
        internal BaseData<T> AddAttribute(string key, string val)
        {
            Attributes.Add(key, val);
            return this;
        }
        internal BaseData<T> AddAttribute(string key, double val)
        {
            Attributes.Add(key, val.ToString());
            return this;
        }

        internal double GetNumberAddAttribute(string key)
        {            
            return Convert.ToDouble(Attributes.GetValueOrDefault(key)!);
        }

        internal bool HaveAttribute(string key)
        {
            return Attributes.ContainsKey(key);
        }
        public override string ToString()
        {
            return Utils.ListProperties(this);
        }
    }
}
