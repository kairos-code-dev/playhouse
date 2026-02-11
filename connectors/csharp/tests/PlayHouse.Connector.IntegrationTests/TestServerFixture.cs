using System.Net.Http.Json;
using System.Text.Json;

namespace PlayHouse.Connector.IntegrationTests;

/// <summary>
/// 테스트 서버 Stage 생성 응답
/// </summary>
public record CreateStageResponse(
    bool Success,
    int StageId,
    string StageType,
    string? ReplyPayloadId
);

/// <summary>
/// 테스트 서버 연결 관리 Fixture
/// </summary>
/// <remarks>
/// 테스트 서버의 HTTP API를 통해 Stage를 생성하고,
/// TCP 연결 정보를 제공합니다.
/// 환경 변수로 서버 주소 설정 가능:
    /// - TEST_SERVER_HOST (기본: 127.0.0.1)
/// - TEST_SERVER_HTTP_PORT (기본: 8080)
/// - TEST_SERVER_HTTPS_PORT (기본: 8443)
/// - TEST_SERVER_TCP_PORT (기본: 34001)
/// - TEST_SERVER_TCP_TLS_PORT (기본: 34002)
/// </remarks>
public class TestServerFixture : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// 테스트 서버 호스트
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// 테스트 서버 TCP 포트
    /// </summary>
    public int TcpPort { get; }

    /// <summary>
    /// 테스트 서버 TCP TLS 포트
    /// </summary>
    public int TcpTlsPort { get; }

    /// <summary>
    /// 테스트 서버 HTTP 포트
    /// </summary>
    public int HttpPort { get; }

    /// <summary>
    /// 테스트 서버 HTTPS 포트
    /// </summary>
    public int HttpsPort { get; }

    public TestServerFixture()
    {
        Host = Environment.GetEnvironmentVariable("TEST_SERVER_HOST") ?? "127.0.0.1";
        HttpPort = int.Parse(Environment.GetEnvironmentVariable("TEST_SERVER_HTTP_PORT") ?? "8080");
        HttpsPort = int.Parse(Environment.GetEnvironmentVariable("TEST_SERVER_HTTPS_PORT") ?? "8443");
        TcpPort = int.Parse(Environment.GetEnvironmentVariable("TEST_SERVER_TCP_PORT") ?? "34001");
        TcpTlsPort = int.Parse(Environment.GetEnvironmentVariable("TEST_SERVER_TCP_TLS_PORT") ?? "34002");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{Host}:{HttpPort}"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    private static int _stageIdCounter = 1;

    /// <summary>
    /// API 응답 매핑용 내부 클래스
    /// </summary>
    private record ApiCreateStageResponse(bool Success, int StageId, string? ReplyPayloadId);
    private record ApiAssignStageResponse(bool Success, string UserId, long StageId);

    /// <summary>
    /// 테스트용 Stage 생성
    /// </summary>
    /// <param name="stageType">Stage 타입</param>
    /// <param name="maxPlayers">최대 플레이어 수 (선택)</param>
    /// <returns>생성된 Stage 정보</returns>
    public async Task<CreateStageResponse> CreateStageAsync(string stageType, int? maxPlayers = null)
    {
        const int maxAttempts = 5;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var stageId = Interlocked.Increment(ref _stageIdCounter);
            var request = new
            {
                stageType,
                stageId
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/stages", request);
                response.EnsureSuccessStatusCode();

                var apiResult = await response.Content.ReadFromJsonAsync<ApiCreateStageResponse>(_jsonOptions);
                if (apiResult == null)
                {
                    throw new InvalidOperationException("Failed to create stage: null response");
                }

                if (apiResult.Success)
                {
                    return new CreateStageResponse(apiResult.Success, apiResult.StageId, stageType, apiResult.ReplyPayloadId);
                }

                lastError = new InvalidOperationException($"Stage already exists: {apiResult.StageId}");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException("Failed to create stage after retries", lastError);
    }

    /// <summary>
    /// 기본 테스트 Stage 생성 (TestStage 타입)
    /// </summary>
    /// <returns>생성된 Stage 정보</returns>
    public Task<CreateStageResponse> CreateTestStageAsync()
    {
        return CreateStageAsync("TestStage");
    }

    public async Task AssignStageAsync(string userId, long stageId)
    {
        var request = new
        {
            userId,
            stageId
        };

        var response = await _httpClient.PostAsJsonAsync("/api/stage-assignments", request);
        response.EnsureSuccessStatusCode();

        var apiResult = await response.Content.ReadFromJsonAsync<ApiAssignStageResponse>(_jsonOptions);
        if (apiResult == null || !apiResult.Success)
        {
            throw new InvalidOperationException($"Failed to assign stage for user {userId}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
