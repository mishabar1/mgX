using MG.Server.BL;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class Item
    {
        public string Id { get; set; }
        public string? Name { get; set; }

        public Asset Asset { get; set; }


        public string GameId { get { return Game.Id; } }
        [JsonIgnore] public GameData Game { get; set; }





        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; }



        public string? UserData { get; set; }



        public List<Item> Items { get; set; }



        public string? ParentItemId { get { return ParentItem?.Id; } }
        [JsonIgnore] public Item? ParentItem { get; set; }


        public Item(GameData game, Asset asset, Item? parentItem = null)
        {
            Id = Guid.NewGuid().ToString();
            Name = Utils.RandomName();

            Asset = asset;

            Game = game;
            Game.Items.Add(this);


            ParentItem = parentItem;
            if (ParentItem != null)
            {
                ParentItem.Items.Add(this);
            }

            Items = new List<Item>();

            Position = new Vector3();
            Rotation = new Vector3();
            Scale = new Vector3(1);

        }
    }

}