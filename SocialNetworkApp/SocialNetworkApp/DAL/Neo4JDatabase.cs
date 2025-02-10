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
            // ❌ Supprimer toutes les relations FOLLOWS et BOUGHT, ainsi que les utilisateurs (sauf les produits)
            session.ExecuteWriteAsync(async tx =>
            {
                // Supprimer les relations FOLLOWS et BOUGHT
                await tx.RunAsync("MATCH (u:User)-[r]->(n) DELETE r;");  // Supprime toutes les relations des utilisateurs

                // Supprimer les utilisateurs, mais garder les produits intacts
                await tx.RunAsync("MATCH (u:User) DELETE u;");
            }).Wait();

            Console.WriteLine("✅ Données existantes supprimées (produits conservés).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erreur lors de la suppression des données : {ex.Message}");
        }
    }

    public void AddUsers(int userCount, int batchSize = 100)
    {
        using var session = GetSession();
        var random = new Random();

        try
        {
            // ❌ SUPPRIMER TOUT AVANT D'AJOUTER LES NOUVEAUX UTILISATEURS
            session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("MATCH(n) WHERE NOT n: Product DETACH DELETE n;");
            }).Wait();

            Console.WriteLine("✅ Base de données nettoyée.");

            // 🔍 Récupérer le nombre d'utilisateurs existants (après suppression = 0)
            var currentUserCount = 0;

            // 🆕 Création des utilisateurs en une seule requête UNWIND
            var userNames = Enumerable.Range(1, userCount)
                                      .Select(i => $"User{i}")
                                      .ToList();

            session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("UNWIND $userNames AS name CREATE (:User {name: name})",
                                  new { userNames });
            }).Wait();

            Console.WriteLine($"✅ {userCount} utilisateurs ajoutés.");

            // Récupérer les UserIds des utilisateurs nouvellement insérés
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

            // Générer les relations FOLLOWS et BOUGHT
            var followRelationships = new List<(int, int)>();
            var purchaseRelationships = new List<(int, int)>();

            foreach (var userId in insertedUserIds)
            {
                // 🎯 Générer des relations FOLLOWS aléatoires
                var followerCount = random.Next(0, 21);
                for (int j = 0; j < followerCount; j++)
                {
                    var followedIndex = insertedUserIds[random.Next(insertedUserIds.Count)];
                    if (followedIndex == userId) continue; // Un utilisateur ne peut pas se suivre lui-même
                    followRelationships.Add((userId, followedIndex));
                }

                // 🛒 Générer des relations BOUGHT aléatoires
                var purchaseCount = random.Next(0, 6);
                for (int j = 0; j < purchaseCount; j++)
                {
                    var productId = random.Next(1, 6); // ID de produit aléatoire entre 1 et 5
                    purchaseRelationships.Add((userId, productId));
                }
            }

            // 🚀 Exécuter les relations en batch
            ExecuteBatch(session, "FOLLOWS", followRelationships, batchSize);
            ExecuteBatch(session, "BOUGHT", purchaseRelationships, batchSize);

            Console.WriteLine("✅ Relations ajoutées avec succès.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erreur lors de l'ajout : {ex.Message}");
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

                // Utilisation d'un format explicite (d'un objet avec Item1 et Item2)
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