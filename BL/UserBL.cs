using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;

namespace MG.Server.BL
{
    public class UserBL
    {
        DataRepository _dataRepository;
        public UserBL(DataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        internal async Task<UserData> Login(LoginData data)
        {

           var user = _dataRepository.Users.FindLast(x => x.Name == data.name);
            if (user == null)
            {
                user = new UserData() { Name = data.name };
                _dataRepository.Users.Add(user);
            }

            return user;

        }
    }
}
