using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AssetBundles
{
    /// <summary>
    ///     Simple AssetBundle management
    /// </summary>
    public class AssetBundleManager : IDisposable
    {
        public enum DownloadSettings
        {
            UseCacheIfAvailable,
            DoNotUseCache
        }

        public enum PrioritizationStrategy
        {
            PrioritizeRemote,
            PrioritizeStreamingAssets
        }

        private const string MANIFEST_DOWNLOAD_IN_PROGRESS_KEY = "__manifest__";

        private string baseUri;
        private PrioritizationStrategy defaultPrioritizationStrategy;
        private ICommandHandler<AssetBundleDownloadCommand> handler;
        private AssetBundleManifest manifest;
        private IDictionary<string, AssetBundleContainer> activeBundles = new Dictionary<string, AssetBundleContainer>();
        private IDictionary<string, DownloadInProgressContainer> downloadsInProgress = new Dictionary<string, DownloadInProgressContainer>();

        /// <summary>
        ///     Sets the uri that will be used as the base for all AssetBundle calls
        /// </summary>
        public AssetBundleManager SetBaseUri(string uri)
        {
            if (uri == baseUri) return this;

            if (string.IsNullOrEmpty(baseUri)) {
                Debug.LogFormat("Setting base uri to [{0}].", uri);
            } else {
                Debug.LogWarningFormat("Overriding base uri from [{0}] to [{1}].", baseUri, uri);
            }

            var builder = new StringBuilder(uri);

            if (!uri.EndsWith("/")) {
                builder.Append("/");
            }

            builder.Append(Utility.GetPlatformName()).Append("/");
            baseUri = builder.ToString();
            return this;
        }

        /// <summary>
        ///     Changes the strategy used to determine what should happen when an asset bundle exists in both the StreamingAssets
        ///     folder and the remote server.  The default is to prioritize the remote asset over the StreamingAssets folder
        /// </summary>
        public AssetBundleManager SetPrioritizationStrategy(PrioritizationStrategy strategy)
        {
            defaultPrioritizationStrategy = strategy;
            return this;
        }

        /// <summary>
        ///     Sets the uri that will be used as the base for ass AssetBundle calls to the one created by the AssetBundleBrowser
        ///     when bundles are built.
        ///     Used for easier testing in the editor
        /// </summary>
        public AssetBundleManager UseSimulatedUri()
        {
            SetBaseUri(string.Format("file://{0}/../AssetBundles/", Application.dataPath));
            return this;
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        /// </summary>
        /// <param name="onComplete">Called when initialization is complete.</param>
        public void Initialize(Action onComplete)
        {
            if (string.IsNullOrEmpty(baseUri)) {
                Debug.LogError("You need to set the base uri before you can initialize.");
                return;
            }

            GetManifest(Utility.GetPlatformName(), onComplete);
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        /// </summary>
        /// <returns>An IEnumerator that can be yielded to until the system is ready.</returns>
        public IEnumerator InitializeAsync()
        {
            if (string.IsNullOrEmpty(baseUri)) {
                Debug.LogError("You need to set the base uri before you can initialize.");
                return null;
            }

            // Initializing the manifest doesn't return the manifest bundle directly so we redirect the call with a delegate.
            return new AssetBundleAsync(Utility.GetPlatformName(), (bundleName, onComplete) => GetManifest(bundleName, () => onComplete(null)));
        }

        private void GetManifest(string bundleName, Action onComplete)
        {
            DownloadInProgressContainer inProgress;
            if (downloadsInProgress.TryGetValue(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY, out inProgress)) {
                inProgress.References++;
                inProgress.OnComplete += _ => onComplete();
                return;
            }

            downloadsInProgress.Add(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY, new DownloadInProgressContainer(_ => onComplete()));

            handler = new AssetBundleDownloader(baseUri);

            if (Application.isEditor == false) {
                handler = new StreamingAssetsBundleDownloadDecorator(handler, defaultPrioritizationStrategy);
            }

            handler.Handle(new AssetBundleDownloadCommand {
                BundleName = bundleName,
                OnComplete = OnInitializationComplete
            });
        }

        private void OnInitializationComplete(AssetBundle manifestBundle)
        {
            var inProgress = downloadsInProgress[MANIFEST_DOWNLOAD_IN_PROGRESS_KEY];
            downloadsInProgress.Remove(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY);

            if (manifestBundle == null) {
                Debug.LogError("AssetBundleManifest not found.");

                var streamingAssetsDecorator = handler as StreamingAssetsBundleDownloadDecorator;
                if (streamingAssetsDecorator != null) {
                    manifest = streamingAssetsDecorator.GetManifest();

                    if (manifest != null) {
                        Debug.LogWarning("Falling back to streaming assets for bundle information.");
                    }
                }
            } else {
                manifest = manifestBundle.LoadAsset<AssetBundleManifest>("assetbundlemanifest");
                manifestBundle.Unload(false);
            }

            inProgress.OnComplete(null);
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        ///     <param name="bundleName">Name of the bundle to download.</param>
        ///     <param name="onComplete">Action to perform when the bundle has been successfully downloaded.</param>
        /// </summary>
        public void GetBundle(string bundleName, Action<AssetBundle> onComplete)
        {
            GetBundle(bundleName, onComplete, DownloadSettings.UseCacheIfAvailable);
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        ///     <param name="bundleName">Name of the bundle to download.</param>
        ///     <param name="onComplete">Action to perform when the bundle has been successfully downloaded.</param>
        ///     <param name="downloadSettings">
        ///         Tell the function to use a previously downloaded version of the bundle if available.
        ///         Important!  If the bundle is currently "active" (it has not been unloaded) then the active bundle will be used
        ///         regardless of this setting.  If it's important that a new version is downloaded then be sure it isn't active.
        ///     </param>
        /// </summary>
        public void GetBundle(string bundleName, Action<AssetBundle> onComplete, DownloadSettings downloadSettings)
        {
            AssetBundleContainer active;

            if (activeBundles.TryGetValue(bundleName, out active)) {
                active.References++;
                onComplete(active.AssetBundle);
                return;
            }

            DownloadInProgressContainer inProgress;

            if (downloadsInProgress.TryGetValue(bundleName, out inProgress)) {
                inProgress.References++;
                inProgress.OnComplete += onComplete;
                return;
            }

            downloadsInProgress.Add(bundleName, new DownloadInProgressContainer(onComplete));

            var mainBundle = new AssetBundleDownloadCommand {
                BundleName = bundleName,
                Hash = downloadSettings == DownloadSettings.UseCacheIfAvailable ? manifest.GetAssetBundleHash(bundleName) : default(Hash128),
                OnComplete = bundle => OnDownloadComplete(bundleName, bundle)
            };

            var dependencies = manifest.GetDirectDependencies(bundleName);
            var dependenciesToDownload = new List<string>();

            for (int i = 0; i < dependencies.Length; i++) {
                if (activeBundles.TryGetValue(dependencies[i], out active)) {
                    active.References++;
                } else {
                    dependenciesToDownload.Add(dependencies[i]);
                }
            }

            if (dependenciesToDownload.Count > 0) {
                var dependencyCount = dependenciesToDownload.Count;
                Action<AssetBundle> onDependenciesComplete = dependency => {
                    if (--dependencyCount == 0)
                        handler.Handle(mainBundle);
                };

                for (int i = 0; i < dependenciesToDownload.Count; i++) {
                    var dependencyName = dependenciesToDownload[i];
                    GetBundle(dependencyName, onDependenciesComplete);
                }
            } else {
                handler.Handle(mainBundle);
            }
        }

        /// <summary>
        ///     Asynchronously downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName"></param>
        /// <returns></returns>
        public AssetBundleAsync GetBundleAsync(string bundleName)
        {
            return new AssetBundleAsync(bundleName, GetBundle);
        }

        /// <summary>
        ///     Cleans up all downloaded bundles
        /// </summary>
        public void Dispose()
        {
            foreach (var cache in activeBundles.Values) {
                cache.AssetBundle.Unload(true);
            }

            activeBundles.Clear();
        }

        /// <summary>
        ///     Unloads an AssetBundle.  Objects that were loaded from this bundle will need to be manually destroyed.
        /// </summary>
        /// <param name="bundle">Bundle to unload.</param>
        public void UnloadBundle(AssetBundle bundle)
        {
            UnloadBundle(bundle.name, false, false);
        }

        /// <summary>
        ///     Unloads an AssetBundle.
        /// </summary>
        /// <param name="bundle">Bundle to unload.</param>
        /// <param name="unloadAllLoadedObjects">
        ///     When true, all objects that were loaded from this bundle will be destroyed as
        ///     well. If there are game objects in your scene referencing those assets, the references to them will become missing.
        /// </param>
        public void UnloadBundle(AssetBundle bundle, bool unloadAllLoadedObjects)
        {
            UnloadBundle(bundle.name, unloadAllLoadedObjects, false);
        }

        /// <summary>
        ///     Unloads an AssetBundle.
        /// </summary>
        /// <param name="bundleName">Bundle to unload.</param>
        /// <param name="unloadAllLoadedObjects">
        ///     When true, all objects that were loaded from this bundle will be destroyed as
        ///     well. If there are game objects in your scene referencing those assets, the references to them will become missing.
        /// </param>
        /// <param name="force">Unload the bundle even if we believe there are other dependencies on it.</param>
        public void UnloadBundle(string bundleName, bool unloadAllLoadedObjects, bool force)
        {
            AssetBundleContainer cache;

            if (!activeBundles.TryGetValue(bundleName, out cache)) return;

            if (force || --cache.References <= 0) {
                cache.AssetBundle.Unload(unloadAllLoadedObjects);
                activeBundles.Remove(bundleName);

                for (int i = 0; i < cache.Dependencies.Length; i++) {
                    UnloadBundle(cache.Dependencies[i], unloadAllLoadedObjects, force);
                }
            }
        }

        /// <summary>
        ///     Caches the downloaded bundle and pushes it to the onComplete callback.
        /// </summary>
        private void OnDownloadComplete(string bundleName, AssetBundle bundle)
        {
            var inProgress = downloadsInProgress[bundleName];
            downloadsInProgress.Remove(bundleName);

            activeBundles.Add(bundleName, new AssetBundleContainer {
                AssetBundle = bundle,
                References = inProgress.References,
                Dependencies = manifest.GetDirectDependencies(bundleName)
            });

            inProgress.OnComplete(bundle);
        }

        internal class AssetBundleContainer
        {
            public AssetBundle AssetBundle;
            public int References = 1;
            public string[] Dependencies;
        }

        internal class DownloadInProgressContainer
        {
            public int References;
            public Action<AssetBundle> OnComplete;

            public DownloadInProgressContainer(Action<AssetBundle> onComplete)
            {
                References = 1;
                OnComplete = onComplete;
            }
        }
    }

    /// <summary>
    ///     An asynchronous wrapper for the AssetBundleManager downloading system
    /// </summary>
    public class AssetBundleAsync : IEnumerator
    {
        public AssetBundle AssetBundle;

        public bool IsDone { get; private set; }

        public AssetBundleAsync(string bundleName, Action<string, Action<AssetBundle>> callToAction)
        {
            IsDone = false;
            callToAction(bundleName, OnAssetBundleComplete);
        }

        private void OnAssetBundleComplete(AssetBundle bundle)
        {
            AssetBundle = bundle;
            IsDone = true;
        }

        public bool MoveNext()
        {
            return !IsDone;
        }

        public void Reset()
        { }

        public object Current {
            get { return null; }
        }
    }
}