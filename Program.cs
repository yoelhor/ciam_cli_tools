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
var clientSecretCredential = new ClientSecretCredential(settings.TenantName, settings.AppId, settings.ClientSecret);
var graphClient = new GraphServiceClient(clientSecretCredential, scopes);

PrintCommands();


try
{
    while (true)
    {
        Console.Write("Enter command, then press ENTER: ");
        string decision = Console.ReadLine()!;
        switch (decision.ToLower())
        {
            case "1":
                await UserService.ListUsers(graphClient);
                break;
            case "2":
                await UserService.CreateTestUsers(graphClient, settings);
                break;
            case "help":
                PrintCommands();
                break;
            case "exit":
                return;
            default:
                Console.WriteLine("Invalid command. Enter 'help' to show a list of commands.");
                break;
        }

        Console.ResetColor();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex}");
}


//await UserService.CreateTestUsers(graphClient, settings);
//await UserService.CleanUpTestUsers(graphClient);


// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


static void PrintCommands()
{
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Command  Description");
    Console.WriteLine("====================");
    Console.WriteLine("[1]      Get all users");
    Console.WriteLine("[2]      Create test users");
    Console.WriteLine("[help]   Show available commands");
    Console.WriteLine("[exit]   Exit the program");
    Console.WriteLine("-------------------------");
}