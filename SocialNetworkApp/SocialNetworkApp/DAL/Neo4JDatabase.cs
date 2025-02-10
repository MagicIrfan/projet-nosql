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

    public void DeleteExistingData()
    {
        using var session = GetSession();

        try
        {
            session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("MATCH (u:User)-[r]->(n) DELETE r;");

                await tx.RunAsync("MATCH (u:User) DELETE u;");
            }).Wait();

            Console.WriteLine("✅ Données existantes supprimées (produits conservés).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erreur lors de la suppression des données : {ex.Message}");
        }
    }

    public void AddUsers(int userCount)
    {
        using var session = GetSession();
        var random = new Random();

        try
        {
            int batchSize = userCount / 10;
            if (batchSize == 0) batchSize = 1;

            var userNames = Enumerable.Range(1, userCount)
                                      .Select(i => $"User{i}")
                                      .ToList();

            session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("UNWIND $userNames AS name CREATE (:User {name: name})",
                                  new { userNames });
            }).Wait();

            Console.WriteLine($"{userCount} utilisateurs ajoutés.");

            var insertedUserIds = new List<int>();

            session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync("MATCH (u:User) WHERE u.name IN $userNames RETURN u.name AS name, id(u) AS userId",
                    new { userNames });
                var records = await result.ToListAsync();
                foreach (var record in records)
                {
                    insertedUserIds.Add(record["userId"].As<int>());
                }
            }).Wait();

            var followRelationships = new List<(int, int)>();
            var purchaseRelationships = new List<(int, int)>();

            foreach (var userId in insertedUserIds)
            {
                var followerCount = random.Next(0, 21);
                for (int j = 0; j < followerCount; j++)
                {
                    var followedIndex = insertedUserIds[random.Next(insertedUserIds.Count)];
                    if (followedIndex == userId) continue;
                    followRelationships.Add((userId, followedIndex));
                }
                var purchaseCount = random.Next(0, 6);
                for (int j = 0; j < purchaseCount; j++)
                {
                    var productId = random.Next(1, 6);
                    purchaseRelationships.Add((userId, productId));
                }
            }

            ExecuteBatch(session, "FOLLOWS", followRelationships, batchSize);
            ExecuteBatch(session, "BOUGHT", purchaseRelationships, batchSize);

            Console.WriteLine("Relations ajoutées avec succès.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'ajout : {ex.Message}");
        }
    }

    private void ExecuteBatch(IAsyncSession session, string relationType, List<(int, int)> relationships, int batchSize)
    {
        for (int i = 0; i < relationships.Count; i += batchSize)
        {
            var batch = relationships.Skip(i).Take(batchSize).ToList();
            session.ExecuteWriteAsync(async tx =>
            {
                string relationQuery = relationType == "FOLLOWS"
                    ? @"UNWIND $relationships AS rel
                    MATCH (a:User) WHERE id(a) = rel.Item1
                    MATCH (b:User) WHERE id(b) = rel.Item2
                    CREATE (a)-[:FOLLOWS]->(b)"
                    : @"UNWIND $relationships AS rel
                    MATCH (a:User) WHERE id(a) = rel.Item1
                    MATCH (p:Product) WHERE p.ProductId = rel.Item2
                    CREATE (a)-[:BOUGHT]->(p)";

                var structuredBatch = batch.Select(rel => new { Item1 = rel.Item1, Item2 = rel.Item2 }).ToList();

                await tx.RunAsync(relationQuery, new { relationships = structuredBatch });
            }).Wait();
        }
    }

    public void Dispose()
    {
        _driver.Dispose();
        GC.SuppressFinalize(this); 
    }
}