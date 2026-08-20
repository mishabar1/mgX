using System.Security.Cryptography;
using System.Text;

namespace MG.Server.Entities
{
    public class UserData : BaseData<UserData>
    {

        public UserData():base()
        {
           
        }

        // ------------------------------------------------------------------------------
        // STABLE IDENTITY.
        //
        // Login is by name only, and a user's id is what everything else hangs off: a game's
        // CreatorId, each seat's User.Id, the hub's caller check, "lastHumanActor". BaseData's
        // constructor hands out a fresh random Guid, so a user created on Monday and the "same"
        // user created again on Tuesday were two different people to the server — you'd log back
        // in as "A" and no longer be the creator of your own game.
        //
        // Deriving the id from the name FROM THE NAME ITSELF removes the dependency on the store
        // surviving: "A" is the same id on every machine, after every restart, and after the
        // SQLite file in the OS temp folder is cleaned up or the container is redeployed. Same
        // reasoning as the deterministic asset keys in AssetData — a random key regenerated on
        // restart left persisted games pointing at something that no longer existed.
        //
        // Matching is EXACT: "A" and "a" are deliberately different users.
        // ------------------------------------------------------------------------------

        // Fixed namespace for MGx user ids. Changing this re-issues every id, so don't.
        private static readonly Guid UserNamespace = new("7c9e6f1a-2d3b-4c5e-8a71-9f0b3d6e2a44");

        /// <summary>
        /// The id a user with this exact name always gets. RFC 4122 name-based (v5) UUID.
        /// <para>
        /// PINNED REFERENCE VALUES — if these ever change, every persisted CreatorId and
        /// seat.User.Id in every saved game is orphaned, so treat a change here as a migration:
        ///   IdForName("A")     == "d505b1d0-129f-5032-8897-fbebb8676e9b"
        ///   IdForName("a")     == "782733a0-8f7e-5dc5-95b3-ce167bccddf1"
        ///   IdForName("Misha") == "0bd36499-cc38-5072-af98-6896aa4d5afa"
        /// </para>
        /// </summary>
        public static string IdForName(string name)
        {
            var ns = UserNamespace.ToByteArray();
            // Guid.ToByteArray() gives the first three fields little-endian; RFC 4122 hashes the
            // big-endian form, so flip them before hashing (and back again after).
            SwapEndianness(ns);

            var nameBytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
            var input = new byte[ns.Length + nameBytes.Length];
            Buffer.BlockCopy(ns, 0, input, 0, ns.Length);
            Buffer.BlockCopy(nameBytes, 0, input, ns.Length, nameBytes.Length);

            var hash = SHA1.HashData(input);

            var bytes = new byte[16];
            Array.Copy(hash, 0, bytes, 0, 16);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant

            SwapEndianness(bytes);
            return new Guid(bytes).ToString();
        }

        private static void SwapEndianness(byte[] g)
        {
            (g[0], g[3]) = (g[3], g[0]);
            (g[1], g[2]) = (g[2], g[1]);
            (g[4], g[5]) = (g[5], g[4]);
            (g[6], g[7]) = (g[7], g[6]);
        }
    }
}