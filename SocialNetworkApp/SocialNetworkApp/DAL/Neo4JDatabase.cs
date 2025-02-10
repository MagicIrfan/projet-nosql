using Neo4j.Driver;

namespace SocialNetworkApp.DAL;

public class Neo4JDatabase(string uri, string user, string password) : IDisposable
{
    private readonly IDriver _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

    private IAsyncSession GetSession()
    {
        return _driver.AsyncSession();
    }
    
    public async Task<List<(string ProductName, int Count)>> GetProductsOrderedByFollowers(string name, int depth)
    {
        var products = new List<(string ProductName, int Count)>();

        await using var session = GetSession();

        var query = $@"
        MATCH (u:User {{name: '{name}'}})<-[:FOLLOWS*1..{depth}]-(f:User)
        MATCH (f)-[:BOUGHT]->(p:Product)
        WITH p.name AS ProductName, COLLECT(DISTINCT f.name) AS Buyers
        RETURN ProductName, SIZE(Buyers) AS ProductCount
        ORDER BY ProductCount DESC;
        ";

        var result = await session.RunAsync(query, new { name });

        while (await result.FetchAsync())
        {
            products.Add((result.Current["ProductName"].As<string>(), result.Current["ProductCount"].As<int>()));
        }


        return products;
    }
    
    public async Task<int> GetNbUserOrderedProductByDepth(string name, int depth)
    {
        await using var session = GetSession();

        var query = $@"
        MATCH (u:User)-[:BOUGHT]->(p:Product {{name: '{{name}}'}})
        WITH p, COLLECT(DISTINCT u) AS level0Users

        MATCH (f:User)-[:FOLLOWS*{{depth}}]->(target:User)-[:BOUGHT]->(p)
        WHERE target IN level0Users
        AND EXISTS {{ MATCH (f)-[:BOUGHT]->(p) }} 
        RETURN COUNT(DISTINCT f) AS UserCount;
        ";

        var result = await session.RunAsync(query, new { name });

        if (await result.FetchAsync()) 
        {
            return result.Current["UserCount"].As<int>();
        }

        return 0;
    }
    
    public async Task<List<(string ProductName, int Count)>> GetProductsOrderedByFollowersFilteredByProduct(string name, int depth, string product)
    {
        var products = new List<(string ProductName, int Count)>();

        await using var session = GetSession();

        var query = $@"
            MATCH (u:User {{name: '{name}'}})<-[:FOLLOWS*1..{depth}]-(f:User)
            MATCH (f)-[:BOUGHT]->(p:Product {{name: '{product}'}})
            WITH p.name AS ProductName, COLLECT(DISTINCT f.name) AS Buyers
            RETURN ProductName, SIZE(Buyers) AS ProductCount
            ORDER BY ProductCount DESC;
        ";


        var result = await session.RunAsync(query, new { name });

        while (await result.FetchAsync())
        {
            products.Add((result.Current["ProductName"].As<string>(), result.Current["ProductCount"].As<int>()));
        }


        return products;
    }
    
   public void AddUsers(int userCount)
    {
        using var session = GetSession();

        var result = session.RunAsync("MATCH (u:User) RETURN COUNT(u) AS currentUserCount").Result;
        result.FetchAsync().Wait();
        var currentUserCount = result.Current["currentUserCount"].As<int>();

        using var tx = session.BeginTransactionAsync().Result;
        
        try
        {
            var userNames = new List<string>();
            var followerRelationships = new List<string>();
            var purchaseRelationships = new List<string>();

            for (var i = 0; i < userCount; i++)
            {
                userNames.Add($"'User{currentUserCount + i + 1}'");
            }

            tx.RunAsync($"UNWIND [{string.Join(", ", userNames)}] AS name CREATE (:User {{name: name}});").Wait();

            for (var i = 0; i < userCount; i++)
            {
                var followerCount = new Random().Next(0, 21);
                for (var j = 0; j < followerCount; j++)
                {
                    var followedId = new Random().Next(1, currentUserCount + userCount + 1);
                    var followerName = $"User{currentUserCount + i + 1}";
                    var followedName = $"User{currentUserCount + followedId}";
                    followerRelationships.Add($"MATCH (a:User {{name: '{followerName}'}}), (b:User {{name: '{followedName}'}}) CREATE (a)-[:FOLLOWS]->(b)");
                }
            }

            if (followerRelationships.Count != 0)
            {
                foreach (var relationship in followerRelationships)
                {
                    tx.RunAsync(relationship).Wait();
                }
            }

            for (var i = 0; i < userCount; i++)
            {
                var purchaseCount = new Random().Next(0, 6);
                for (var j = 0; j < purchaseCount; j++)
                {
                    var productId = new Random().Next(1, 6);
                    var userName = $"User{currentUserCount + i + 1}";
                    var productName = $"Product{productId}";
                    purchaseRelationships.Add($"MATCH (u:User {{name: '{userName}'}}), (p:Product {{name: '{productName}'}}) CREATE (u)-[:BOUGHT]->(p)");
                }
            }

            if (purchaseRelationships.Count != 0)
            {
                foreach (var relationship in purchaseRelationships)
                {
                    tx.RunAsync(relationship).Wait();
                }
            }

            tx.CommitAsync().Wait();
        }
        catch (Exception ex)
        {
            tx.RollbackAsync().Wait();
            Console.WriteLine($"Erreur lors de l'ajout : {ex.Message}");
        }
    }




    public void Dispose()
    {
        _driver.Dispose();
        GC.SuppressFinalize(this); 
    }
}