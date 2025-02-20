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
            command.CommandText = "DELETE FROM Purchases; DELETE FROM Followers; DELETE FROM Users;";
            command.ExecuteNonQuery();

            transaction.Commit();
            Console.WriteLine("Données existantes supprimées.");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"Erreur lors de la suppression des données : {ex.Message}");
        }
    }
    public void AddUsers(int userCount)
    {
        using var connection = GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var random = new Random();

        try
        {
            int batchSize = 1000;

            command.CommandText = "SELECT ProductId FROM Products;";
            var productIds = new List<int>();
            using (var productReader = command.ExecuteReader())
            {
                while (productReader.Read())
                {
                    productIds.Add(productReader.GetInt32(0));
                }
            }

            if (productIds.Count == 0)
            {
                throw new Exception("Aucun produit disponible dans la table Products !");
            }

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

            var insertedUserIds = new List<int>();
            command.CommandText = "SELECT TOP (@userCount) UserId FROM Users ORDER BY UserId DESC";
            command.Parameters.AddWithValue("@userCount", userCount);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    insertedUserIds.Add(reader.GetInt32(0));
                }
            }

            var followerInsertValues = new HashSet<(int, int)>();
            var purchaseInsertValues = new List<(int, int)>();

            for (var i = 0; i < userCount; i++)
            {
                var userId = insertedUserIds[i];
                var followerCount = random.Next(0, 21);

                for (var j = 0; j < followerCount; j++)
                {
                    var followedId = insertedUserIds[random.Next(insertedUserIds.Count)];
                    if (followedId != userId && !followerInsertValues.Contains((userId, followedId)))
                    {
                        followerInsertValues.Add((userId, followedId));
                    }
                }

                var purchaseCount = random.Next(0, 6);
                for (var j = 0; j < purchaseCount; j++)
                {
                    var productId = productIds[random.Next(productIds.Count)];
                    purchaseInsertValues.Add((userId, productId));
                }
            }

            InsertBatch(command, "Followers", "FollowerId, FollowedId", followerInsertValues, batchSize);
            InsertBatch(command, "Purchases", "UserId, ProductId", purchaseInsertValues, batchSize);

            transaction.Commit();
            Console.WriteLine($"{userCount} utilisateurs ajoutés avec succès !");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"Erreur : {ex.Message}");
        }
    }

    private void InsertBatch(SqlCommand command, string tableName, string columns, IEnumerable<(int, int)> values, int batchSize)
    {
        var batchList = new List<string>();
        int currentBatchSize = 0;

        foreach (var (val1, val2) in values)
        {
            batchList.Add($"({val1}, {val2})");
            currentBatchSize++;

            if (currentBatchSize >= 1000)
            {
                command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES {string.Join(", ", batchList)};";
                command.ExecuteNonQuery();
                batchList.Clear();
                currentBatchSize = 0;
            }
        }

        if (batchList.Count > 0)
        {
            command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES {string.Join(", ", batchList)};";
            command.ExecuteNonQuery();
        }
    }


    public async Task<int> GetNbUserOrderedProductByDepth(string name, int depth)
    {
        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandTimeout = 0;
            command.CommandText = @"
        WITH followers_cte AS (
            SELECT DISTINCT o.UserId, 0 AS level
            FROM Purchases o
            JOIN Products p ON o.ProductId = p.ProductId
            WHERE p.ProductName = @Name

                UNION ALL

                SELECT f.FollowedId AS UserId, fc.level + 1
                FROM Followers f
                JOIN followers_cte fc ON f.FollowerID = fc.UserId
                JOIN Purchases po ON f.FollowedId = po.UserId  
                JOIN Products pp ON po.ProductId = pp.ProductId
                WHERE pp.ProductName = @Name
                AND fc.level < @Depth
            )
            SELECT COUNT(DISTINCT UserId) AS UserCount
            FROM followers_cte
            WHERE level = @Depth;
        ";

            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Depth", depth);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return reader.GetInt32(0);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur dans GetNbUserOrderedProductByDepth : {ex.Message}");
            return -1; // Retourne -1 pour signaler une erreur
        }
    }


    public async Task<List<(string ProductName, int Count)>> GetProductsOrderedByFollowers(string name, int depth)
    {
        var products = new List<(string ProductName, int Count)>();

        await using var connection = GetConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandTimeout = 0;
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