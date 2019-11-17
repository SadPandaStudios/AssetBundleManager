using System;
using System.Collections;
using UnityEngine;

namespace AssetBundles
{
    /// <summary>
    ///     Decorator for AssetBundleDownloader that attempts to use assets in the StreamingAssets folder before moving to the
    ///     next handler in the chain.
    /// </summary>
    public class StreamingAssetsBundleDownloadDecorator : ICommandHandler<AssetBundleDownloadCommand>
    {
        private string fullBundlePath;
        private ICommandHandler<AssetBundleDownloadCommand> decorated;
        private string remoteManifestName;

        private AssetBundleManifest manifest;
        private AssetBundleManager.PrioritizationStrategy currentStrategy;
        private Action<IEnumerator> coroutineHandler;
        private string currentPlatform;

        /// <param name="remoteManifestName">
        ///     Filename for the remote manifest, so this decorator knows it should be ignored.
        /// </param>
        /// <param name="decorated">CommandHandler to use when the bundle is not available in StreamingAssets</param>
        /// <param name="strategy">
        ///     Strategy to use.  Defaults to having remote bundle override StreamingAssets bundle if the hashes
        ///     are different
        /// </param>
        public StreamingAssetsBundleDownloadDecorator(string remoteManifestName, ICommandHandler<AssetBundleDownloadCommand> decorated, AssetBundleManager.PrioritizationStrategy strategy)
        {
            this.decorated = decorated;
            this.remoteManifestName = remoteManifestName;
            currentStrategy = strategy;
            currentPlatform = Utility.GetPlatformName();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                coroutineHandler = EditorCoroutine.Start;
            else
#endif
                coroutineHandler = AssetBundleDownloaderMonobehaviour.Instance.HandleCoroutine;

            fullBundlePath = string.Format("{0}/{1}", Application.streamingAssetsPath, Utility.GetPlatformName());
            var manifestBundle = AssetBundle.LoadFromFile(string.Format("{0}/{1}", fullBundlePath, currentPlatform));

            if (manifestBundle == null) {
                Debug.LogWarning("Unable to retrieve manifest file from StreamingAssets, disabling StreamingAssetsBundleDownloadDecorator.");
            } else {
                manifest = manifestBundle.LoadAsset<AssetBundleManifest>("assetbundlemanifest");
                manifestBundle.Unload(false);
            }
        }

        /// <summary>
        ///     Handle the download command
        /// </summary>
        public void Handle(AssetBundleDownloadCommand cmd)
        {
            coroutineHandler(InternalHandle(cmd));
        }

        /// <summary>
        ///     Returns the manifest in the StreamingAssets folder
        /// </summary>
        public AssetBundleManifest GetManifest()
        {
            return manifest;
        }

        private IEnumerator InternalHandle(AssetBundleDownloadCommand cmd)
        {
            // Never use StreamingAssets for the manifest bundle, always try to use it for bundles with a matching hash (Unless the strategy says otherwise)
            if (manifest != null && cmd.BundleName != currentPlatform && (currentStrategy == AssetBundleManager.PrioritizationStrategy.PrioritizeStreamingAssets || manifest.GetAssetBundleHash(cmd.BundleName) == cmd.Hash)) {
                Debug.Log(string.Format("Using StreamingAssets for bundle [{0}]", cmd.BundleName));
                var request = AssetBundle.LoadFromFileAsync(string.Format("{0}/{1}", fullBundlePath, cmd.BundleName));

                while (request.isDone == false)
                    yield return null;

                if (request.assetBundle != null) {
                    cmd.OnComplete(request.assetBundle);
                    yield break;
                }

                Debug.LogWarning(string.Format("StreamingAssets download failed for bundle [{0}], switching to standard download.", cmd.BundleName));
            }

            decorated.Handle(cmd);
        }


        private bool BundleAvailableInStreamingAssets(string bundleName, Hash128 hash)
        {
            // Rules for when a bundle should be retrieved from StreamingAssets
            //  #) There IS a manifest in the StreamingAssets folder
            //  #) We ARE NOT trying to retrieve the remote manifest
            //  #) The file exists in the StreamingAssets folder
            //  #) One of:
            //       - We ARE prioritizing StreamingAssets bundles over remote bundles
            //       - The hash for the bundle in StreamingAssets matches the requested hash

            if (manifest == null) {
                Debug.Log("StreamingAssets manifest is null, using standard download.");
                return false;
            }

            if (bundleName == remoteManifestName) {
                Debug.Log("Attempting to download manifest file, using standard download.");
                return false;
            }

            if (manifest.GetAssetBundleHash(bundleName) != hash && currentStrategy != AssetBundleManager.PrioritizationStrategy.PrioritizeStreamingAssets) {
                Debug.LogFormat("Hash for [{0}] does not match the one in StreamingAssets, using standard download.", bundleName);
                return false;
            }

            return true;
        }
    }
}