using MG.Server.Controllers;
using MG.Server.Entities;
using System.Reflection;

namespace MG.Server.GameFlows
{
    public abstract class BaseGameFlow
    {

        public GameData GameData { get; set; }


        public static GameData CreateGame(string gameType,string userId)
        {
            var game = new GameData();
            switch (gameType)
            {
                case GameTypeEnum.TIK_TAK_TOE:
                    game.GameFlow = new TikTakToeGameFlow(game);
                    break;
                case GameTypeEnum.CATAN:
                    game.GameFlow = new CatanGameFlow(game);
                    break;
                case GameTypeEnum.DND:
                    game.GameFlow = new DnDGameFlow(game);
                    break;
                default:
                    break;
            }

            game.GameStatus = GameStatusEnum.CREATED;
            game.CreatorId = userId;
            return game;
        }
        public BaseGameFlow(GameData gameData)
        {
            GameData = gameData;
            GameData.GameFlow = this;
        }
        public abstract Task Setup();
        public abstract Task StartGame();
        public abstract Task EndGame();

        public abstract bool IsEndGame();

        
        public async Task ExecuteAction(ExecuteActionData data)
        {
            data.Item = GameData.FindItem(data.itemId);
            data.Player = GameData.FindPlayer(data.playerId);
            if(data.Item != null && data.Player != null)
            {
                Console.WriteLine("TikTakToeGameFlow ExecuteAction " + data);
                Type thisType = GetType();
                MethodInfo theMethod = thisType.GetMethod(data.actionId);
                theMethod.Invoke(this, new object[] { data });
            }



            
        }

        internal void addAsset(string assetKey, AssetData asset)
        {
            this.GameData.Assets.Add(assetKey, asset);
        }

        internal ItemData addItem(string assetKey)
        {
            var item = new ItemData(assetKey,this.GameData.Table);
            return item;
        }
        internal ItemData addItem(string assetKey, ItemData parentItem)
        {
            var item = new ItemData(assetKey, parentItem);
            return item;
        }

        internal void removeItem(string itemId)
        {
            this.GameData.Table.RemoveItem(itemId);
        }

        internal void advanceNextTurn()
        {
            if (this.GameData.CurrentTurnId == null)
            {
                this.GameData.CurrentTurnId = this.GameData.Players.First().Id;
            }
            else
            {
                var idx = this.GameData.Players.FindIndex(x => x.Id == this.GameData.CurrentTurnId);
                if(idx== this.GameData.Players.Count)
                {
                    idx = 0;
                }
                else
                {
                    idx++;
                }
                this.GameData.CurrentTurnId = this.GameData.Players[idx].Id;
            }
        }
    }
}
