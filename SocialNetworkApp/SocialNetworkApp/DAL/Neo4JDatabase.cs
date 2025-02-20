﻿using Neo4j.Driver;

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

        var query = @"
    MATCH (u:User)-[:BOUGHT]->(p:Product)
    WHERE p.name = $name
    WITH p, COLLECT(DISTINCT u) AS level0Users

    MATCH (f:User)-[:FOLLOWS*]->(target:User)-[:BOUGHT]->(p)
    WHERE target IN level0Users
    AND EXISTS {
        MATCH (f)-[:BOUGHT]->(p)
    }
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

    public async Task DeleteExistingData()
    {
        using var session = GetSession();

        try
        {
            Console.WriteLine("Suppression des relations et des utilisateurs en cours...");

            // Suppression des relations FOLLOWS entre utilisateurs
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("MATCH (u:User)-[r:FOLLOWS]->(n:User) DELETE r;");
            });

            // Suppression des relations BOUGHT entre utilisateurs et produits
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("MATCH (u:User)-[r:BOUGHT]->(p:Product) DELETE r;");
            });

            // Suppression des utilisateurs
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("MATCH (u:User) DELETE u;");
            });

            Console.WriteLine("Données supprimées. Vérification et création des index si nécessaire...");

            // Création des index s'ils n'existent pas
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("CREATE INDEX IF NOT EXISTS FOR (u:User) ON (u.id);");
            });

            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("CREATE INDEX IF NOT EXISTS FOR (p:Product) ON (p.id);");
            });

            Console.WriteLine("Index vérifiés et créés si nécessaire.");
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
            int batchSize = Math.Min(1000, userCount / 10);

            Console.WriteLine("Insertion des utilisateurs en cours...");

            // ✅ Insérer les utilisateurs par batch
            await ExecuteBatchUsers(session, userCount, batchSize);

            Console.WriteLine($"{userCount} utilisateurs ajoutés.");

            // ✅ Récupérer tous les IDs des produits existants
            var productIds = await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync("MATCH (p:Product) RETURN p.id AS productId");
                return (await result.ToListAsync()).Select(record => record["productId"].As<int>()).ToList();
            });

            if (!productIds.Any())
            {
                throw new Exception("Aucun produit trouvé dans la base de données !");
            }

            Console.WriteLine("Génération des relations en cours...");

            // ✅ Insérer les relations par batch sans tout stocker en mémoire
            await GenerateAndInsertRelationships(session, userCount, productIds, batchSize);

            Console.WriteLine("Relations ajoutées avec succès.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'ajout : {ex.Message}");
        }
    }

    // 🔹 Insertion des utilisateurs en batch
    private async Task ExecuteBatchUsers(IAsyncSession session, int userCount, int batchSize)
    {
        for (int i = 1; i <= userCount; i += batchSize)
        {
            var usersBatch = Enumerable.Range(i, Math.Min(batchSize, userCount - i + 1))
                                       .Select(id => new { Id = id, Name = $"User{id}" })
                                       .ToList();

            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
            UNWIND $users AS user
            CREATE (u:User {name: user.Name, id: user.Id})",
                new { users = usersBatch });
            });

            Console.WriteLine($"Batch {i} - {i + batchSize - 1} inséré.");
        }
    }

    // 🔹 Génération et insertion des relations sans tout stocker en mémoire
    private async Task GenerateAndInsertRelationships(IAsyncSession session, int userCount, List<int> productIds, int batchSize)
    {
        var random = new Random();

        for (int i = 1; i <= userCount; i += batchSize)
        {
            var followBatch = new List<dynamic>();
            var purchaseBatch = new List<dynamic>();

            for (int j = i; j < i + batchSize && j <= userCount; j++)
            {
                int followerCount = random.Next(0, 21);
                for (int k = 0; k < followerCount; k++)
                {
                    int followedId = random.Next(1, userCount + 1);
                    if (followedId != j)
                    {
                        followBatch.Add(new { source = j, target = followedId });
                    }
                }

                int purchaseCount = random.Next(0, 6);
                for (int k = 0; k < purchaseCount; k++)
                {
                    int productId = productIds[random.Next(productIds.Count)];
                    purchaseBatch.Add(new { source = j, target = productId });
                }
            }

            // Insérer en batch
            await ExecuteBatch(session, "FOLLOWS", followBatch, batchSize);
            await ExecuteBatch(session, "BOUGHT", purchaseBatch, batchSize);
        }
    }

    // 🔹 Exécute l'insertion des relations en batch
    private async Task ExecuteBatch(IAsyncSession session, string relationType, List<dynamic> relationships, int batchSize)
    {
        if (relationships.Count == 0) return;

        for (int i = 0; i < relationships.Count; i += batchSize)
        {
            var batch = relationships.Skip(i).Take(batchSize).ToList();

            string targetNode = relationType == "FOLLOWS" ? "User" : "Product";

            string query = $@"
        UNWIND $relationships AS rel
        MATCH (a:User {{id: rel.source}}), (b:{targetNode} {{id: rel.target}})
        CREATE (a)-[:{relationType}]->(b)";

            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(query, new { relationships = batch });
            });

            Console.WriteLine($"Batch de {batch.Count} relations {relationType} inséré.");
        }
    }


    public void Dispose()
    {
        _driver.Dispose();
        GC.SuppressFinalize(this); 
    }
}