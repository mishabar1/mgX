using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using System.Reflection;
using static MG.Server.GameFlows.TikTakToeGameFlow;

namespace MG.Server.GameFlows
{
    public abstract class BaseGameFlow
    {

        public GameData GameData { get; set; }
        public List<GameData> HistoryGameData { get; set; }


        public static GameData CreateGame(string gameType, string userId)
        {
            var game = new GameData();

            switch (gameType!)
            {
                case GameTypeEnum.TIK_TAK_TOE:
                    game.GameFlow = new TikTakToeGameFlow(game);
                    break;
                case GameTypeEnum.CHESS:
                    game.GameFlow = new ChessGameFlow(game);
                    break;
                case GameTypeEnum.DND:
                    game.GameFlow = new DnDGameFlow(game);
                    break;
                default:
                    break;
            }

            //switch (gameType)
            //{
            //    case GameTypeEnum.TIK_TAK_TOE.Name:
            //        break;

            //}

            game.GameStatus = GameStatusEnum.CREATED;
            game.CreatorId = userId;

            return game;
        }
        public BaseGameFlow(GameData gameData)
        {
            GameData = gameData;
            GameData.GameFlow = this;
        }

        public async Task RunSetupFlow()
        {
            // reset all            
            this.GameData.Table = ItemData.Table();
            this.GameData.Players = new List<PlayerData>();
            this.GameData.Winners = null;
            this.GameData.CurrentTurnId = null;
            this.GameData.GameStatus = GameStatusEnum.SETUP;

            await Setup();

            // reset history
            HistoryGameData = new List<GameData>() { GameData.DeepCopy() };

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);
        }
        public abstract Task Setup();

        public async Task RunStartFlow()
        {

            this.GameData.GameStatus = GameStatusEnum.PLAY;

            await StartGame();

            // create AI agents
            foreach (var player in this.GameData.Players)
            {
                if (player.Type == PlayerTypeEnum.AI)
                {
                    player.AIAgent = new AIAgent(this.GameData, player);
                }
            }

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);

        }
        public abstract Task StartGame();
        public abstract Task EndGame();

        public abstract Task<bool> IsEndGame();
        public abstract List<PlayerData> GetGameWinners();

        public async Task ExecuteAction(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow ExecuteAction ");

            data.Item = GameData.FindItem(data.itemId);
            data.Player = GameData.FindPlayer(data.playerId);
            if (data.Item != null && data.Player != null)
            {

                Type thisType = GetType();
                MethodInfo theMethod = thisType.GetMethod(data.actionId);
                await (Task)theMethod.Invoke(this, new object[] { data });
            }

            // check if game ended - 
            var ended = await IsEndGame();
            if (ended)
            {
                this.GameData.GameStatus = GameStatusEnum.ENDED;

                this.GameData.Winners = GetGameWinners();
                Console.WriteLine("TikTakToeGameFlow GAME ENDED !!!!!! winners count: " + this.GameData.Winners.Count());

                await EndGame();

            }

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);

        }

        internal AssetData addAsset(AssetData asset)
        {
            this.GameData.Assets.Add(asset.Name, asset);
            return asset;
        }
        
        internal ItemData addItem(AssetData asset)
        {
            var item = new ItemData(asset.Name, this.GameData.Table);
            return item;
        }
        //internal ItemData addItem(string assetKey)
        //{
        //    var item = new ItemData(assetKey, this.GameData.Table);
        //    return item;
        //}

        internal ItemData addTextItem(AssetData asset)
        {
            // TODO !!!!
            //this.GameData.Assets.TryAdd("TEXTBLOCK", new AssetData("", "", "TEXTBLOCK"));
            //var item = new ItemData("TEXTBLOCK", this.GameData.Table);
            var item = new ItemData(asset.Name, this.GameData.Table);
            //item.Text = text;
            return item;
        }

        internal ItemData playSound(AssetData asset, string playType="ONCE") // "ONCE" OR "LOOP"
        {
            var item = new ItemData(asset.Name, this.GameData.Table);
            item.PlayType = playType;
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
                if (idx == (this.GameData.Players.Count - 1))
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

        internal List<ItemData> getItemsByAsset(AssetData asset)
        {
            return ItemData.GetItemsByAsset(GameData.Table, asset);
        }
        internal void removeItemsByAsset(AssetData asset)
        {
            ItemData.GetItemsByAsset(GameData.Table, asset).ForEach(x => { removeItem(x.Id); });
        }

        internal List<ItemData> getItemsByAttribute(string key) {
            return ItemData.GetItemsByAttribute(this.GameData.Table, key);
        }



    }
}
