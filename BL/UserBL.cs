using MG.Server.Database;

namespace MG.Server.BL
{
    public class UserBL
    {
        DataRepository _dataRepository;
        public UserBL(DataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }
    }
}
