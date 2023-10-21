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

        internal double GetNumberAttribute(string key)
        {            
            return Convert.ToDouble(Attributes.GetValueOrDefault(key)!);
        }
        internal int GetIntAttribute(string key)
        {
            return Convert.ToInt32(Attributes.GetValueOrDefault(key)!);
        }
        internal string GetStringAttribute(string key)
        {
            return Attributes.GetValueOrDefault(key);
        }

        internal bool HaveAttribute(string key)
        {
            return Attributes.ContainsKey(key);
        }
        internal bool HaveAttribute(string key,string val)
        {
            return Attributes.ContainsKey(key) && Attributes.GetValueOrDefault(key)==val;
        }
        public override string ToString()
        {
            return Utils.ListProperties(this);
        }
    }
}
