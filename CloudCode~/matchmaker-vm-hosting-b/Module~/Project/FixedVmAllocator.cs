using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Matchmaker;
using Unity.Services.CloudCode.Core;
using IExecutionContext = Unity.Services.CloudCode.Core.IExecutionContext;

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
    private readonly ILogger<FixedVmAllocator> logger;

    public FixedVmAllocator(IGameApiClient gameApiClient, ILogger<FixedVmAllocator> logger)
    {
        this.gameApiClient = gameApiClient;
        this.logger = logger;
    }

    [CloudCodeFunction("allocate")]
    public async Task<AllocateResponse> Allocate(IExecutionContext context, AllocateRequest request)
    {
        var matchId = string.IsNullOrWhiteSpace(request.MatchId)
            ? Guid.NewGuid().ToString("N")
            : request.MatchId;

        try
        {
            var matchProperties = ResolveMatchPropertiesObject(request);
            var teamCount = ResolveTeamCount(matchProperties);
            var playerCount = ResolvePlayerCount(matchProperties);
            var expectedAuthIds = ResolveExpectedAuthIds(matchProperties);
            var expectedPlayers = ResolveExpectedPlayers(teamCount, playerCount, expectedAuthIds);
            if (expectedAuthIds.Count == 0)
            {
                LogAllocatorWarning($"Allocate matchId={matchId} resolved no expectedAuthIds; matchPropertiesKeys={DescribeMatchPropertiesKeys(request.MatchmakingResults?.MatchProperties)}, teams={teamCount}, players={playerCount}, expectedPlayers={expectedPlayers}");
            }
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
                    { "MatchId", matchId },
                    { "assignmentId", matchId },
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

    private static object? ResolveMatchPropertiesObject(AllocateRequest request)
    {
        if (request.MatchmakingResults?.MatchProperties == null)
        {
            return null;
        }

        return request.MatchmakingResults.MatchProperties;
    }

    private static IReadOnlyList<string> ResolveExpectedAuthIds(object? matchProperties)
    {
        if (matchProperties == null)
        {
            return Array.Empty<string>();
        }

        var expectedAuthIds = new HashSet<string>(StringComparer.Ordinal);
        if (TryGetNamedValue(matchProperties, "players", out var playersElement))
        {
            foreach (var playerElement in EnumerateArray(playersElement))
            {
                if (!TryGetNamedValue(playerElement, "id", out var idElement))
                {
                    continue;
                }

                var authId = TryReadString(idElement)?.Trim();
                if (!string.IsNullOrWhiteSpace(authId))
                {
                    expectedAuthIds.Add(authId);
                }
            }
        }

        if (expectedAuthIds.Count == 0 &&
            TryGetNamedValue(matchProperties, "teams", out var teamsElement))
        {
            foreach (var teamElement in EnumerateArray(teamsElement))
            {
                if (!TryGetNamedValue(teamElement, "playerIds", out var playerIdsElement))
                {
                    continue;
                }

                foreach (var playerIdElement in EnumerateArray(playerIdsElement))
                {
                    var authId = TryReadString(playerIdElement)?.Trim();
                    if (!string.IsNullOrWhiteSpace(authId))
                    {
                        expectedAuthIds.Add(authId);
                    }
                }
            }
        }

        return expectedAuthIds.ToArray();
    }

    private static int ResolveExpectedPlayers(int teamCount, int playerCount, IReadOnlyList<string> expectedAuthIds)
    {
        if (expectedAuthIds.Count > 0)
        {
            return expectedAuthIds.Count;
        }

        if (playerCount > 0)
        {
            return playerCount;
        }

        if (teamCount > 0)
        {
            return teamCount;
        }

        return DefaultExpectedPlayers;
    }

    private static string DescribeMatchPropertiesKeys(Dictionary<string, object>? matchProperties)
    {
        if (matchProperties == null || matchProperties.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",",
            matchProperties.Select(pair => $"{pair.Key}:{pair.Value?.GetType().Name ?? "null"}"));
    }

    private static int ResolveTeamCount(object? matchProperties)
    {
        if (!TryGetNamedValue(matchProperties, "teams", out var teamsElement))
        {
            return 0;
        }

        return EnumerateArray(teamsElement).Count();
    }

    private static int ResolvePlayerCount(object? matchProperties)
    {
        if (!TryGetNamedValue(matchProperties, "players", out var playersElement))
        {
            return 0;
        }

        return EnumerateArray(playersElement).Count();
    }

    private static bool TryGetNamedValue(object? container, string propertyName, out object? value)
    {
        if (container == null)
        {
            value = null;
            return false;
        }

        if (container is JsonDocument jsonDocument)
        {
            return TryGetNamedValue(jsonDocument.RootElement, propertyName, out value);
        }

        if (container is JObject jsonObject)
        {
            foreach (var property in jsonObject.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        if (container is JProperty jsonProperty)
        {
            return TryGetNamedValue(jsonProperty.Value, propertyName, out value);
        }

        if (container is JToken jsonToken)
        {
            if (jsonToken.Type == JTokenType.Object)
            {
                return TryGetNamedValue((JObject)jsonToken, propertyName, out value);
            }

            value = null;
            return false;
        }

        if (container is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in jsonElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        if (container is IDictionary<string, object> stringObjectDictionary)
        {
            foreach (var pair in stringObjectDictionary)
            {
                if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        if (container is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key &&
                    string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            }
        }

        var properties = container.GetType().GetProperties()
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
        foreach (var property in properties)
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.GetValue(container);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IEnumerable<object?> EnumerateArray(object? value)
    {
        if (value == null)
        {
            return Array.Empty<object?>();
        }

        if (value is JsonDocument jsonDocument)
        {
            return EnumerateArray(jsonDocument.RootElement);
        }

        if (value is JArray jsonArray)
        {
            return jsonArray.Select(item => (object?)item).ToArray();
        }

        if (value is JToken jsonToken)
        {
            return jsonToken.Type == JTokenType.Array
                ? ((JArray)jsonToken).Select(item => (object?)item).ToArray()
                : Array.Empty<object?>();
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.Array
                ? jsonElement.EnumerateArray().Select(item => (object?)item).ToArray()
                : Array.Empty<object?>();
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var items = new List<object?>();
            foreach (var item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }

        return Array.Empty<object?>();
    }

    private static string? TryReadString(object? value)
    {
        return value switch
        {
            null => null,
            JsonDocument jsonDocument => TryReadString(jsonDocument.RootElement),
            JValue jsonValue => jsonValue.Value?.ToString(),
            JToken jsonToken when jsonToken.Type == JTokenType.String || jsonToken.Type == JTokenType.Integer || jsonToken.Type == JTokenType.Float || jsonToken.Type == JTokenType.Boolean => jsonToken.ToString(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String => jsonElement.GetString(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetRawText(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.True => bool.TrueString,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
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
        if (TryGetAllocationDataValue(request.AllocationData, "matchId", out var fromAllocation) &&
            fromAllocation != null)
        {
            return TryReadString(fromAllocation);
        }

        if (TryGetAllocationDataValue(request.AllocationData, "MatchId", out fromAllocation) &&
            fromAllocation != null)
        {
            return TryReadString(fromAllocation);
        }

        if (TryGetAllocationDataValue(request.AllocationData, "assignmentId", out fromAllocation) &&
            fromAllocation != null)
        {
            return TryReadString(fromAllocation);
        }

        return request.MatchId;
    }

    private static bool TryGetAllocationDataValue(IDictionary<string, object>? allocationData, string key, out object? value)
    {
        if (allocationData != null)
        {
            foreach (var pair in allocationData)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private async Task<LauncherMatchResponse> SendAllocateAsync(
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

                using var response = await s_HttpClient.SendAsync(httpRequest);
                var launcherResponse = await ReadLauncherResponseAsync(requestUri, response);
                if (attempt == 0 && ShouldRetryAllocateResponse(response))
                {
                    LogAllocatorWarning($"Launcher allocate retrying after HTTP {(int)response.StatusCode} for uri={requestUri}");
                    await Task.Delay(AllocateRetryDelayMilliseconds);
                    continue;
                }

                return launcherResponse;
            }
            catch (Exception ex) when (attempt == 0 && IsTransientLauncherException(ex))
            {
                LogAllocatorWarning($"Launcher allocate transient failure attempt={attempt + 1} uri={requestUri}: {FormatException(ex)}; retrying");
                await Task.Delay(AllocateRetryDelayMilliseconds);
            }
        }

        throw new InvalidOperationException($"launcher allocate retry loop exhausted for uri={requestUri}");
    }

    private async Task<LauncherMatchResponse> SendPollAsync(LauncherSettings launcherSettings, string matchId)
    {
        var requestUri = new Uri(
            new Uri(EnsureTrailingSlash(launcherSettings.BaseUrl)),
            $"matches/{Uri.EscapeDataString(matchId)}");
        using var httpRequest = CreateRequest(launcherSettings, HttpMethod.Get, requestUri);

        using var response = await s_HttpClient.SendAsync(httpRequest);
        return await ReadLauncherResponseAsync(requestUri, response);
    }

    private async Task<LauncherMatchResponse> WaitForAllocationReadyAsync(
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

    private async Task<LauncherMatchResponse> ReadLauncherResponseAsync(Uri requestUri, HttpResponseMessage response)
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

    private void LogAllocator(string message)
    {
        logger.LogInformation("{allocatorMessage}", $"[FixedVmAllocator] {message}");
    }

    private void LogAllocatorWarning(string message)
    {
        logger.LogWarning("{allocatorMessage}", $"[FixedVmAllocator] {message}");
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
