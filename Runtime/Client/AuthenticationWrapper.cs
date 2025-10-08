using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
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
                return true;
            }

            try
            {
                Debug.Log("[AuthenticationWrapper] Starting anonymous sign in...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log($"[AuthenticationWrapper] ✓ Signed in successfully");
                Debug.Log($"[AuthenticationWrapper] Player ID: {PlayerId}");

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
    }
}
