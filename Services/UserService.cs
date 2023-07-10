using System.Text.Json;
using ciam_cli_tools.Models;
using Microsoft.Graph;

namespace ciam_cli_tools.Services
{
    public class UserService
    {
        const string TEST_USER_PREFIX = "CIAM_";
        const string TEST_USER_SUFFIX = "test.com";
        const string TIME_FORMAT = "{0:D2},{1:D2}:{2:D2}:{3:D2}";
        const int BATCH_SIZE = 20;

        public static async Task CreateTestUsers(GraphServiceClient graphClient, AppSettings appSettings, bool addMissingUsers)
        {

            Console.Write("Enter the from value: ");
            int from = int.Parse(Console.ReadLine()!);

            Console.Write("Enter the to value: ");
            int to = int.Parse(Console.ReadLine()!);
            int count = 0;


            Console.WriteLine("Starting create test users operation...");
            DateTime startTime = DateTime.Now;
            Dictionary<string, string> existingUsers = new Dictionary<string, string>();

            // Add the missing users
            if (addMissingUsers)
            {
                // Set a variable to the Documents path.
                string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "users.json");

                if (!System.IO.File.Exists(docPath))
                {
                    Console.WriteLine("Can't find the '{docPath}' file.");
                }

                string usersFile = System.IO.File.ReadAllText(docPath);

                existingUsers = JsonSerializer.Deserialize<Dictionary<string, string>>(usersFile);

                if (existingUsers == null)
                {
                    Console.WriteLine("Can't deserialize users");
                    return;
                }

                Console.WriteLine($"There are {existingUsers.Count} in the directory");
            }

            List<User> users = new List<User>();

            // The batch object
            var batchRequestContent = new BatchRequestContent();

            for (int i = from; i < to; i++)
            {
                // 1,000,000
                string ID = TEST_USER_PREFIX + i.ToString().PadLeft(7, '0');

                if (addMissingUsers)
                {
                    if (existingUsers.ContainsKey(ID))
                        continue;
                }

                count++;

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

                    if (addMissingUsers)
                    {
                        Console.WriteLine($"Adding missing {ID} user");
                    }

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
                        Console.WriteLine($"{string.Format(TIME_FORMAT, d.Days, d.Hours, d.Minutes, d.Seconds)}, count: {count}, user: {ID}");

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
                    Console.WriteLine(ex.Message);
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
                                Console.WriteLine($"{string.Format(TIME_FORMAT, d.Days, d.Hours, d.Minutes, d.Seconds)} users: {iUsers}");
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
                Console.WriteLine(ex.Message);
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
                Console.WriteLine(ex.Message);
            }
        }
        public static async Task ListUsers(GraphServiceClient graphClient)
        {
            Console.WriteLine("Getting list of users...");
            DateTime startTime = DateTime.Now;
            Dictionary<string, string> usersCollection = new Dictionary<string, string>();

            int page = 0;

            try
            {
                // Get all users
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.Id
                    }).OrderBy("DisplayName")
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            usersCollection.Add(user.DisplayName, user.Id);

                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            var d = DateTime.Now - startTime;
                            Console.WriteLine($"{string.Format(TIME_FORMAT, d.Days, d.Hours, d.Minutes, d.Seconds)} users: {usersCollection.Count}");

                            // Set a variable to the Documents path.
                            string filePrefix = "0";
                            if (usersCollection.Count >= 1000000)
                            {
                                filePrefix = usersCollection.Count.ToString()[0].ToString();
                            }

                            page++;

                            if (page >= 50)
                            {
                                page = 0;
                                string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"users_{filePrefix}.json");
                                System.IO.File.WriteAllTextAsync(docPath, JsonSerializer.Serialize(usersCollection));
                            }

                            Thread.Sleep(200);

                            return req;
                        }
                    );

                await pageIterator.IterateAsync();

                // Write last page
                string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"users_all.json");
                System.IO.File.WriteAllTextAsync(docPath, JsonSerializer.Serialize(usersCollection));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
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
                Console.WriteLine(ex.Message);
            }
        }

        public static async Task AddTestUsersToSecurityGroups(GraphServiceClient graphClient)
        {
            Console.WriteLine("Enter the group ID. Use comma delimiter for multiple groups. ");
            string groupsString = Console.ReadLine()!;
            string[] groups = groupsString.Split(",");

            DateTime startTime = DateTime.Now;
            int count = 0;

            List<string> usersToAdd = new List<string>(); ;

            try
            {
                // Get all users
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.Id
                    }).OrderBy("DisplayName")
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            // Only test users
                            if (!user.DisplayName.StartsWith(TEST_USER_PREFIX))
                                return true;

                            count++;
                            usersToAdd.Add($"https://graph.microsoft.com/v1.0/directoryObjects/{user.Id}");

                            if (usersToAdd.Count == 20)
                            {
                                var d = DateTime.Now - startTime;
                                Console.WriteLine($"{string.Format(TIME_FORMAT, d.Days, d.Hours, d.Minutes, d.Seconds)} users: {count}");

                                foreach (var group in groups)
                                {
                                    AddTestUsersToSecurityGroup(graphClient, group, usersToAdd);
                                }

                                usersToAdd = new List<string>();

                                Thread.Sleep(1000);
                            }


                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            return req;
                        }
                    );

                await pageIterator.IterateAsync();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task AddTestUsersToSecurityGroup(GraphServiceClient graphClient, string groupID, List<string> usersToAdd)
        {
            var group = new Group
            {
                AdditionalData = new Dictionary<string, object>()
                {
                    {"members@odata.bind", usersToAdd}
                }
            };

            await graphClient.Groups[groupID]
                .Request()
                .UpdateAsync(group);
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
                Console.WriteLine(ex.Message);
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
                Console.WriteLine(ex.Message);
            }
        }

        public static async Task DeleteAllTestUsers(GraphServiceClient graphClient)
        {
            Console.WriteLine("Getting list of users...");
            DateTime startTime = DateTime.Now;


            try
            {
                // Get all users
                var users = await graphClient.Users
                    .Request()
                    .Select(e => new
                    {
                        e.DisplayName,
                        e.Id
                    }).OrderBy("DisplayName")
                    .GetAsync();

                // Iterate over all the users in the directory
                var pageIterator = PageIterator<User>
                    .CreatePageIterator(
                        graphClient,
                        users,
                        // Callback executed for each user in the collection
                        (user) =>
                        {
                            // Delete user by object ID
                            if (user.DisplayName.StartsWith("CIAM_"))
                            {
                                graphClient.Users[user.Id]
                               .Request()
                               .DeleteAsync();
                            }

                            Console.WriteLine($"{user.DisplayName} was deleted");

                            return true;
                        },
                        // Used to configure subsequent page requests
                        (req) =>
                        {
                            //Thread.Sleep(2000);
                            return req;
                        }
                    );

                await pageIterator.IterateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

    }
}