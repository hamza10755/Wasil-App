using System;
using System.Net.Http;
using NBomber.CSharp;
using NBomber.Http;
using NBomber.Http.CSharp;

namespace ApiLoadTests;

class Program
{
    static void Main(string[] args)
    {
        // Define the target API base URL
        const string baseUrl = "http://localhost:5247";

        Console.WriteLine($"Configuring NBomber load tests for: {baseUrl}");

        // Create a shared HttpClient instance
        using var httpClient = Http.CreateDefaultClient();

        // 1. Define Scenario 1: Fetch Stores (Public GET Endpoint)
        var fetchStoresScenario = Scenario.Create("fetch_stores_scenario", async context =>
        {
            var step = await Step.Run("fetch_stores_step", context, async () =>
            {
                var request = Http.CreateRequest("GET", $"{baseUrl}/api/v1/stores?page=1&pageSize=10")
                                  .WithHeader("Accept", "application/json");

                var response = await Http.Send(httpClient, request);
                return response;
            });

            return step;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 20, during: TimeSpan.FromSeconds(30))
        );

        // 2. Define Scenario 2: Product Search (Public GET Endpoint with Query Params)
        var searchProductsScenario = Scenario.Create("search_products_scenario", async context =>
        {
            var step = await Step.Run("search_products_step", context, async () =>
            {
                var request = Http.CreateRequest("GET", $"{baseUrl}/api/v1/products?page=1&pageSize=10&inStockOnly=true")
                                  .WithHeader("Accept", "application/json");

                var response = await Http.Send(httpClient, request);
                return response;
            });

            return step;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 20, during: TimeSpan.FromSeconds(30))
        );

        // 3. Run the load tests
        NBomberRunner
            .RegisterScenarios(fetchStoresScenario, searchProductsScenario)
            .Run();
    }
}