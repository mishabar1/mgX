using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
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
                case GameTypeEnum.CHESS:
                    game.GameFlow = new ChessGameFlow(game);
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

        public async Task RunSetupFlow()
        {
            // reset all
            this.GameData.Assets = new Dictionary<string, AssetData>();
            this.GameData.Table = ItemData.Table();
            this.GameData.Players = new List<PlayerData>();
            this.GameData.Winners = null;
            this.GameData.CurrentTurnId = null;
            this.GameData.GameStatus = GameStatusEnum.SETUP;
            
            await Setup();

            await DataRepository.Singeltone.HubGameUpdated(GameData);
            await DataRepository.Singeltone.HubGamesUpdated(GameData);
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

            await DataRepository.Singeltone.HubGameUpdated(GameData);
            await DataRepository.Singeltone.HubGamesUpdated(GameData);

        }
        public abstract Task StartGame();
        public abstract Task EndGame();

        public abstract Task<bool> IsEndGame();
        public abstract List<PlayerData> GetGameWinners();

        public async Task ExecuteAction(ExecuteActionData data)
        {
            data.Item = GameData.FindItem(data.itemId);
            data.Player = GameData.FindPlayer(data.playerId);
            if(data.Item != null && data.Player != null)
            {
                Console.WriteLine("TikTakToeGameFlow ExecuteAction " + data);
                Type thisType = GetType();
                MethodInfo theMethod = thisType.GetMethod(data.actionId);
                await (Task)theMethod.Invoke(this, new object[] { data });
            }

            // check if game ended - 
            var ended = await IsEndGame();
            if (ended)
            {
                this.GameData.GameStatus = GameStatusEnum.ENDED;
                
                await EndGame();

                this.GameData.Winners = GetGameWinners();
                Console.WriteLine("TikTakToeGameFlow GAME ENDED !!!!!! winners count: " + this.GameData.Winners.Count());

            }

            await DataRepository.Singeltone.HubGameUpdated(GameData);
            await DataRepository.Singeltone.HubGamesUpdated(GameData);

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
                if(idx == (this.GameData.Players.Count-1))
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
