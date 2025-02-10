using System.Diagnostics;
using System.Windows;
using Microsoft.IdentityModel.Tokens;
using SocialNetworkApp.DAL;

namespace SocialNetworkApp;

public partial class MainWindow
{
    private const string SqlConnectionString = "Server=FEUR\\SQLEXPRESS;Database=SocialNetworkDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string Neo4JUri = "bolt://localhost:7687";
    private const string Neo4JUser = "neo4j";
    private const string Neo4JPassword = "INFRES_XV";

    private readonly SqlServerDatabase _sqlServerDatabase;
    private readonly Neo4JDatabase _neo4JDatabase;

    public MainWindow()
    {
        InitializeComponent();
        _sqlServerDatabase = new SqlServerDatabase(SqlConnectionString);
        _neo4JDatabase = new Neo4JDatabase(Neo4JUri, Neo4JUser, Neo4JPassword);
    }

    private async void AddUsersAndTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var userCount = int.Parse(UserCountTextBox.Text);

            LoadingProgressBar.Visibility = Visibility.Visible;

            var stopwatch = new Stopwatch();
            await Task.Run(() => _sqlServerDatabase.DeleteExistingData());
            stopwatch.Start();
            await Task.Run(() => _sqlServerDatabase.AddUsers(userCount)); 
            stopwatch.Stop();
            SqlServerTimeStatus.Text = $"{stopwatch.ElapsedMilliseconds} ms";

            await Task.Run(() => _neo4JDatabase.DeleteExistingData());
            stopwatch.Restart();
            await _neo4JDatabase.AddUsers(userCount);
            stopwatch.Stop();
            Neo4JTimeStatus.Text = $"{stopwatch.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
        }
    }
    
    private async void GetProductsFromFollowers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var depth = int.Parse(DepthTextBox.Text);
            var userName = UserNameTextBox.Text;
            var productName = ProductNameTextBox.Text;
            
            Console.WriteLine(productName.IsNullOrEmpty());

            LoadingProgressBar.Visibility = Visibility.Visible;
            SqlServerProductsListBox.Items.Clear();
            Neo4JProductsListBox.Items.Clear();

            var stopwatch = new Stopwatch();
            List<(string ProductName, int Count)> productsSql;
            List<(string ProductName, int Count)> productsNeo4J;

            stopwatch.Start();
            if (productName.IsNullOrEmpty())
                productsSql = await Task.Run(() => _sqlServerDatabase.GetProductsOrderedByFollowers(userName, depth)); 
            else
                productsSql = await Task.Run(() => _sqlServerDatabase.GetProductsOrderedByFollowersFilteredByProduct(userName, depth, productName));
            stopwatch.Stop();
            SqlServerTimeStatus.Text = $"{stopwatch.ElapsedMilliseconds} ms";

            stopwatch.Restart();
            if (productName.IsNullOrEmpty())
                productsNeo4J = await Task.Run(() => _neo4JDatabase.GetProductsOrderedByFollowers(userName, depth));  
            else
                productsNeo4J = await Task.Run(() => _neo4JDatabase.GetProductsOrderedByFollowersFilteredByProduct(userName, depth, productName));  
            stopwatch.Stop();
            Neo4JTimeStatus.Text = $"{stopwatch.ElapsedMilliseconds} ms";
            
            foreach (var product in productsSql)
            {
                SqlServerProductsListBox.Items.Add($"Produit Name: {product.ProductName}, Commandé {product.Count} fois");
            }
            
            foreach (var product in productsNeo4J)
            {
                Neo4JProductsListBox.Items.Add($"Produit Name: {product.ProductName}, Commandé {product.Count} fois");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur : {ex.Message}");
        }
        finally
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
        }
    }


}