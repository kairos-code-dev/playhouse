#nullable enable

using System.Net.Http.Json;

namespace PlayHouse.E2E.Shared.Infrastructure;

/// <summary>
/// Sample usage of TestHttpApiController for reference.
/// This file demonstrates how to use the HTTP API endpoints.
/// </summary>
/// <remarks>
/// This is a sample/documentation file, not a test file.
/// Actual tests will be implemented in the main verification test suite.
/// </remarks>
public static class TestHttpApiControllerSample
{
    /// <summary>
    /// Example: Create a stage via HTTP API
    /// </summary>
    public static async Task<CreateStageResponse> CreateStageExample(HttpClient httpClient, string stageType, string stageId)
    {
        var request = new CreateStageRequest
        {
            StageType = stageType,
            StageId = stageId
        };

        var response = await httpClient.PostAsJsonAsync("/api/stages", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateStageResponse>();
        return result ?? throw new InvalidOperationException("Failed to deserialize CreateStageResponse");
    }

    /// <summary>
    /// Example: Get or create a stage via HTTP API
    /// </summary>
    public static async Task<GetOrCreateStageResponse> GetOrCreateStageExample(HttpClient httpClient, string stageType, string stageId)
    {
        var request = new GetOrCreateStageRequest
        {
            StageType = stageType,
            StageId = stageId
        };

        var response = await httpClient.PostAsJsonAsync("/api/stages/get-or-create", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();
        return result ?? throw new InvalidOperationException("Failed to deserialize GetOrCreateStageResponse");
    }

    /// <summary>
    /// Example: Complete workflow - setup servers and make HTTP API call
    /// </summary>
    public static Task CompleteWorkflowExample()
    {
        // Note: This is pseudocode for demonstration purposes
        // Actual implementation will be in the test suite

        // 1. Setup PlayServer
        // var playServer = await ServerFactory.CreatePlayServerAsync(...);

        // 2. Setup ApiServer with HTTP endpoint
        // var (apiServer, httpApp, httpPort) = await ServerFactory.CreateApiServerWithHttpAsync(...);

        // 3. Create HttpClient
        // using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };

        // 4. Call HTTP API
        // var result = await CreateStageExample(httpClient, "TestStage", "1");

        // 5. Verify result
        // Assert.True(result.Success);

        // 6. Cleanup
        // await httpApp.StopAsync();
        // await apiServer.StopAsync();
        // await playServer.StopAsync();

        return Task.CompletedTask;
    }
}
