namespace MG.Server.Entities
{
    public class User
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public User()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}