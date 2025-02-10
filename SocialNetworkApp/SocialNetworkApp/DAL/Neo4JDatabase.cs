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

            Console.WriteLine("Données existantes supprimées (produits conservés).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de la suppression des données : {ex.Message}");
        }
    }

    public async Task AddUsers(int userCount)
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

            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("UNWIND $userNames AS name CREATE (:User {name: name})",
                                  new { userNames });
            });

            Console.WriteLine($"{userCount} utilisateurs ajoutés.");

            var productNames = new List<string>();
            await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync("MATCH (p:Product) RETURN p.name AS productName");
                var records = await result.ToListAsync();
                foreach (var record in records)
                {
                    productNames.Add(record["productName"].As<string>());
                }
            });

            if (productNames.Count == 0)
            {
                throw new Exception("Aucun produit trouvé dans la base de données !");
            }

            var followRelationships = new List<(string, string)>();
            var purchaseRelationships = new List<(string, string)>();

            foreach (var userName in userNames)
            {
                var followerCount = random.Next(0, 21);
                for (int j = 0; j < followerCount; j++)
                {
                    var followedName = userNames[random.Next(userNames.Count)];
                    if (followedName == userName) continue;
                    followRelationships.Add((userName, followedName));
                }
                var purchaseCount = random.Next(0, 6);
                for (int j = 0; j < purchaseCount; j++)
                {
                    var productName = productNames[random.Next(productNames.Count)];
                    purchaseRelationships.Add((userName, productName));
                }
            }

            await ExecuteBatchAsync(session, "FOLLOWS", followRelationships, batchSize);
            await ExecuteBatchAsync(session, "BOUGHT", purchaseRelationships, batchSize);

            Console.WriteLine("Relations ajoutées avec succès.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'ajout : {ex.Message}");
        }
    }

    private async Task ExecuteBatchAsync(IAsyncSession session, string relationType, List<(string, string)> relationships, int batchSize)
    {
        var relationQuery = relationType == "FOLLOWS"
            ? @"UNWIND $relationships AS rel
           MATCH (a:User) WHERE a.name = rel.Item1
           MATCH (b:User) WHERE b.name = rel.Item2
           CREATE (a)-[:FOLLOWS]->(b)"
            : @"UNWIND $relationships AS rel
           MATCH (a:User) WHERE a.name = rel.Item1
           MATCH (p:Product) WHERE p.name = rel.Item2
           CREATE (a)-[:BOUGHT]->(p)";

        for (int i = 0; i < relationships.Count; i += batchSize)
        {
            var batch = relationships.Skip(i).Take(batchSize).ToList();
            var structuredBatch = batch.Select(rel => new { Item1 = rel.Item1, Item2 = rel.Item2 }).ToList();

            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(relationQuery, new { relationships = structuredBatch });
            });
        }
    }

    public void Dispose()
    {
        _driver.Dispose();
        GC.SuppressFinalize(this); 
    }
}