using MG.Server.Entities;

namespace MG.Server.BL
{
    public class DataRepository
    {
        List<User> Users;// = new List<User>();
        List<GameData> Games;// = new List<GameData>();

        public DataRepository()
        {
            Users = new List<User>();
            Games = new List<GameData>();

            //using (var context = new AppDbContext())
            //{
            //    var authors = new List<Author>
            //    {
            //        new Author
            //        {
            //            FirstName ="Joydip",
            //            LastName = "Kanjilal",
            //            Books =new List<Book>()
            //            {
            //                new Book { Title = "Mastering C# 8.0"},
            //                new Book { Title = "Entity Framework Tutorial"},
            //                new Book { Title = "ASP.NET 4.0 Programming"}
            //            }
            //    },
            //    new Author
            //    {
            //        FirstName ="Yashavanth",
            //        LastName ="Kanetkar",
            //        Books = new List<Book>()
            //        {
            //            new Book { Title = "Let us C"},
            //            new Book { Title = "Let us C++"},
            //            new Book { Title = "Let us C#"}
            //        }
            //    }
            //    };
            //    context.Authors.AddRange(authors);
            //    context.SaveChanges();
            //}
        }
        //public List<Author> GetAuthors()
        //{
        //    using (var context = new AppDbContext())
        //    {
        //        var list = context.Authors
        //            .Include(x => x.Books)
        //            .ToList();
        //        return list;
        //    }
        //}

        internal async Task<List<GameData>> test1()
        {

            //using (var context = new AppDbContext())
            {

                //    var game = new GameData
                //    {
                //        Name = "gam x",
                //        GameType = GameTypeEnum.TIK_TAK_TOE,
                //        Items = new List<Item>()
                //        //LastName = "Kanjilal",
                //        //Books = new List<Book>()
                //        //{
                //        //    new Book { Title = "Mastering C# 8.0" },
                //        //    new Book { Title = "Entity Framework Tutorial" },
                //        //    new Book { Title = "ASP.NET 4.0 Programming" }
                //        //}


                //    };

                //    var i1 = new Item { Name = "i 1", };
                //    var i2 = new Item { Name = "i 2", ParentItem = i1 };
                //    var i3 = new Item { Name = "i 3", ParentItem = i1 };
                //    var i4 = new Item { Name = "i 3", ParentItem = i2 };

                //    game.Items.Add(i1);
                //    game.Items.Add(i2);
                //    game.Items.Add(i3);
                //    game.Items.Add(i4);

                //    context.Games.Add(game);
                //    await context.SaveChangesAsync();

                var list = Games
                    //.Where(x => x.Id > 0)
                    .ToList();

                return list;

                //return game;

            }

        }

        internal async Task<object?> test2()
        {
            //using (var context = new AppDbContext())
            {

                var game = new GameData
                {
                    Name = "gam x",
                    GameType = GameTypeEnum.DND,
                };

                new Player(game) { Type = PlayerTypeEnum.HUMAN };
                new Player(game) { Type = PlayerTypeEnum.AI };
                new Player(game) { Type = PlayerTypeEnum.AI };


                var asset = new Asset();

                var i1 = new Item(game, asset) { };
                var i2 = new Item(game, asset, i1) { };
                var i3 = new Item(game, asset, i1) { };
                var i4 = new Item(game, asset, i2) { };

                i1.Position = new Vector3(1, 2, 3);
                i2.Scale.Y = 999;

                Games.Add(game);



                return game;

            }
        }
    }
}