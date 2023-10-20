using System.Text;

namespace MG.Server.Services
{
    public class Utils
    {

        public static string RandomName()
        {
            return RandomNamesUtil.GetName();
        }

        public static string ListProperties(object obj)
        {
            return obj.GetType().GetProperties()
                .Select(info => (info.Name, Value: info.GetValue(obj, null) ?? "(null)"))
                .Aggregate(
                    new StringBuilder(),
                    (sb, pair) => sb.AppendLine($"{pair.Name}: {pair.Value}"),
                    sb => sb.ToString());
        }

    }
}
