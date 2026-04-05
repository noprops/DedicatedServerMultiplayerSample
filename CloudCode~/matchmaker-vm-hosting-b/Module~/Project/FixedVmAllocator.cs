using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Matchmaker;
using Unity.Services.CloudCode.Core;
using IExecutionContext = Unity.Services.CloudCode.Core.IExecutionContext;
using MatchProperties = Unity.Services.Matchmaker.Model.MatchProperties;

namespace MatchmakerVmHostingB;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton(GameApiClient.Create());
    }
}

public class FixedVmAllocator : IMatchmakerAllocator
{
    private const string LauncherBaseUrlSecretName = "DSMS_VM_B_LAUNCHER_BASE_URL";
    private const string LauncherApiTokenSecretName = "DSMS_VM_B_LAUNCHER_TOKEN";
    private const int DefaultExpectedPlayers = 2;
    private const int AllocateReadyTimeoutSeconds = 20;
    private const int AllocatePollIntervalMilliseconds = 1000;

    private static readonly HttpClient s_HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly JsonSerializerOptions s_JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IGameApiClient gameApiClient;

    public FixedVmAllocator(IGameApiClient gameApiClient)
    {
        this.gameApiClient = gameApiClient;
    }

    [CloudCodeFunction("allocate")]
    public async Task<AllocateResponse> Allocate(IExecutionContext context, AllocateRequest request)
    {
        var matchId = string.IsNullOrWhiteSpace(request.MatchId)
            ? Guid.NewGuid().ToString("N")
            : request.MatchId;
        var expectedAuthIds = ResolveExpectedAuthIds(request);
        var expectedPlayers = expectedAuthIds.Count > 0 ? expectedAuthIds.Count : DefaultExpectedPlayers;

        try
        {
            var launcherSettings = await LoadLauncherSettingsAsync(context);
            var launcherResponse = await WaitForAllocationReadyAsync(
                launcherSettings,
                matchId,
                expectedPlayers,
                expectedAuthIds);

            if (IsFailed(launcherResponse))
            {
                return new AllocateResponse(AllocateStatus.Error)
                {
                    Message = launcherResponse.Message ?? "launcher allocation failed"
                };
            }

            return new AllocateResponse(AllocateStatus.Created)
            {
                AllocationData = new Dictionary<string, object>
                {
                    { "matchId", matchId },
                    { "expectedPlayers", expectedPlayers }
                }
            };
        }
        catch (Exception ex)
        {
            return new AllocateResponse(AllocateStatus.Error)
            {
                Message = $"allocate request failed: {ex.Message}"
            };
        }
    }

    [CloudCodeFunction("poll")]
    public async Task<PollResponse> Poll(IExecutionContext context, PollRequest request)
    {
        var matchId = ResolveMatchId(request);
        if (string.IsNullOrWhiteSpace(matchId))
        {
            return new PollResponse(PollStatus.Error)
            {
                Message = "poll request did not include a match id"
            };
        }

        try
        {
            var launcherSettings = await LoadLauncherSettingsAsync(context);
            var launcherResponse = await SendPollAsync(launcherSettings, matchId);
            if (IsReady(launcherResponse))
            {
                return new PollResponse(PollStatus.Allocated)
                {
                    AssignmentData = AssignmentData.IpPort(
                        launcherResponse.Ip!,
                        launcherResponse.Port!.Value)
                };
            }

            if (IsFailed(launcherResponse))
            {
                return new PollResponse(PollStatus.Error)
                {
                    Message = launcherResponse.Message ?? "launcher reported failed status"
                };
            }

            return new PollResponse(PollStatus.Pending);
        }
        catch (Exception ex)
        {
            return new PollResponse(PollStatus.Error)
            {
                Message = $"poll request failed: {ex.Message}"
            };
        }
    }

