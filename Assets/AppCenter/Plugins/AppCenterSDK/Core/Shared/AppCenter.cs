// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Reflection;
using Microsoft.AppCenter.Unity.Internal;
using UnityEngine;

namespace Microsoft.AppCenter.Unity
{
#if UNITY_IOS || UNITY_ANDROID
    using ServiceType = System.IntPtr;
#else
    using ServiceType = System.Type;
#endif

    public class AppCenter
    {
        public static LogLevel LogLevel
        {
            get { return (LogLevel)AppCenterInternal.GetLogLevel(); }
            set { AppCenterInternal.SetLogLevel((int)value); }
        }

        public static AppCenterTask SetEnabledAsync(bool enabled)
        {
            return AppCenterInternal.SetEnabledAsync(enabled);
        }

        public static void StartFromLibrary(ServiceType[] servicesArray)
        {
            AppCenterInternal.StartFromLibrary(servicesArray);
        }

        public static AppCenterTask<bool> IsEnabledAsync()
        {
            return AppCenterInternal.IsEnabledAsync();
        }

        /// <summary>
        /// Get the unique installation identifier for this application installation on this device.
        /// </summary>
        /// <remarks>
        /// The identifier is lost if clearing application data or uninstalling application.
        /// </remarks>
        public static AppCenterTask<Guid?> GetInstallIdAsync()
        {
            var stringTask = AppCenterInternal.GetInstallIdAsync();
            var guidTask = new AppCenterTask<Guid?>();
            stringTask.ContinueWith(t =>
            {
                var installId = !string.IsNullOrEmpty(t.Result) ? new Guid(t.Result) : (Guid?)null;
                guidTask.SetResult(installId);
            });
            return guidTask;
        }

        /// <summary>
        /// Change the base URL (scheme + authority + port only) used to communicate with the backend.
        /// </summary>
        /// <param name="logUrl">Base URL to use for server communication.</param>
        public static void SetLogUrl(string logUrl)
        {
            AppCenterInternal.SetLogUrl(logUrl);
        }

        /// <summary>
        /// Check whether SDK has already been configured or not.
        /// </summary>
        public static bool Configured
        {
            get { return AppCenterInternal.IsConfigured(); }
        }

        /// <summary>
        /// Set the custom properties.
        /// </summary>
        /// <param name="customProperties">Custom properties object.</param>
        public static void SetCustomProperties(Unity.CustomProperties customProperties)
        {
            var rawCustomProperties = customProperties.GetRawObject();
            AppCenterInternal.SetCustomProperties(rawCustomProperties);
        }

        public static void SetWrapperSdk()
        {
            AppCenterInternal.SetWrapperSdk(WrapperSdk.WrapperSdkVersion, WrapperSdk.Name, WrapperSdk.WrapperRuntimeVersion, null, null, null);
        }

        // Gets the first instance of an app secret corresponding to the given platform name, or returns the string
        // as-is if no identifier can be found.
        public static string GetSecretForPlatform(string secrets)
        {
            var platformIdentifier = GetPlatformIdentifier();
            if (platformIdentifier == null)
            {
                // Return as is for unsupported platform.
                return secrets;
            }
            if (secrets == null)
            {
                // If "secrets" is null, return that and let the error be dealt
                // with downstream.
                return secrets;
            }

            // If there are no equals signs, then there are no named identifiers
            if (!secrets.Contains("="))
            {
                return secrets;
            }

            var platformIndicator = platformIdentifier + "=";
            var secretIdx = secrets.IndexOf(platformIndicator, StringComparison.Ordinal);
            if (secretIdx == -1)
            {
                // If the platform indicator can't be found, return the original
                // string and let the error be dealt with downstream.
                return secrets;
            }
            secretIdx += platformIndicator.Length;
            var platformSecret = string.Empty;

            while (secretIdx < secrets.Length)
            {
                var nextChar = secrets[secretIdx++];
                if (nextChar == ';')
                {
                    break;
                }

                platformSecret += nextChar;
            }

            return platformSecret;
        }

        private static string GetPlatformIdentifier()
        {
#if UNITY_IOS
            return "ios";
#elif UNITY_ANDROID
            return "android";
#elif UNITY_WSA_10_0
            return "uwp";
#else
            return null;
#endif
        }
    }
}
