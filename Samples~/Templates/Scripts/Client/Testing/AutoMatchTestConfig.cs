using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.Testing
{
#if DSMS_SAMPLE_AUTO_MATCH_TEST
    internal static class AutoMatchTestConfig
    {
        private static bool s_parsed;
        private static bool s_enabled;
        private static string s_queueName = "competitive-queue";
        private static int s_instanceIndex = 1;
        private static string s_playerNamePrefix = "LoadClient";
        private static int s_autoMatchDelayMs = 1000;
        private static int s_autoMatchJitterMs = 1500;
        private static bool s_autoQuitOnSuccess = true;
        private static bool s_autoQuitOnFailure = true;
        private static int s_autoQuitTimeoutSeconds = 180;
        private static string s_autoChoiceStrategy = "cycle";

        public static bool Enabled
        {
            get
            {
                EnsureParsed();
                return s_enabled;
            }
        }

        public static string QueueName
        {
            get
            {
                EnsureParsed();
                return s_queueName;
            }
        }

        public static int InstanceIndex
        {
            get
            {
                EnsureParsed();
                return s_instanceIndex;
            }
        }

        public static string PlayerName
        {
            get
            {
                EnsureParsed();
                return $"{s_playerNamePrefix}-{s_instanceIndex:00}";
            }
        }

        public static string AuthProfileName
        {
            get
            {
                EnsureParsed();
                return $"auto-match-{s_instanceIndex:00}";
            }
        }

        public static bool AutoQuitOnSuccess
        {
            get
            {
                EnsureParsed();
                return s_autoQuitOnSuccess;
            }
        }

        public static bool AutoQuitOnFailure
        {
            get
            {
                EnsureParsed();
                return s_autoQuitOnFailure;
            }
        }

        public static int AutoQuitTimeoutSeconds
        {
            get
            {
                EnsureParsed();
                return s_autoQuitTimeoutSeconds;
            }
        }

        public static int GetInitialDelayMilliseconds()
        {
            EnsureParsed();
            var jitter = s_autoMatchJitterMs <= 0 ? 0 : Mathf.Abs((s_instanceIndex * 173) % (s_autoMatchJitterMs + 1));
            return Mathf.Max(0, s_autoMatchDelayMs + jitter);
        }

        public static Hand GetChoiceForRound(int roundIndex)
        {
            EnsureParsed();

            return s_autoChoiceStrategy.ToLowerInvariant() switch
            {
                "rock" => Hand.Rock,
                "paper" => Hand.Paper,
                "scissors" => Hand.Scissors,
                "random" => HandExtensions.RandomHand(),
                _ => Cycle(roundIndex)
            };
        }

        public static string Describe()
        {
            EnsureParsed();
            return $"enabled={s_enabled}, queue={s_queueName}, instance={s_instanceIndex}, playerName={PlayerName}, delayMs={s_autoMatchDelayMs}, jitterMs={s_autoMatchJitterMs}, autoQuitOnSuccess={s_autoQuitOnSuccess}, autoQuitOnFailure={s_autoQuitOnFailure}, quitTimeoutSeconds={s_autoQuitTimeoutSeconds}, choiceStrategy={s_autoChoiceStrategy}";
        }

        private static Hand Cycle(int roundIndex)
        {
            var normalized = Mathf.Abs((s_instanceIndex - 1 + roundIndex) % 3);
            return normalized switch
            {
                0 => Hand.Rock,
                1 => Hand.Paper,
                _ => Hand.Scissors
            };
        }

        private static void EnsureParsed()
        {
            if (s_parsed)
            {
                return;
            }

            s_parsed = true;
            var args = Environment.GetCommandLineArgs();
            var values = ParseArgs(args);

            s_enabled = values.ContainsKey("autoMatch");
            s_queueName = GetString(values, "queueName", s_queueName);
            s_instanceIndex = Mathf.Max(1, GetInt(values, "instanceIndex", s_instanceIndex));
            s_playerNamePrefix = GetString(values, "playerNamePrefix", s_playerNamePrefix);
            s_autoMatchDelayMs = Mathf.Max(0, GetInt(values, "autoMatchDelayMs", s_autoMatchDelayMs));
            s_autoMatchJitterMs = Mathf.Max(0, GetInt(values, "autoMatchJitterMs", s_autoMatchJitterMs));
            s_autoQuitOnSuccess = GetBool(values, "autoQuitOnSuccess", s_autoQuitOnSuccess);
            s_autoQuitOnFailure = GetBool(values, "autoQuitOnFailure", s_autoQuitOnFailure);
            s_autoQuitTimeoutSeconds = Mathf.Max(5, GetInt(values, "autoQuitTimeoutSeconds", s_autoQuitTimeoutSeconds));
            s_autoChoiceStrategy = GetString(values, "autoChoiceStrategy", s_autoChoiceStrategy);

            Debug.Log($"[AutoMatchTestConfig] {Describe()}");
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg) || !arg.StartsWith("-"))
                {
                    continue;
                }

                var key = arg.TrimStart('-');
                var value = "true";

                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                {
                    value = args[i + 1];
                    i++;
                }

                result[key] = value;
            }

            return result;
        }

        private static string GetString(IReadOnlyDictionary<string, string> values, string key, string defaultValue)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : defaultValue;
        }

        private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int defaultValue)
        {
            return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        {
            return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
                ? parsed
                : defaultValue;
        }
    }
#else
    internal static class AutoMatchTestConfig
    {
        public static bool Enabled => false;
        public static string QueueName => "competitive-queue";
        public static int InstanceIndex => 1;
        public static string PlayerName => "LoadClient-01";
        public static string AuthProfileName => string.Empty;
        public static bool AutoQuitOnSuccess => true;
        public static bool AutoQuitOnFailure => true;
        public static int AutoQuitTimeoutSeconds => 180;

        public static int GetInitialDelayMilliseconds()
        {
            return 0;
        }

        public static Hand GetChoiceForRound(int roundIndex)
        {
            return Hand.Rock;
        }

        public static string Describe()
        {
            return "enabled=false";
        }
    }
#endif
}
