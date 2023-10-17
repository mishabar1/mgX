﻿using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    public class DnDGameFlow : BaseGameFlow
    {
        public DnDGameFlow(GameData gameData) : base(gameData)
        {
        }

        public override async Task Setup()
        {
            Console.WriteLine("DnDGameFlow Setup " + this.GameData);
        }
        public override Task StartGame()
        {
            throw new NotImplementedException();
        }

        public override Task EndGame()
        {
            throw new NotImplementedException();
        }

    }
}
