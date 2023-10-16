using MG.Server.Entities;

namespace MG.Server.Database
{
    public class DataRepository
    {
        public List<UserData> Users;// = new List<UserData>();
        public List<GameData> Games;// = new List<GameData>();

        public DataRepository()
        {
            Users = new List<UserData>();
            Games = new List<GameData>();

        }


        
    }
}