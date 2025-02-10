using Microsoft.Data.SqlClient;

namespace SocialNetworkApp.DAL;

public class SqlServerDatabase(string connectionString) : IDisposable
{
    private SqlConnection GetConnection()
    {
        return new SqlConnection(connectionString);
    }
    
    public void AddUsers(int userCount)
    {
        using var connection = GetConnection();
        connection.Open();
        var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Users;";
        var currentUserCount = (int)command.ExecuteScalar();

        var transaction = connection.BeginTransaction();
        command.Transaction = transaction;

        try
        {
            var userInsertValues = new List<string>();

            for (var i = 0; i < userCount; i++)
            {
                var userName = "User" + (currentUserCount + i + 1);
                userInsertValues.Add($"('{userName}')");
            }

            command.CommandText = $"INSERT INTO Users (UserName) VALUES {string.Join(", ", userInsertValues)};";
            command.ExecuteNonQuery();

            for (var i = 0; i < userCount; i++)
            {
                var followerCount = new Random().Next(0, 21);
                for (var j = 0; j < followerCount; j++)
                {
                    try
                    {
                        var followedId = new Random().Next(currentUserCount + 1, currentUserCount + userCount);

                        command.CommandText =
                            $"INSERT INTO Followers (FollowerId, FollowedId) VALUES ({currentUserCount + i + 1}, {followedId});";
                        command.ExecuteNonQuery();
                    }
                    catch (Exception _)
                    {
                        continue;
                    }
                    
                }
            }

            var purchaseInsertValues = new List<string>();

            for (var i = 0; i < userCount; i++)
            {
                var purchaseCount = new Random().Next(0, 6);
                for (var j = 0; j < purchaseCount; j++)
                {
                    var productId = new Random().Next(1, 6);
                    purchaseInsertValues.Add($"({currentUserCount + i + 1}, {productId})");
                }
            }

            if (purchaseInsertValues.Count != 0)
            {
                command.CommandText = $"INSERT INTO Purchases (UserId, ProductId) VALUES {string.Join(", ", purchaseInsertValues)};";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"Erreur lors de l'ajout : {ex.Message}");
        }
    }
    
    public async Task<int> GetNbUserOrderedProductByDepth(string name, int depth)
    {

        await using var connection = GetConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
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
            WHERE fc.level < @Depth
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