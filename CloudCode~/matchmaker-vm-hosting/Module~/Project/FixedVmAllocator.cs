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

namespace MatchmakerVmHosting;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton(GameApiClient.Create());
    }
}

public class FixedVmAllocator : IMatchmakerAllocator
{
    private const string LauncherBaseUrlSecretName = "DSMS_VM_LAUNCHER_BASE_URL";
    private const string LauncherApiTokenSecretName = "DSMS_VM_LAUNCHER_TOKEN";
    private const int DefaultExpectedPlayers = 2;
    private const int AllocateReadyTimeoutSeconds = 20;
    private const int AllocatePollIntervalMilliseconds = 1000;
    private const int AllocateRetryDelayMilliseconds = 250;

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
        var matchProperties = ResolveMatchProperties(request);
        var expectedAuthIds = ResolveExpectedAuthIds(matchProperties);
        var expectedPlayers = ResolveExpectedPlayers(matchProperties, expectedAuthIds);

        try
        {
            LogAllocator($"Allocate start matchId={matchId}, expectedPlayers={expectedPlayers}, expectedAuthIds={expectedAuthIds.Count}");
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
            LogAllocator($"Allocate exception matchId={matchId}: {FormatException(ex)}");
            return new AllocateResponse(AllocateStatus.Error)
            {
                Message = $"allocate request failed: {FormatException(ex)}"
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
            LogAllocator($"Poll start matchId={matchId}");
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
            LogAllocator($"Poll exception matchId={matchId}: {FormatException(ex)}");
            return new PollResponse(PollStatus.Error)
            {
                Message = $"poll request failed: {FormatException(ex)}"
            };
        }
    }

    private static MatchProperties? ResolveMatchProperties(AllocateRequest request)
    {
        if (request.MatchmakingResults?.MatchProperties == null)
        {
            return null;
        }

        try
        {
            var rawJson = JsonSerializer.Serialize(request.MatchmakingResults.MatchProperties, s_JsonOptions);
            return JsonSerializer.Deserialize<MatchProperties>(rawJson, s_JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ResolveExpectedAuthIds(MatchProperties? matchProperties)
    {
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

    private static int ResolveExpectedPlayers(MatchProperties? matchProperties, IReadOnlyList<string> expectedAuthIds)
    {
        if (IsSinglePlayerFallbackMatch(matchProperties, expectedAuthIds))
        {
            return 1;
        }

        return expectedAuthIds.Count > 0 ? expectedAuthIds.Count : DefaultExpectedPlayers;
    }

    private static bool IsSinglePlayerFallbackMatch(MatchProperties? matchProperties, IReadOnlyList<string> expectedAuthIds)
    {
        if (expectedAuthIds.Count != 1)
        {
            return false;
        }

        if (matchProperties?.Teams?.Count == 1)
        {
            return true;
        }

        return matchProperties?.Players?.Count == 1;
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

        var requestUri = new Uri(new Uri(EnsureTrailingSlash(launcherSettings.BaseUrl)), "matches/allocate");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var httpRequest = CreateRequest(launcherSettings, HttpMethod.Post, requestUri);
                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(payload, s_JsonOptions),
                    Encoding.UTF8,
                    "application/json");

                LogAllocator($"Launcher allocate HTTP POST attempt={attempt + 1} uri={requestUri}");
                using var response = await s_HttpClient.SendAsync(httpRequest);
                var launcherResponse = await ReadLauncherResponseAsync(requestUri, response);
                if (attempt == 0 && ShouldRetryAllocateResponse(response))
                {
                    LogAllocator($"Launcher allocate retrying after HTTP {(int)response.StatusCode} for uri={requestUri}");
                    await Task.Delay(AllocateRetryDelayMilliseconds);
                    continue;
                }

                return launcherResponse;
            }
            catch (Exception ex) when (attempt == 0 && IsTransientLauncherException(ex))
            {
                LogAllocator($"Launcher allocate transient failure attempt={attempt + 1} uri={requestUri}: {FormatException(ex)}; retrying");
                await Task.Delay(AllocateRetryDelayMilliseconds);
            }
        }

        throw new InvalidOperationException($"launcher allocate retry loop exhausted for uri={requestUri}");
    }

    private static async Task<LauncherMatchResponse> SendPollAsync(LauncherSettings launcherSettings, string matchId)
    {
        var requestUri = new Uri(
            new Uri(EnsureTrailingSlash(launcherSettings.BaseUrl)),
            $"matches/{Uri.EscapeDataString(matchId)}");
        using var httpRequest = CreateRequest(launcherSettings, HttpMethod.Get, requestUri);

        LogAllocator($"Launcher poll HTTP GET uri={requestUri}");
        using var response = await s_HttpClient.SendAsync(httpRequest);
        return await ReadLauncherResponseAsync(requestUri, response);
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

    private static HttpRequestMessage CreateRequest(LauncherSettings launcherSettings, HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {launcherSettings.ApiToken}");
        return request;
    }

    private static async Task<LauncherMatchResponse> ReadLauncherResponseAsync(Uri requestUri, HttpResponseMessage response)
    {
        var responseBody = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync();

        LauncherMatchResponse? launcherResponse = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                launcherResponse = JsonSerializer.Deserialize<LauncherMatchResponse>(responseBody, s_JsonOptions);
            }
            catch (Exception ex)
            {
                LogAllocator($"Launcher response JSON parse failed uri={requestUri}, status={(int)response.StatusCode}: {FormatException(ex)}");
            }
        }

        if (response.IsSuccessStatusCode)
        {
            return launcherResponse ?? new LauncherMatchResponse
            {
                Status = "failed",
                Message = "launcher returned an empty response"
            };
        }

        var statusSummary = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        var message = !string.IsNullOrWhiteSpace(launcherResponse?.Message)
            ? launcherResponse!.Message!
            : !string.IsNullOrWhiteSpace(responseBody)
                ? $"launcher http error: {statusSummary}; body={Truncate(responseBody, 400)}"
                : $"launcher http error: {statusSummary}";
        LogAllocator($"Launcher HTTP failure uri={requestUri}, status={statusSummary}, message={message}");

        return launcherResponse ?? new LauncherMatchResponse
        {
            Status = "failed",
            Message = message
        };
    }

    private static bool ShouldRetryAllocateResponse(HttpResponseMessage response)
    {
        return (int)response.StatusCode >= 500;
    }

    private static bool IsTransientLauncherException(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or TimeoutException;
    }

    private static string FormatException(Exception ex)
    {
        var message = $"{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException != null)
        {
            message += $" | Inner {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }

        return message;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

    private static void LogAllocator(string message)
    {
        Console.WriteLine($"[FixedVmAllocator] {message}");
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
