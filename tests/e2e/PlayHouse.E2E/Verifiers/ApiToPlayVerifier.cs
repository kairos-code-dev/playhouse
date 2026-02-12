using System.Net.Http.Json;
using PlayHouse.E2E.Shared.Infrastructure;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// API-to-Play 통신 검증 (HTTP API 기반 E2E)
///
/// E2E 원칙:
/// - HTTP 클라이언트만 사용 (HttpClient.PostAsJsonAsync)
/// - HTTP 응답만으로 검증 (CreateStageResponse, GetOrCreateStageResponse)
/// - 서버 내부 상태 접근 금지 (ApiServer.ApiSender 직접 호출 X)
/// - 클라이언트 연결 불필요 (ApiToPlay는 HTTP API만 사용)
/// </summary>
public class ApiToPlayVerifier : VerifierBase
{
    public override string CategoryName => "ApiToPlay";

    private HttpClient? _httpClient;
    private string _httpBaseUrl = null!;

    public ApiToPlayVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 5;

    protected override async Task SetupAsync()
    {
        // HTTP 클라이언트 초기화
        _httpBaseUrl = $"http://127.0.0.1:{ServerContext.ApiServer1HttpPort}";
        _httpClient = new HttpClient { BaseAddress = new Uri(_httpBaseUrl) };

        // HTTP 서버 안정화 대기
        await Task.Delay(1000);
    }

    protected override Task TeardownAsync()
    {
        // HttpClient 정리
        _httpClient?.Dispose();
        _httpClient = null;

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("CreateStage_Success", Test_CreateStage_Success);
        await RunTest("GetOrCreateStage_NewStage", Test_GetOrCreateStage_NewStage);
        await RunTest("GetOrCreateStage_ExistingStage", Test_GetOrCreateStage_ExistingStage);
        await RunTest("CreateStage_Callback", Test_CreateStage_Callback);
        await RunTest("GetOrCreateStage_Callback", Test_GetOrCreateStage_Callback);
    }

    /// <summary>
    /// CreateStage - HTTP POST /api/stages 성공
    /// OnCreate에 packet이 제대로 전달되는지 검증
    /// </summary>
    private async Task Test_CreateStage_Success()
    {
        // Given
        var stageId = GenerateUniqueStageId(10000).ToString();
        var request = new CreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };

        // When - HTTP POST /api/stages
        var response = await _httpClient!.PostAsJsonAsync("/api/stages", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateStageResponse>();

        // Then - E2E 검증 (HTTP 응답만)
        Assert.IsTrue(result!.Success, "CreateStage should succeed");
        Assert.Equals(stageId, result.StageId);

        // OnCreate에 packet이 전달되었는지 검증 (reply에서 echo된 값 확인)
        Assert.IsTrue(result.ReplyPayloadId != null, "OnCreate should return reply with payload info");
        Assert.Equals("TestStage:10", result.ReplyPayloadId, "OnCreate should receive createPacket");
    }

    /// <summary>
    /// GetOrCreateStage - 새 Stage 생성 (IsCreated=true)
    /// OnCreate에 packet이 제대로 전달되는지 검증
    /// </summary>
    private async Task Test_GetOrCreateStage_NewStage()
    {
        // Given
        var stageId = GenerateUniqueStageId(11000).ToString();
        var request = new GetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };

        // When - HTTP POST /api/stages/get-or-create
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/get-or-create", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();

        // Then - 새로운 Stage 생성 확인
        Assert.IsTrue(result!.Success, "GetOrCreateStage should succeed");
        Assert.IsTrue(result.IsCreated, "Stage should be newly created");
        Assert.Equals(stageId, result.StageId);

        // OnCreate에 packet이 전달되었는지 검증 (reply에서 echo된 값 확인)
        Assert.IsTrue(result.ReplyPayloadId != null, "OnCreate should return reply with payload info");
        Assert.Equals("TestStage:10", result.ReplyPayloadId, "OnCreate should receive createPacket");
    }

    /// <summary>
    /// GetOrCreateStage - 기존 Stage 조회 (IsCreated=false)
    /// </summary>
    private async Task Test_GetOrCreateStage_ExistingStage()
    {
        // Given - 먼저 Stage 생성
        var stageId = GenerateUniqueStageId(12000).ToString();
        var createRequest = new CreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };
        var createResponse = await _httpClient!.PostAsJsonAsync("/api/stages", createRequest);
        createResponse.EnsureSuccessStatusCode();

        await Task.Delay(100); // Stage 생성 완료 대기

        // When - 동일한 StageId로 GetOrCreateStage 재호출
        var getOrCreateRequest = new GetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/get-or-create", getOrCreateRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();

        // Then - 기존 Stage 반환 확인
        Assert.IsTrue(result!.Success, "GetOrCreateStage should succeed");
        Assert.IsFalse(result.IsCreated, "Stage should already exist (IsCreated=false)");
        Assert.Equals(stageId, result.StageId);
    }

    /// <summary>
    /// CreateStage Callback 버전 - HTTP POST /api/stages/callback 성공
    /// callback 방식으로 OnCreate에 packet이 제대로 전달되는지 검증
    /// </summary>
    private async Task Test_CreateStage_Callback()
    {
        // Given
        var stageId = GenerateUniqueStageId(13000).ToString();
        var request = new CreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };

        // When - HTTP POST /api/stages/callback (callback 버전)
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/callback", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateStageResponse>();

        // Then - E2E 검증 (HTTP 응답만)
        Assert.IsTrue(result!.Success, "CreateStage callback should succeed");
        Assert.Equals(stageId, result.StageId);

        // OnCreate에 packet이 전달되었는지 검증 (reply에서 echo된 값 확인)
        Assert.IsTrue(result.ReplyPayloadId != null, "OnCreate callback should return reply with payload info");
        Assert.Equals("TestStage:10", result.ReplyPayloadId, "OnCreate callback should receive createPacket");
    }

    /// <summary>
    /// GetOrCreateStage Callback 버전 - HTTP POST /api/stages/get-or-create/callback 성공
    /// callback 방식으로 OnCreate에 packet이 제대로 전달되는지 검증
    /// </summary>
    private async Task Test_GetOrCreateStage_Callback()
    {
        // Given
        var stageId = GenerateUniqueStageId(14000).ToString();
        var request = new GetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = stageId
        };

        // When - HTTP POST /api/stages/get-or-create/callback (callback 버전)
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/get-or-create/callback", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();

        // Then - 새로운 Stage 생성 확인
        Assert.IsTrue(result!.Success, "GetOrCreateStage callback should succeed");
        Assert.IsTrue(result.IsCreated, "Stage should be newly created");
        Assert.Equals(stageId, result.StageId);

        // OnCreate에 packet이 전달되었는지 검증 (reply에서 echo된 값 확인)
        Assert.IsTrue(result.ReplyPayloadId != null, "OnCreate callback should return reply with payload info");
        Assert.Equals("TestStage:10", result.ReplyPayloadId, "OnCreate callback should receive createPacket");
    }
}
