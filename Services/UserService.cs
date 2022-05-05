using System.Text.Json;
using ciam_cli_tools.Models;
using Microsoft.Graph;

namespace ciam_cli_tools.Services
{
    public class UserService
    {
        const string TEST_USER_PREFIX = "CIAM_";
        const string TEST_USER_SUFFIX = "test.com";
        const int BATCH_SIZE = 20;

        public static async Task CreateTestUsers(GraphServiceClient graphClient, AppSettings appSettings, int from = 1, int to = 1000)
        {

            Console.WriteLine("Starting create test users operation...");
            DateTime startTime = DateTime.Now;

            List<User> users = new List<User>();

            // The batch object
            var batchRequestContent = new BatchRequestContent();

            for (int i = from; i < to; i++)
            {
                // 1,000,000
                string ID = TEST_USER_PREFIX + i.ToString().PadLeft(7, '0');

                try
                {
                    var user = new User
                    {
                        DisplayName = ID,
                        JobTitle = ID.Substring(ID.Length - 1),
                        Identities = new List<ObjectIdentity>()
                    {
                        new ObjectIdentity
                        {
                            SignInType = "userName",
                            Issuer = appSettings.TenantName,
                            IssuerAssignedId = ID
                        },
                        new ObjectIdentity
                        {
                            SignInType = "emailAddress",
                            Issuer = appSettings.TenantName,
                            IssuerAssignedId = $"{ID}@{TEST_USER_SUFFIX}"
                        }
                    },
                        PasswordProfile = new PasswordProfile
                        {
                            Password = "1",
                            ForceChangePasswordNextSignIn = false
                        },
                        PasswordPolicies = "DisablePasswordExpiration,DisableStrongPassword"
                    };

                    users.Add(user);


                    // POST requests are handled a bit differently
                    // The SDK request builders generate GET requests, so
                    // you must get the HttpRequestMessage and convert to a POST
                    var jsonEvent = graphClient.HttpProvider.Serializer.SerializeAsJsonContent(user);

                    HttpRequestMessage addUserRequest = graphClient.Users.Request().GetHttpRequestMessage();
                    addUserRequest.Method = HttpMethod.Post;
                    addUserRequest.Content = jsonEvent;

                    if (batchRequestContent.BatchRequestSteps.Count >= BATCH_SIZE)
                    {
                        var d = DateTime.Now - startTime;
                        Console.WriteLine($"{string.Format("{0},{1}:{2}:{3}", d.Days, d.Hours, d.Minutes, d.Seconds)} users: {i}");

                        // Run sent the batch requests
                        var returnedResponse = await graphClient.Batch.Request().PostAsync(batchRequestContent);

                        // Dispose the HTTP request and empty the batch collection
                        foreach (var step in batchRequestContent.BatchRequestSteps) ((BatchRequestStep)step.Value).Request.Dispose();
                        batchRequestContent = new BatchRequestContent();
                    }

                    // Add the event to the batch operations
                    batchRequestContent.AddBatchRequestStep(addUserRequest);

                    // Console.WriteLine($"User '{user.DisplayName}' successfully created.");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.Message);
                    Console.ResetColor();
                }
            }
        }

        public static async Task CleanUpTestUsers(GraphServiceClient graphClient)
        {
            Console.WriteLine("Delete all test users from the directory...");
            DateTime startTime = DateTime.Now;

            // The batch objects
            var batchRequestContent = new BatchRequestContent();
            int currentBatchStep = 1;
            int iUsers = 0;

            try
            {
                // Get all users
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.Id,
                        e.DisplayName
                    })
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            // Delete only test users
                            if (!user.DisplayName.StartsWith(TEST_USER_PREFIX))
                                return true;

                            // Number of delete users
                            iUsers += 1;

                            // Set the MS Graph API user URL
                            var requestUrl = graphClient
                            .Users[user.Id]
                            .Request().RequestUrl;

                            // Create a HTTP delete request
                            var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
                            var requestStep = new BatchRequestStep(currentBatchStep.ToString(), request, null);

                            // Add the step to the collection
                            batchRequestContent.AddBatchRequestStep(requestStep);

                            // On the last item of the users' collection run the batch command
                            if (batchRequestContent.BatchRequestSteps.Count >= BATCH_SIZE)
                            {
                                var d = DateTime.Now - startTime;
                                Console.WriteLine($"{string.Format("{0},{1}:{2}:{3}", d.Days, d.Hours, d.Minutes, d.Seconds)} users: {iUsers}");
                                graphClient.Batch.Request().PostAsync(batchRequestContent).GetAwaiter().GetResult();

                                // Empty the batch collection
                                batchRequestContent = new BatchRequestContent();
                                currentBatchStep = 1;
                                return true;
                            }

                            currentBatchStep++;

                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            Console.WriteLine($"Reading next page of users...");
                            return req;
                        }
                    );

                await pageIterator.IterateAsync();

                // Delete the remaining items
                if (batchRequestContent.BatchRequestSteps.Count > 0)
                {
                    Console.WriteLine($"{DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()} users: {iUsers}");
                    graphClient.Batch.Request().PostAsync(batchRequestContent).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }


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
        public static async Task ListUsers(GraphServiceClient graphClient)
        {
            Console.WriteLine("Getting list of users...");

            try
            {
                // Get all users
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.Id,
                        e.Identities
                    })
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            Console.WriteLine(JsonSerializer.Serialize(user));
                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            Console.WriteLine($"Reading next page of users...");
                            return req;
                        }
                    );

                await pageIterator.IterateAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }
        //</ms_docref_get_list_of_user_accounts>

        public static async Task CountUsers(GraphServiceClient graphClient)
        {
            int i = 0;
            Console.WriteLine("Getting list of users...");

            try
            {
                // Get all users 
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.Id,
                        e.Identities
                    })
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            i += 1;
                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            Console.WriteLine($"Reading next page of users. Number of users: {i}");
                            return req;
                        }
                    );

                await pageIterator.IterateAsync();

                Console.WriteLine("========================");
                Console.WriteLine($"Number of users in the directory: {i}");
                Console.WriteLine("========================");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }

        public static async Task GetUserBySignInName(AppSettings config, GraphServiceClient graphClient)
        {
            Console.Write("Enter user sign-in name (username or email address): ");
            string userId = Console.ReadLine();

            Console.WriteLine($"Looking for user with sign-in name '{userId}'...");

            try
            {
                // Get user by sign-in name
                var result = await graphClient.Users
                    .Request()
                    .Filter($"identities/any(c:c/issuerAssignedId eq '{userId}' and c/issuer eq '{config.TenantName}')")
                    .Select(e => new
                    {
                        e.DisplayName,
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

        public static async Task DeleteUserById(GraphServiceClient graphClient)
        {
            Console.Write("Enter user object ID: ");
            string userId = Console.ReadLine();

            Console.WriteLine($"Looking for user with object ID '{userId}'...");

            try
            {
                // Delete user by object ID
                await graphClient.Users[userId]
                   .Request()
                   .DeleteAsync();

                Console.WriteLine($"User with object ID '{userId}' successfully deleted.");
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