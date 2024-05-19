using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AssetBundles
{
    public class Utility
    {
        public const string AssetBundlesOutputPath = "AssetBundles";

        public static string GetPlatformName()
        {
#if UNITY_EDITOR
            return GetPlatformForAssetBundles(EditorUserBuildSettings.activeBuildTarget);
#else
			return GetPlatformForAssetBundles(Application.platform);
#endif
        }

#if UNITY_EDITOR
        public static string GetPlatformForAssetBundles(BuildTarget target)
        {
            switch (target) {
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.tvOS:
                    return "tvOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "StandaloneWindows";
                case BuildTarget.StandaloneOSX:
                    return "StandaloneOSX";
                // Add more build targets for your own.
                // If you add more targets, don't forget to add the same platforms to the function below.
                case BuildTarget.StandaloneLinux64:
#if !UNITY_2019_1_OR_NEWER
                case BuildTarget.StandaloneLinuxUniversal:
                case BuildTarget.StandaloneLinux:
#endif
                    return "StandaloneLinux";
                case BuildTarget.Switch:
                    return "Switch";
                default:
                    Debug.Log("Unknown BuildTarget: Using Default Enum Name: " + target);
                    return target.ToString();
            }
        }
#endif

        public static string GetPlatformForAssetBundles(RuntimePlatform platform)
        {
            switch (platform) {
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.tvOS:
                    return "tvOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                case RuntimePlatform.WindowsPlayer:
                    return "StandaloneWindows";
                case RuntimePlatform.OSXPlayer:
                    return "StandaloneOSX";
                case RuntimePlatform.LinuxPlayer:
                    return "StandaloneLinux";
                case RuntimePlatform.Switch:
                    return "Switch";
                // Add more build targets for your own.
                // If you add more targets, don't forget to add the same platforms to the function above.
                default:
                    Debug.Log("Unknown BuildTarget: Using Default Enum Name: " + platform);
                    return platform.ToString();
            }
        }
    }
}
