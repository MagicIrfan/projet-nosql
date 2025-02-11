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
            SqlServerTimeStatus.Text = "N/A";
            Neo4JTimeStatus.Text = "N/A";
           var userCount = int.Parse(UserCountTextBox.Text);
            LoadingProgressBar.Visibility = Visibility.Visible;

            await Task.Run(() => _sqlServerDatabase.DeleteExistingData());
            await Task.Run(() => _neo4JDatabase.DeleteExistingData());

            var sqlStopwatch = new Stopwatch();
            var neo4jStopwatch = new Stopwatch();

            sqlStopwatch.Start();
            var sqlTask = Task.Run(() =>
            {
                _sqlServerDatabase.AddUsers(userCount);
                sqlStopwatch.Stop();

                Dispatcher.Invoke(() => SqlServerTimeStatus.Text = $"{sqlStopwatch.ElapsedMilliseconds} ms");
            });

            neo4jStopwatch.Start();
            var neo4jTask = Task.Run(async () =>
            {
                await _neo4JDatabase.AddUsers(userCount);
                neo4jStopwatch.Stop();

                Dispatcher.Invoke(() => Neo4JTimeStatus.Text = $"{neo4jStopwatch.ElapsedMilliseconds} ms");
            });

            await Task.WhenAll(sqlTask, neo4jTask);
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

    private async void GetNbFollowersFromProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SqlServerTimeStatus2.Text = "";
            Neo4JTimeStatus2.Text = "";
            SqlServerProductsListBox2.Items.Clear();
            Neo4JProductsListBox2.Items.Clear();
            
            var depth = int.Parse(DepthTextBox2.Text);
            var productName = ProductNameTextBox2.Text;

            LoadingProgressBar.Visibility = Visibility.Visible;
            SqlServerProductsListBox.Items.Clear();
            Neo4JProductsListBox.Items.Clear();

            var stopwatch = new Stopwatch();

            stopwatch.Start();
            var nbFollowersSql = await Task.Run(() => _sqlServerDatabase.GetNbUserOrderedProductByDepth(productName, depth));
            stopwatch.Stop();
            SqlServerTimeStatus2.Text = $"{stopwatch.ElapsedMilliseconds} ms";

            stopwatch.Restart();
            var nbFollowersNeo4J = await Task.Run(() => _neo4JDatabase.GetNbUserOrderedProductByDepth(productName, depth));  
            stopwatch.Stop();
            Neo4JTimeStatus2.Text = $"{stopwatch.ElapsedMilliseconds} ms";

            SqlServerProductsListBox2.Items.Add($"Nb followers: {nbFollowersSql}");
            Neo4JProductsListBox2.Items.Add($"Nb followers: {nbFollowersNeo4J}");
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
    
    private async void GetProductsFromFollowers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SqlServerTimeStatus.Text = "";
            Neo4JTimeStatus.Text = "";
            SqlServerProductsListBox.Items.Clear();
            Neo4JProductsListBox.Items.Clear();
            
            
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