using Microsoft.Data.SqlClient;
using System.Data;

namespace SocialNetworkApp.DAL;

public class SqlServerDatabase(string connectionString) : IDisposable
{
    private SqlConnection GetConnection()
    {
        return new SqlConnection(connectionString);
    }

    public void DeleteExistingData()
    {
        using var connection = GetConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        try
        {
            // Exécution des commandes DELETE
            command.CommandText = "DELETE FROM Purchases; DELETE FROM Followers; DELETE FROM Users;";
            command.ExecuteNonQuery();

            // Commit des modifications si tout se passe bien
            transaction.Commit();
            Console.WriteLine("✅ Données existantes supprimées.");
        }
        catch (Exception ex)
        {
            // Si une erreur survient, on effectue un rollback
            transaction.Rollback();
            Console.WriteLine($"❌ Erreur lors de la suppression des données : {ex.Message}");
        }
    }

    public void AddUsers(int userCount, int batchSize = 100)
    {
        using var connection = GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var random = new Random();

        try
        {
            command.CommandText = "SELECT ProductId FROM Products;";
            using var productReader = command.ExecuteReader();
            var productIds = new List<int>();

            while (productReader.Read())
            {
                productIds.Add(productReader.GetInt32(0));
            }
            productReader.Close();

            if (productIds.Count == 0)
            {
                throw new Exception("❌ Aucun produit disponible dans la table Products !");
            }

            // 📌 Création des utilisateurs en lots
            var userInsertValues = new List<string>();

            for (var i = 0; i < userCount; i++)
            {
                var userName = $"User{i + 1}";
                userInsertValues.Add($"('{userName}')");

                if ((i + 1) % batchSize == 0 || i == userCount - 1)
                {
                    command.CommandText = $"INSERT INTO Users (UserName) VALUES {string.Join(", ", userInsertValues)};";
                    command.ExecuteNonQuery();
                    userInsertValues.Clear();
                }
            }

            // 🔍 Récupération des nouveaux utilisateurs
            command.CommandText = "SELECT UserId FROM Users;";
            using var reader = command.ExecuteReader();
            var insertedUserIds = new List<int>();

            while (reader.Read())
            {
                insertedUserIds.Add(reader.GetInt32(0));
            }
            reader.Close();

            // 👥 Génération des relations FOLLOWERS
            var followerInsertValues = new HashSet<(int, int)>();

            foreach (var userId in insertedUserIds)
            {
                var followerCount = random.Next(0, 21);
                for (var j = 0; j < followerCount; j++)
                {
                    var followedId = insertedUserIds[random.Next(insertedUserIds.Count)];
                    if (followedId == userId || followerInsertValues.Contains((userId, followedId)))
                        continue; // Éviter l'auto-follow et les doublons

                    followerInsertValues.Add((userId, followedId));
                }
            }

            InsertBatch(command, "Followers", "FollowerId, FollowedId", followerInsertValues, batchSize);

            // 🛒 Génération des relations PURCHASES
            var purchaseInsertValues = new List<(int, int)>();

            foreach (var userId in insertedUserIds)
            {
                var purchaseCount = random.Next(0, 6);
                for (var j = 0; j < purchaseCount; j++)
                {
                    var productId = productIds[random.Next(productIds.Count)]; // ✅ Sélection de produits valides
                    purchaseInsertValues.Add((userId, productId));
                }
            }

            InsertBatch(command, "Purchases", "UserId, ProductId", purchaseInsertValues, batchSize);

            transaction.Commit();
            Console.WriteLine($"✅ {userCount} utilisateurs ajoutés avec succès !");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"❌ Erreur : {ex.Message}");
        }
    }

    /// <summary>
    /// Insère des données en lots dans une table SQL.
    /// </summary>
    private void InsertBatch(SqlCommand command, string tableName, string columns, IEnumerable<(int, int)> values, int batchSize)
    {
        var batchList = new List<string>();

        foreach (var (val1, val2) in values)
        {
            batchList.Add($"({val1}, {val2})");

            if (batchList.Count >= batchSize)
            {
                command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES {string.Join(", ", batchList)};";
                command.ExecuteNonQuery();
                batchList.Clear();
            }
        }

        if (batchList.Count > 0)
        {
            command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES {string.Join(", ", batchList)};";
            command.ExecuteNonQuery();
        }
    }

    public async Task<List<(string ProductName, int Count)>> GetProductsOrderedByFollowers(string name, int depth)
    {
        var products = new List<(string ProductName, int Count)>();

        await using var connection = GetConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
        WITH FollowersHierarchy AS (
            SELECT u.UserId, u.UserName, 0 AS Depth
            FROM Users u
            WHERE u.UserName = @Name

            UNION ALL

            SELECT f.FollowerId AS UserId, u.UserName, fh.Depth + 1
            FROM Followers f
            JOIN Users u ON f.FollowerId = u.UserId 
            JOIN FollowersHierarchy fh ON f.FollowedId = fh.UserId
            WHERE fh.Depth < @Depth 
        )

        SELECT p.ProductName, COUNT(*) AS PurchaseCount
        FROM Purchases pu
        JOIN Products p ON pu.ProductId = p.ProductId
        JOIN Users u ON pu.UserId = u.UserId 
        WHERE pu.UserId IN (SELECT UserId FROM FollowersHierarchy)
        AND u.UserName != @Name 
        GROUP BY p.ProductName
        ORDER BY PurchaseCount DESC;
        ";

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Depth", depth);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            products.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return products;
    }
    
    public async Task<List<(string ProductName, int Count)>> GetProductsOrderedByFollowersFilteredByProduct(string name, int depth, string product)
    {
        var products = new List<(string ProductName, int Count)>();

        await using var connection = GetConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
        WITH FollowersHierarchy AS (
            SELECT u.UserId, u.UserName, 0 AS Depth
            FROM Users u
            WHERE u.UserName = @Name

            UNION ALL

            SELECT f.FollowerId AS UserId, u.UserName, fh.Depth + 1
            FROM Followers f
            JOIN Users u ON f.FollowerId = u.UserId 
            JOIN FollowersHierarchy fh ON f.FollowedId = fh.UserId
            WHERE fh.Depth < @Depth 
        )

        SELECT p.ProductName, COUNT(*) AS PurchaseCount
        FROM Purchases pu
        JOIN Products p ON pu.ProductId = p.ProductId
        JOIN Users u ON pu.UserId = u.UserId 
        WHERE pu.UserId IN (SELECT UserId FROM FollowersHierarchy)
        AND p.ProductName = @Product
        AND u.UserName != @Name  
        GROUP BY p.ProductName
        ORDER BY PurchaseCount DESC;
        ";

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Depth", depth);
        command.Parameters.AddWithValue("@Product", product);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            products.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return products;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}