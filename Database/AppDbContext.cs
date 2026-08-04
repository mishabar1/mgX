using Microsoft.EntityFrameworkCore;

namespace MG.Server.Database
{
    /// <summary>
    /// One row per top-level collection, stored as a single JSON document.
    /// This deliberately avoids a fragile relational mapping of the rich, tree-shaped
    /// game object graph (GameData -> ItemData tree, polymorphic AssetData, players, etc.)
    /// while still giving durable storage. Keys used: "users" and "games".
    /// </summary>
    public class StoreRecord
    {
        public string Key { get; set; } = string.Empty;
        public string Json { get; set; } = string.Empty;
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<StoreRecord> Store => Set<StoreRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StoreRecord>().HasKey(x => x.Key);
        }
    }
}
