using ConflictCommon.Classes.DTOs;
using Neo4j.Driver;
using System.Data;
using System.Diagnostics;
using System.Numerics;
using System.Xml.Linq;

namespace ConflictCommon.Classes.StaticHelpers
{
    public  class Neo4JHelper
    {
       // public static object GraphDatabase { get; private set; }

        public static async Task<string[]> GetDatabaseNamesAsync(string uri, string user, string password)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            var databases = new List<string>();

            await using (var session = driver.AsyncSession(o => o.WithDatabase("system")))
            {
                var cursor = await session.RunAsync("SHOW DATABASES");

                while (await cursor.FetchAsync())
                {
                    // Neo4j returns a record with fields like: name, type, access, etc.
                    var name = cursor.Current["name"].As<string>();
                    databases.Add(name);
                }
            }

            await driver.DisposeAsync();

            return databases.ToArray();
        }

        public static bool IsNeo4jDesktopRunning()
        {
            var processes = Process.GetProcessesByName("Neo4j Desktop 2");
            return processes.Length > 0;
        }
    }
}
