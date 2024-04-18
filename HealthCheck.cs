using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Portfolio.Functions
{
    public class HealthCheck
    {
        private readonly ILogger _logger;
        private static int failedCalls = 0;
        public HealthCheck(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<HealthCheck>();
        }

        [Function("HealthCheck")]
        public void Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer)
        {
            try
            {
                HttpClient httpClient = new HttpClient();
                var baseURL = Environment.GetEnvironmentVariable("BaseURL");
                _logger.LogInformation($"Base URL: {baseURL}");
                if(!string.IsNullOrEmpty(baseURL))
                {
                    string requestURI = $"{baseURL}/weatherforecast";
                    _logger.LogInformation($"Request URL: {requestURI}");
                    var result = httpClient.GetAsync(requestURI).Result;
                    int statusCode = (int)result.StatusCode;
                    _logger.LogInformation($"Status: {result.StatusCode}");

                    if(statusCode < 200 || statusCode > 299)
                    {
                        _logger.LogInformation($"Error Status Code: {statusCode}");
                        failedCalls += 1;
                        _logger.LogInformation($"Failed Calls Count: {failedCalls}");
                        if(failedCalls >= 2)
                        {
                            var azureLoginBaseURL = Environment.GetEnvironmentVariable("AzureLoginBaseURL");
                            var clientId = Environment.GetEnvironmentVariable("ClientId");
                            var clientSecret = Environment.GetEnvironmentVariable("ClientSecret");
                            var tenantId = Environment.GetEnvironmentVariable("TenantId");
                            var azureManagementBaseURL = Environment.GetEnvironmentVariable("AzureManagementBaseURL");

                            if(string.IsNullOrEmpty(azureLoginBaseURL) ||
                                string.IsNullOrEmpty(clientId) ||
                                string.IsNullOrEmpty(clientSecret) ||
                                string.IsNullOrEmpty(tenantId) ||
                                string.IsNullOrEmpty(azureManagementBaseURL))
                            {
                                _logger.LogError($"Envs for azure login aren't added.");
                                return;
                            }


                            var body = new List<KeyValuePair<string, string>>
                            {
                                new KeyValuePair<string, string>("client_id", clientId),
                                new KeyValuePair<string, string>("client_secret", clientSecret),
                                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                                new KeyValuePair<string, string>("scope", $"{azureManagementBaseURL}/.default"),
                            };

                            var content = new FormUrlEncodedContent(body);
                            
                            var azureLoginResponse = httpClient.PostAsync($"{azureLoginBaseURL}/{tenantId}/oauth2/v2.0/token", content).Result;
                            if(azureLoginResponse.StatusCode != HttpStatusCode.OK)
                            {
                                _logger.LogError($"Azure Login Failed with status code: {azureLoginResponse.StatusCode}");
                                return;
                            }
                            _logger.LogError($"{azureLoginResponse.Content.ReadAsStringAsync().Result}");
                            JsonNode azureLoginResult = JsonSerializer.Deserialize<JsonNode>(azureLoginResponse.Content.ReadAsStringAsync().Result);
                            _logger.LogInformation($"RESULT: {azureLoginResult}");
                            var azureAccessToken = azureLoginResult["access_token"].ToString();
                            
                            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
                            var resourceGroupName = Environment.GetEnvironmentVariable("ResourceGroupName");
                            var appserviceName = Environment.GetEnvironmentVariable("AppServiceName");

                            if(string.IsNullOrEmpty(subscriptionId) ||
                                string.IsNullOrEmpty(resourceGroupName) ||
                                string.IsNullOrEmpty(appserviceName))
                            {
                                _logger.LogError($"Envs for azure management apis aren't added.");
                                return;
                            }

                            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", azureAccessToken);
                            var azureManagementResponse = httpClient.PostAsync($"{azureManagementBaseURL}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{appserviceName}/restart?api-version=2023-01-01", null).Result;
                            _logger.LogError($"{azureManagementResponse.Content.ReadAsStringAsync().Result}");
                            if(!azureManagementResponse.IsSuccessStatusCode)
                            {
                                _logger.LogInformation($"Restart status: {azureManagementResponse.StatusCode}");
                                _logger.LogError("Failed to restart appservice.");
                                failedCalls = 0;
                            }
                            else
                            {
                                _logger.LogInformation("Appservice restarted successfully.");
                            }
                        }
                    }
                    else
                    {
                        failedCalls = 0;
                        _logger.LogInformation($"Failed Calls Count: {failedCalls}");
                    }
                }
            }
            catch(Exception e)
            {
                _logger.LogInformation($"Error: {e.Message} || {e.StackTrace}");
            }
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }
    }
}
