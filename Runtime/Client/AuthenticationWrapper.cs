using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace MultiplayerServicesTest.Client
{
    /// <summary>
    /// Unity Authentication Serviceのラッパークラス（静的クラス）
    /// クライアント側の認証処理を一元管理
    /// </summary>
    public static class AuthenticationWrapper
    {
        // ========== Properties ==========

        /// <summary>
        /// 認証済みかどうか
        /// </summary>
        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        /// <summary>
        /// プレイヤーID（認証済みの場合のみ有効）
        /// </summary>
        public static string PlayerId =>
            IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

        /// <summary>
        /// プレイヤー名（認証済みの場合のみ有効）
        /// </summary>
        public static string PlayerName { get; private set; }

        // ========== Public Methods ==========

        /// <summary>
        /// 匿名認証を実行
        /// アプリ起動時に一度だけ呼ばれることを想定
        /// </summary>
        public static async Task<bool> SignInAnonymouslyAsync()
        {
            // Unity Servicesが初期化されていない場合はエラー
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.LogError("[AuthenticationWrapper] Unity Services not initialized");
                return false;
            }

            // 既にサインイン済みの場合は成功を返す
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"[AuthenticationWrapper] Already signed in as: {PlayerId}");
                UpdatePlayerName();
                return true;
            }

            try
            {
                Debug.Log("[AuthenticationWrapper] Starting anonymous sign in...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log($"[AuthenticationWrapper] ✓ Signed in successfully");
                Debug.Log($"[AuthenticationWrapper] Player ID: {PlayerId}");

                UpdatePlayerName();

                return true;
            }
            catch (AuthenticationException e)
            {
                Debug.LogError($"[AuthenticationWrapper] Authentication failed: {e.Message}");
                Debug.LogError($"[AuthenticationWrapper] Error code: {e.ErrorCode}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthenticationWrapper] Unexpected error during authentication: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// プレイヤー名を更新
        /// </summary>
        public static async Task<bool> UpdatePlayerNameAsync(string newName)
        {
            if (!IsSignedIn)
            {
                Debug.LogError("[AuthenticationWrapper] Cannot update player name: Not authenticated");
                return false;
            }

            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(newName);
                PlayerName = newName;
                Debug.Log($"[AuthenticationWrapper] Player name updated to: {newName}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthenticationWrapper] Failed to update player name: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// サインアウト
        /// </summary>
        public static void SignOut()
        {
            if (!IsSignedIn)
            {
                Debug.LogWarning("[AuthenticationWrapper] Already signed out");
                return;
            }

            try
            {
                AuthenticationService.Instance.SignOut();
                PlayerName = null;
                Debug.Log("[AuthenticationWrapper] Signed out successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthenticationWrapper] Failed to sign out: {e.Message}");
            }
        }

        /// <summary>
        /// セッショントークンのリフレッシュ
        /// 長時間のプレイセッション用
        /// </summary>
        public static async Task<bool> RefreshSessionTokenAsync()
        {
            if (!IsSignedIn)
            {
                Debug.LogError("[AuthenticationWrapper] Cannot refresh token: Not authenticated");
                return false;
            }

            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("[AuthenticationWrapper] Session token refreshed");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthenticationWrapper] Failed to refresh session token: {e.Message}");
                return false;
            }
        }

        // ========== Private Methods ==========

        /// <summary>
        /// プレイヤー名を生成または取得
        /// </summary>
        private static void UpdatePlayerName()
        {
            // 将来的にCloud Saveから取得する場合はここで処理
            // 現在はランダム名を生成
            if (string.IsNullOrEmpty(PlayerName))
            {
                PlayerName = GenerateRandomPlayerName();
                Debug.Log($"[AuthenticationWrapper] Generated player name: {PlayerName}");
            }
        }

        /// <summary>
        /// ランダムなプレイヤー名を生成
        /// </summary>
        private static string GenerateRandomPlayerName()
        {
            string[] adjectives = { "Swift", "Brave", "Mighty", "Silent", "Clever", "Bold", "Fierce", "Noble" };
            string[] nouns = { "Tiger", "Eagle", "Wolf", "Dragon", "Hawk", "Lion", "Panther", "Phoenix" };

            var random = new System.Random();
            string adjective = adjectives[random.Next(adjectives.Length)];
            string noun = nouns[random.Next(nouns.Length)];
            int number = random.Next(100, 999);

            return $"{adjective}{noun}{number}";
        }
    }
}