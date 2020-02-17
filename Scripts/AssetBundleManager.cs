#if NET_4_6 || NET_STANDARD_2_0
#define AWAIT_SUPPORTED
#endif

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if AWAIT_SUPPORTED
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

#endif

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
            PrioritizeStreamingAssets,
        }

        public enum PrimaryManifestType
        {
            None,
            Remote,
            RemoteCached,
            StreamingAssets,
        }

        public bool Initialized { get; private set; }
        public AssetBundleManifest Manifest { get; private set; }
        public PrimaryManifestType PrimaryManifest { get; private set; }

        private const string MANIFEST_DOWNLOAD_IN_PROGRESS_KEY = "__manifest__";
        private const string MANIFEST_PLAYERPREFS_KEY = "__abm_manifest_version__";

        private string[] baseUri;
        private bool useHash;
        internal static bool debugLoggingEnabled = true;
        private string platformName;
        private PrioritizationStrategy defaultPrioritizationStrategy;
        private ICommandHandler<AssetBundleDownloadCommand> handler;
        private IDictionary<string, AssetBundleContainer> activeBundles = new Dictionary<string, AssetBundleContainer>(StringComparer.OrdinalIgnoreCase);
        private IDictionary<string, DownloadInProgressContainer> downloadsInProgress = new Dictionary<string, DownloadInProgressContainer>(StringComparer.OrdinalIgnoreCase);
        private IDictionary<string, string> unhashedToHashedBundleNameMap = new Dictionary<string, string>(10, StringComparer.OrdinalIgnoreCase);
        private IDictionary<string, string> hashedToUnhashedBundleNameMap = new Dictionary<string, string>(10, StringComparer.OrdinalIgnoreCase);

        public AssetBundleManager()
        {
            platformName = Utility.GetPlatformName();
        }

        /// <summary>
        ///     Sets the base uri used for AssetBundle calls.
        /// </summary>
        public AssetBundleManager SetBaseUri(string uri)
        {
            if (uri == null) {
                uri = "";
            }

            return SetBaseUri(new[] { uri });
        }

        /// <summary>
        ///     Sets the base uri used for AssetBundle calls.
        /// </summary>
        /// <param name="uris">List of uris to use.  In order of priority (highest to lowest).</param>
        public AssetBundleManager SetBaseUri(string[] uris)
        {
            if (baseUri == null || baseUri.Length == 0) {
                if (debugLoggingEnabled) Debug.LogFormat("Setting base uri to [{0}].", string.Join(",", uris));
            } else {
                Debug.LogWarningFormat("Overriding base uri from [{0}] to [{1}].", string.Join(",", baseUri), string.Join(",", uris));
            }

            baseUri = new string[uris.Length];

            for (int i = 0; i < uris.Length; i++) {
                var builder = new StringBuilder(uris[i]);

                if (!uris[i].EndsWith("/")) {
                    builder.Append("/");
                }

                builder.Append(platformName).Append("/");
                baseUri[i] = builder.ToString();
            }

            return this;
        }

        /// <summary>
        ///     Sets the base uri used for AssetBundle calls to the one created by the AssetBundleBrowser when the bundles are
        ///     built.
        ///     Used for easier testing in the editor
        /// </summary>
        public AssetBundleManager UseSimulatedUri()
        {
            SetBaseUri(new[] { "file://" + Application.dataPath + "/../AssetBundles/" });
            return this;
        }

        /// <summary>
        ///     Sets the base uri used for AssetBundle calls to the StreamingAssets folder.
        /// </summary>
        public AssetBundleManager UseStreamingAssetsFolder()
        {
#if UNITY_ANDROID
            var url = Application.streamingAssetsPath;
#else
            var url = "file:///" + Application.streamingAssetsPath;
#endif
            SetBaseUri(new[] { url });
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
        ///     Tell ABM to append the hash name to bundle names before downloading.
        ///     If you are using AssetBundleBrowser then you need to enable "Append Hash" in the advanced settings for this to
        ///     work.
        /// </summary>
        public AssetBundleManager AppendHashToBundleNames(bool appendHash = true)
        {
            if (appendHash && Initialized) {
                GenerateNameHashMaps(Manifest);
            }

            useHash = appendHash;
            return this;
        }

        /// <summary>
        ///     Tell ABM to lower-case the platform name used for various paths and filenames.
        /// </summary>
        public AssetBundleManager UseLowerCasePlatformName(bool useLowerCase)
        {
            if (baseUri != null) {
                Debug.LogWarning("UseLowerCasePlatformName: Base URI previously set. The platform is used in the uris, you will need to call SetBaseUri again.");
                baseUri = null;
            }

            platformName = useLowerCase ? platformName.ToLower() : Utility.GetPlatformName();
            return this;
        }

        public AssetBundleManager DisableDebugLogging(bool disable = true)
        {
            debugLoggingEnabled = !disable;
            return this;
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        ///     Uses the platform name as the manifest name.  This is the default behaviour when
        ///     using Unity's AssetBundleBrowser to create your bundles.
        /// </summary>
        /// <param name="onComplete">Called when initialization is complete.</param>
        public void Initialize(Action<bool> onComplete)
        {
            Initialize(platformName, true, onComplete);
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        /// </summary>
        /// <param name="manifestName">The name of the manifest file to download.</param>
        /// <param name="getFreshManifest">
        ///     Always try to download a new manifest even if one has already been cached.
        /// </param>
        /// <param name="onComplete">Called when initialization is complete.</param>
        public void Initialize(string manifestName, bool getFreshManifest, Action<bool> onComplete)
        {
            if (baseUri == null || baseUri.Length == 0) {
                Debug.LogError("You need to set the base uri before you can initialize.");
                return;
            }

            GetManifest(manifestName, getFreshManifest, bundle => onComplete(bundle != null));
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        ///     Uses the platform name as the manifest name.  This is the default behaviour when
        ///     using Unity's AssetBundleBrowser to create your bundles.
        /// </summary>
        /// <returns>An IEnumerator that can be yielded to until the system is ready.</returns>
        public AssetBundleManifestAsync InitializeAsync()
        {
            return InitializeAsync(platformName, true);
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        /// </summary>
        /// <param name="manifestName">The name of the manifest file to download.</param>
        /// <param name="getFreshManifest">
        ///     Always try to download a new manifest even if one has already been cached.
        /// </param>
        /// <returns>An IEnumerator that can be yielded to until the system is ready.</returns>
        public AssetBundleManifestAsync InitializeAsync(string manifestName, bool getFreshManifest)
        {
            if (baseUri == null || baseUri.Length == 0) {
                Debug.LogError("You need to set the base uri before you can initialize.");
                return null;
            }

            // Wrap the GetManifest with an async operation.
            return new AssetBundleManifestAsync(manifestName, getFreshManifest, GetManifest);
        }

        private void GetManifest(string bundleName, bool getFreshManifest, Action<AssetBundle> onComplete)
        {
            DownloadInProgressContainer inProgress;
            if (downloadsInProgress.TryGetValue(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY, out inProgress)) {
                inProgress.References++;
                inProgress.OnComplete += onComplete;
                return;
            }

            downloadsInProgress.Add(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY, new DownloadInProgressContainer(onComplete));
            PrimaryManifest = PrimaryManifestType.Remote;

            uint manifestVersion = 1;

            if (getFreshManifest) {
                // Find the first cached version and then get the "next" one.
                manifestVersion = (uint)PlayerPrefs.GetInt(MANIFEST_PLAYERPREFS_KEY, 0) + 1;

                // The PlayerPrefs value may have been wiped so we have to calculate what the next uncached manifest version is.
                while (Caching.IsVersionCached(bundleName, new Hash128(0, 0, 0, manifestVersion))) {
                    manifestVersion++;
                }
            }

            GetManifestInternal(bundleName, manifestVersion, 0);
        }

        private void GetManifestInternal(string manifestName, uint version, int uriIndex)
        {
            handler = new AssetBundleDownloader(baseUri[uriIndex]);

            if (Application.isEditor == false) {
                handler = new StreamingAssetsBundleDownloadDecorator(manifestName, platformName, handler, defaultPrioritizationStrategy);
            }

            handler.Handle(new AssetBundleDownloadCommand {
                BundleName = manifestName,
                Version = version,
                OnComplete = manifest => {
                    var maxIndex = baseUri.Length - 1;
                    if (manifest == null && uriIndex < maxIndex && version > 1) {
                        if (debugLoggingEnabled) Debug.LogFormat("Unable to download manifest from [{0}], attempting [{1}]", baseUri[uriIndex], baseUri[uriIndex + 1]);
                        GetManifestInternal(manifestName, version, uriIndex + 1);
                    } else if (manifest == null && uriIndex >= maxIndex && version > 1 && PrimaryManifest != PrimaryManifestType.RemoteCached) {
                        PrimaryManifest = PrimaryManifestType.RemoteCached;
                        if (debugLoggingEnabled) Debug.LogFormat("Unable to download manifest, attempting to use one previously downloaded (version [{0}]).", version);
                        GetManifestInternal(manifestName, version - 1, uriIndex);
                    } else {
                        OnInitializationComplete(manifest, manifestName, version);
                    }
                }
            });
        }

        private void OnInitializationComplete(AssetBundle manifestBundle, string bundleName, uint version)
        {
            if (manifestBundle == null) {
                Debug.LogError("AssetBundleManifest not found.");

                var streamingAssetsDecorator = handler as StreamingAssetsBundleDownloadDecorator;
                if (streamingAssetsDecorator != null) {
                    PrimaryManifest = PrimaryManifestType.StreamingAssets;
                    Manifest = streamingAssetsDecorator.GetManifest();

                    if (Manifest != null) {
                        Debug.LogWarning("Falling back to streaming assets for bundle information.");
                    }
                }
            } else {
                Manifest = manifestBundle.LoadAsset<AssetBundleManifest>("assetbundlemanifest");
                PlayerPrefs.SetInt(MANIFEST_PLAYERPREFS_KEY, (int)version);

#if UNITY_2017_1_OR_NEWER
                Caching.ClearOtherCachedVersions(bundleName, new Hash128(0, 0, 0, version));
#endif
            }

            if (Manifest == null) {
                PrimaryManifest = PrimaryManifestType.None;
            } else {
                Initialized = true;

                if (useHash) {
                    GenerateNameHashMaps(Manifest);
                }
            }

            var inProgress = downloadsInProgress[MANIFEST_DOWNLOAD_IN_PROGRESS_KEY];
            downloadsInProgress.Remove(MANIFEST_DOWNLOAD_IN_PROGRESS_KEY);
            inProgress.OnComplete(manifestBundle);

            // Need to do this after OnComplete, otherwise the bundle will always be null
            if (manifestBundle != null) {
                manifestBundle.Unload(false);
            }
        }

        private void GenerateNameHashMaps(AssetBundleManifest manifest)
        {
            unhashedToHashedBundleNameMap.Clear();
            hashedToUnhashedBundleNameMap.Clear();
            var allBundles = manifest.GetAllAssetBundles();

            for (int i = 0; i < allBundles.Length; i++) {
                var bundle = allBundles[i];
                var indexOfHashSplit = bundle.LastIndexOf('_');
                if (indexOfHashSplit < 0) continue;
                var splitName = bundle.Substring(0, indexOfHashSplit);
                unhashedToHashedBundleNameMap[splitName] = bundle;
                hashedToUnhashedBundleNameMap[bundle] = splitName;
            }
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName">Name of the bundle to download.</param>
        /// <param name="onComplete">Action to perform when the bundle has been successfully downloaded.</param>
        public void GetBundle(string bundleName, Action<AssetBundle> onComplete)
        {
            if (Initialized == false) {
                Debug.LogError("AssetBundleManager must be initialized before you can get a bundle.");
                onComplete(null);
                return;
            }

            GetBundle(bundleName, onComplete, DownloadSettings.UseCacheIfAvailable);
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName">Name of the bundle to download.</param>
        /// <param name="onComplete">Action to perform when the bundle has been successfully downloaded.</param>
        /// <param name="downloadSettings">
        ///     Tell the function to use a previously downloaded version of the bundle if available.
        ///     Important!  If the bundle is currently "active" (it has not been unloaded) then the active bundle will be used
        ///     regardless of this setting.  If it's important that a new version is downloaded then be sure it isn't active.
        /// </param>
        public void GetBundle(string bundleName, Action<AssetBundle> onComplete, DownloadSettings downloadSettings)
        {
            if (Initialized == false) {
                Debug.LogError("AssetBundleManager must be initialized before you can get a bundle.");
                onComplete(null);
                return;
            }

            if (useHash) bundleName = GetHashedBundleName(bundleName);

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
                Hash = downloadSettings == DownloadSettings.UseCacheIfAvailable ? Manifest.GetAssetBundleHash(bundleName) : default(Hash128),
                OnComplete = bundle => OnDownloadComplete(bundleName, bundle)
            };

            var dependencies = Manifest.GetDirectDependencies(bundleName);
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
                    if (useHash) dependencyName = GetUnhashedBundleName(dependencyName);
                    GetBundle(dependencyName, onDependenciesComplete);
                }
            } else {
                handler.Handle(mainBundle);
            }
        }

#if AWAIT_SUPPORTED
        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        ///     Uses the platform name as the manifest name.  This is the default behaviour when
        ///     using Unity's AssetBundleBrowser to create your bundles.
        /// </summary>
        public async Task<bool> Initialize()
        {
            return await Initialize(platformName, true);
        }

        /// <summary>
        ///     Downloads the AssetBundle manifest and prepares the system for bundle management.
        /// </summary>
        /// <param name="manifestName">Name of the manifest to download. </param>
        /// <param name="getFreshManifest">
        ///     Always try to download a new manifest even if one has already been cached.
        /// </param>
        public async Task<bool> Initialize(string manifestName, bool getFreshManifest)
        {
            var completionSource = new TaskCompletionSource<bool>();
            var onComplete = new Action<bool>(b => completionSource.SetResult(b));
            Initialize(manifestName, getFreshManifest, onComplete);
            return await completionSource.Task;
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName">Name of the bundle to download.</param>
        public async Task<AssetBundle> GetBundle(string bundleName)
        {
            var completionSource = new TaskCompletionSource<AssetBundle>();
            var onComplete = new Action<AssetBundle>(bundle => completionSource.SetResult(bundle));
            GetBundle(bundleName, onComplete);
            return await completionSource.Task;
        }

        /// <summary>
        ///     Downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName">Name of the bundle to download.</param>
        /// <param name="downloadSettings">
        ///     Tell the function to use a previously downloaded version of the bundle if available.
        ///     Important!  If the bundle is currently "active" (it has not been unloaded) then the active bundle will be used
        ///     regardless of this setting.  If it's important that a new version is downloaded then be sure it isn't active.
        /// </param>
        public async Task<AssetBundle> GetBundle(string bundleName, DownloadSettings downloadSettings)
        {
            var completionSource = new TaskCompletionSource<AssetBundle>();
            var onComplete = new Action<AssetBundle>(bundle => completionSource.SetResult(bundle));
            GetBundle(bundleName, onComplete, downloadSettings);
            return await completionSource.Task;
        }

        /// <summary>
        ///     Downloads a bundle (or uses a cached bundle) and loads a Unity scene contained in an asset bundle asynchronously.
        /// </summary>
        /// <param name="bundleName">Name of the bundle to donwnload.</param>
        /// <param name="levelName">Name of the unity scene to load.</param>
        /// <param name="loadSceneMode">See <see cref="LoadSceneMode">UnityEngine.SceneManagement.LoadSceneMode</see>.</param>
        /// <returns></returns>
        public async Task<AsyncOperation> LoadLevelAsync(string bundleName, string levelName, LoadSceneMode loadSceneMode)
        {
            try {
                await GetBundle(bundleName);
                return SceneManager.LoadSceneAsync(levelName, loadSceneMode);
            } catch {
                Debug.LogError($"Error while loading the scene {levelName} from {bundleName}");
                throw;
            }
        }
#endif

        /// <summary>
        ///     Asynchronously downloads an AssetBundle or returns a cached AssetBundle if it has already been downloaded.
        ///     Remember to call <see cref="UnloadBundle(UnityEngine.AssetBundle,bool)" /> for every bundle you download once you
        ///     are done with it.
        /// </summary>
        /// <param name="bundleName"></param>
        /// <returns></returns>
        public AssetBundleAsync GetBundleAsync(string bundleName)
        {
            if (Initialized == false) {
                Debug.LogError("AssetBundleManager must be initialized before you can get a bundle.");
                return new AssetBundleAsync();
            }

            return new AssetBundleAsync(bundleName, GetBundle);
        }


        /// <summary>
        ///     Returns the bundle name with the bundle hash appended to it.  Needed if you have hash naming enabled via
        ///     <code>AppendHashToBundleNames(true)</code>
        /// </summary>
        public string GetHashedBundleName(string bundleName)
        {
            string hashedBundleName;
            if (unhashedToHashedBundleNameMap.TryGetValue(bundleName, out hashedBundleName)) {
                return hashedBundleName;
            }

            Debug.LogWarningFormat("Unable to find hash for bundle [{0}], this request is likely to fail.", bundleName);
            return bundleName;
        }

        /// <summary>
        ///     Returns the bundle name with the bundle hash removed from it.
        /// </summary>
        public string GetUnhashedBundleName(string bundleName)
        {
            string unhashedBundleName;
            if (hashedToUnhashedBundleNameMap.TryGetValue(bundleName, out unhashedBundleName)) {
                return unhashedBundleName;
            }

            Debug.LogWarningFormat("Unable to find unhashed name for bundle [{0}], this request is likely to fail.", bundleName);
            return bundleName;
        }

        /// <summary>
        ///     Check to see if a specific asset bundle is cached or needs to be downloaded.
        /// </summary>
        public bool IsVersionCached(string bundleName)
        {
            if (Manifest == null) return false;
            if (useHash) bundleName = GetHashedBundleName(bundleName);
            if (string.IsNullOrEmpty(bundleName)) return false;
            return Caching.IsVersionCached(bundleName, Manifest.GetAssetBundleHash(bundleName));
        }

        /// <summary>
        ///     Cleans up all downloaded bundles
        /// </summary>
        public void Dispose()
        {
            foreach (var cache in activeBundles.Values) {
                if (cache.AssetBundle != null) {
                    cache.AssetBundle.Unload(true);
                }
            }

            activeBundles.Clear();
        }

        /// <summary>
        ///     Unloads an AssetBundle.  Objects that were loaded from this bundle will need to be manually destroyed.
        /// </summary>
        /// <param name="bundle">Bundle to unload.</param>
        public void UnloadBundle(AssetBundle bundle)
        {
            if (bundle == null) return;
            UnloadBundleInternal(useHash ? GetHashedBundleName(bundle.name) : bundle.name, false, false);
        }

        /// <summary>
        ///     Unloads an AssetBundle.
        /// </summary>
        /// <param name="bundle">Bundle to unload.</param>
        /// <param name="unloadAllLoadedObjects">
        ///     When true, all objects that were loaded from this bundle will be destroyed as
        ///     well. If there are game objects in your scene referencing those assets, the references to them will
        ///     become missing.
        /// </param>
        public void UnloadBundle(AssetBundle bundle, bool unloadAllLoadedObjects)
        {
            if (bundle == null) return;
            UnloadBundleInternal(useHash ? GetHashedBundleName(bundle.name) : bundle.name, unloadAllLoadedObjects, false);
        }

        /// <summary>
        ///     Unloads an AssetBundle.
        /// </summary>
        /// <param name="bundleName">Bundle to unload.</param>
        /// <param name="unloadAllLoadedObjects">
        ///     When true, all objects that were loaded from this bundle will be destroyed as
        ///     well. If there are game objects in your scene referencing those assets, the references to them will
        ///     become missing.
        /// </param>
        /// <param name="force">Unload the bundle even if ABM believes there are other dependencies on it.</param>
        public void UnloadBundle(string bundleName, bool unloadAllLoadedObjects, bool force)
        {
            if (bundleName == null) return;
            UnloadBundleInternal(useHash ? GetHashedBundleName(bundleName) : bundleName, unloadAllLoadedObjects, force);
        }

        /// <summary>Unloads an AssetBundle and its dependencies if there are no more active references.</summary>
        private void UnloadBundleInternal(string bundleName, bool unloadAllLoadedObjects, bool force)
        {
            AssetBundleContainer cache;

            if (!activeBundles.TryGetValue(bundleName, out cache)) return;

            if (force || --cache.References <= 0) {
                if (cache.AssetBundle != null) {
                    cache.AssetBundle.Unload(unloadAllLoadedObjects);
                }

                activeBundles.Remove(bundleName);

                for (int i = 0; i < cache.Dependencies.Length; i++) {
                    UnloadBundleInternal(cache.Dependencies[i], unloadAllLoadedObjects, force);
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

            try {
                activeBundles.Add(bundleName, new AssetBundleContainer {
                    AssetBundle = bundle,
                    References = inProgress.References,
                    Dependencies = Manifest.GetDirectDependencies(bundleName)
                });
            } catch (ArgumentException) {
                Debug.LogWarning("Attempted to activate a bundle that was already active.  Not sure how this happened, attempting to fail gracefully.");
                activeBundles[bundleName].References++;
            }

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
}