using Azure.Identity;
using ciam_cli_tools.Models;
using ciam_cli_tools.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;

// Build a config object, using env vars and JSON providers.
IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

// Get values from the config given their key and their target type.
AppSettings settings = config.GetRequiredSection("AppSettings").Get<AppSettings>();

// Initialize the client credential auth provider
var scopes = new[] { "https://graph.microsoft.com/.default" };
var clientSecretCredential = new ClientSecretCredential(settings.TenantId, settings.AppId, settings.ClientSecret);
var graphClient = new GraphServiceClient(clientSecretCredential, scopes);

await UserService.GetUserById(graphClient);

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
