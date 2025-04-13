using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net.Http;
using System.Text.Json;

namespace lunch_roulette_api
{
    public class RestaurantsFunction
    {
        private readonly ILogger<RestaurantsFunction> _logger;

        public RestaurantsFunction(ILogger<RestaurantsFunction> logger)
        {
            _logger = logger;
        }

        [Function("RestaurantsFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "restaurants")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Azure Maps request.");

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latitude = query["latitude"];
            var longitude = query["longitude"];
            var categories = query["categories"]?.Split(',');
            var price = query["price"];
            var distance = query["distance"];

            if (string.IsNullOrEmpty(latitude) || string.IsNullOrEmpty(longitude))
            {
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("Latitude and longitude are required.");
                return errorResponse;
            }

            // Example: Call Azure Maps API with parameters
            var client = new HttpClient();
            var azureMapsKey = Environment.GetEnvironmentVariable("AzureMapsKey");
            var url = $"https://atlas.microsoft.com/search/poi/json?api-version=1.0&query=restaurant&subscription-key={azureMapsKey}&lat={latitude}&lon={longitude}&radius={distance}&category={string.Join(",", categories ?? new string[0])}&price={price}";

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Azure Maps API call failed.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Azure Maps API call failed.");
                return errorResponse;
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonDocument.Parse(responseBody);

            // Safely access properties to avoid breaking when they are missing
            var filteredResults = jsonResponse.RootElement.GetProperty("results")
                .EnumerateArray()
                .Where(result => result.GetProperty("score").GetDouble() > 0.98 &&
                                 result.GetProperty("poi").GetProperty("categories").EnumerateArray().Any(c => c.GetString() == "restaurant"))
                .Select(result => new
                {
                    name = result.GetProperty("poi").TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                    phone = result.GetProperty("poi").TryGetProperty("phone", out var phoneProp) ? phoneProp.GetString() : null,
                    url = result.GetProperty("poi").TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null,
                    address = result.GetProperty("address").TryGetProperty("freeformAddress", out var addressProp) ? addressProp.GetString() : null
                });

            var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(filteredResults);
            return okResponse;
        }
    }
}
