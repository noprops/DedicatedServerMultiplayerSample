using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Static wrapper around the Unity Authentication Service that centralizes client authentication flows.
    /// </summary>
    public static class AuthenticationWrapper
    {
        // ========== Properties ==========

        /// <summary>
        /// True when Unity Services are initialized and the player is signed in.
        /// </summary>
        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        /// <summary>
        /// Player ID if authenticated; otherwise null.
        /// </summary>
        public static string PlayerId =>
            IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

        // ========== Public Methods ==========

        /// <summary>
        /// Performs anonymous authentication. Intended to run once during app startup.
        /// </summary>
        public static async Task<bool> SignInAnonymouslyAsync()
        {
            // Unity Services must be initialized before signing in.
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.LogError("[AuthenticationWrapper] Unity Services not initialized");
                return false;
            }

            // Return success if already signed in.
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
        /// Signs the player out if currently signed in.
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
        /// Refreshes the session token, intended for long-running play sessions.
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
