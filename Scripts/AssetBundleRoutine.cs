using System;
using System.Collections;
using UnityEngine;

namespace AssetBundles
{
    /// <summary>
    ///     A coroutine wrapper for the AssetBundleManager downloading system
    /// </summary>
    public class AssetBundleRoutine : IEnumerator
    {
        public AssetBundle AssetBundle;

        public bool IsDone { get; private set; }
        public bool Failed { get; private set; }

        public AssetBundleRoutine(string bundleName, Action<float> onProgress, Action<string, Action<AssetBundle>, Action<float>> callToAction)
        {
            IsDone = false;
            callToAction(bundleName, OnAssetBundleComplete, onProgress);
        }

        public AssetBundleRoutine()
        {
            IsDone = true;
            Failed = true;
        }

        private void OnAssetBundleComplete(AssetBundle bundle)
        {
            AssetBundle = bundle;
            Failed = bundle == null;
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

    /// <summary>
    ///     A coroutine wrapper for the AssetBundleManager manifest downloading system
    /// </summary>
    public class AssetBundleManifestRoutine : IEnumerator
    {
        public bool Success { get; private set; }
        public bool IsDone { get; private set; }

        public AssetBundleManifestRoutine(string bundleName, bool getFreshManifest, Action<string, bool, Action<AssetBundle>> callToAction)
        {
            IsDone = false;
            callToAction(bundleName, getFreshManifest, OnAssetBundleManifestComplete);
        }

        private void OnAssetBundleManifestComplete(AssetBundle bundle)
        {
            Success = bundle != null;
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