    private static IReadOnlyList<string> ResolveExpectedAuthIds(AllocateRequest request)
    {
        if (request.MatchmakingResults?.MatchProperties == null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var rawJson = JsonSerializer.Serialize(request.MatchmakingResults.MatchProperties, s_JsonOptions);
            var matchProperties = JsonSerializer.Deserialize<MatchProperties>(rawJson, s_JsonOptions);
            if (matchProperties?.Players == null)
            {
                return Array.Empty<string>();
            }

            return matchProperties.Players
                .Select(player => player?.Id?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<LauncherSettings> LoadLauncherSettingsAsync(IExecutionContext context)
    {
        var baseUrlSecret = await gameApiClient.SecretManager.GetSecret(context, LauncherBaseUrlSecretName);
        var tokenSecret = await gameApiClient.SecretManager.GetSecret(context, LauncherApiTokenSecretName);

        var baseUrl = baseUrlSecret?.Value?.Trim();
        var token = tokenSecret?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException($"Missing secret: {LauncherBaseUrlSecretName}");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Missing secret: {LauncherApiTokenSecretName}");
        }

        return new LauncherSettings(baseUrl, token);
    }

    private static string? ResolveMatchId(PollRequest request)
    {
        if (request.AllocationData != null &&
            request.AllocationData.TryGetValue("matchId", out var fromAllocation) &&
            fromAllocation != null)
        {
            return fromAllocation.ToString();
        }

        return request.MatchId;
    }

    private static async Task<LauncherMatchResponse> SendAllocateAsync(
        LauncherSettings launcherSettings,
        string matchId,
        int expectedPlayers,
        IReadOnlyList<string> expectedAuthIds)
    {
        var payload = new LauncherAllocateRequest
        {
            MatchId = matchId,
            ExpectedPlayers = expectedPlayers,
            ExpectedAuthIds = expectedAuthIds
        };

        using var httpRequest = CreateRequest(launcherSettings, HttpMethod.Post, "matches/allocate");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, s_JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await s_HttpClient.SendAsync(httpRequest);
        return await ReadLauncherResponseAsync(response);
    }

    private static async Task<LauncherMatchResponse> SendPollAsync(LauncherSettings launcherSettings, string matchId)
    {
        using var httpRequest = CreateRequest(launcherSettings, HttpMethod.Get, $"matches/{Uri.EscapeDataString(matchId)}");
        using var response = await s_HttpClient.SendAsync(httpRequest);
        return await ReadLauncherResponseAsync(response);
    }

    private static async Task<LauncherMatchResponse> WaitForAllocationReadyAsync(
        LauncherSettings launcherSettings,
        string matchId,
        int expectedPlayers,
        IReadOnlyList<string> expectedAuthIds)
    {
        var launcherResponse = await SendAllocateAsync(
            launcherSettings,
            matchId,
            expectedPlayers,
            expectedAuthIds);
        if (IsReady(launcherResponse) || IsFailed(launcherResponse))
        {
            return launcherResponse;
        }

        var deadline = DateTime.UtcNow.AddSeconds(AllocateReadyTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(AllocatePollIntervalMilliseconds);
            launcherResponse = await SendPollAsync(launcherSettings, matchId);
            if (IsReady(launcherResponse) || IsFailed(launcherResponse))
            {
                return launcherResponse;
            }
        }

        return new LauncherMatchResponse
        {
            MatchId = matchId,
            Status = "failed",
            Message = "allocation timeout"
        };
    }

    private static HttpRequestMessage CreateRequest(LauncherSettings launcherSettings, HttpMethod method, string relativePath)
    {
        var uri = new Uri(new Uri(EnsureTrailingSlash(launcherSettings.BaseUrl)), relativePath);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {launcherSettings.ApiToken}");
        return request;
    }

    private static async Task<LauncherMatchResponse> ReadLauncherResponseAsync(HttpResponseMessage response)
    {
        LauncherMatchResponse? launcherResponse = null;
        try
        {
            launcherResponse = await response.Content.ReadFromJsonAsync<LauncherMatchResponse>(s_JsonOptions);
        }
        catch
        {
        }

        if (response.IsSuccessStatusCode)
        {
            return launcherResponse ?? new LauncherMatchResponse
            {
                Status = "failed",
                Message = "launcher returned an empty response"
            };
        }

        return launcherResponse ?? new LauncherMatchResponse
        {
            Status = "failed",
            Message = $"launcher http error: {(int)response.StatusCode} {response.ReasonPhrase}"
        };
    }

    private static bool IsReady(LauncherMatchResponse response)
    {
        return string.Equals(response.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.Ip) &&
               response.Port.HasValue;
    }

    private static bool IsFailed(LauncherMatchResponse response)
    {
        return string.Equals(response.Status, "failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private sealed class LauncherAllocateRequest
    {
        [JsonPropertyName("matchId")]
        public string MatchId { get; init; } = string.Empty;

        [JsonPropertyName("expectedPlayers")]
        public int ExpectedPlayers { get; init; } = DefaultExpectedPlayers;

        [JsonPropertyName("expectedAuthIds")]
        public IReadOnlyList<string> ExpectedAuthIds { get; init; } = Array.Empty<string>();
    }

    private sealed class LauncherMatchResponse
    {
        [JsonPropertyName("matchId")]
        public string? MatchId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("ip")]
        public string? Ip { get; init; }

        [JsonPropertyName("port")]
        public int? Port { get; init; }

        [JsonPropertyName("activeMatches")]
        public int? ActiveMatches { get; init; }

        [JsonPropertyName("maxConcurrentMatches")]
        public int? MaxConcurrentMatches { get; init; }
    }

    private sealed record LauncherSettings(string BaseUrl, string ApiToken);
}
