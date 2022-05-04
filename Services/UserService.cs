using System.Text.Json;
using Microsoft.Graph;

namespace ciam_cli_tools.Services
{
    public class UserService
    {
        public static async Task GetUserById(GraphServiceClient graphClient)
        {
            Console.Write("Enter user object ID: ");
            string userId = Console.ReadLine()!;

            Console.WriteLine($"Looking for user with object ID '{userId}'...");

            try
            {
                // Get user by object ID
                var result = await graphClient.Users[userId]
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.GivenName,
                        e.Surname,
                        e.JobTitle,
                        e.CompanyName,
                        e.Id,
                        e.Identities
                    })
                    .GetAsync();

                if (result != null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result));
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }
    }
}