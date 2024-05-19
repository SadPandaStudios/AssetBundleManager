using System;
using UnityEngine;

namespace AssetBundles
{
    public class Example1 : MonoBehaviour
    {
        private AssetBundleManager abm;

        private void Start()
        {
            abm = new AssetBundleManager();
            abm.SetBaseUri("https://www.example.com/bundles");
            abm.Initialize(OnAssetBundleManagerInitialized);
        }

        private void OnAssetBundleManagerInitialized(bool success)
        {
            if (success) {
                abm.GetBundle("BundleNameHere", OnAssetBundleDownloaded, OnProgress);
            } else {
                Debug.LogError("Error initializing ABM.");
            }
        }

        private void OnAssetBundleDownloaded(AssetBundle bundle)
        {
            if (bundle != null) {
                // Do something with the bundle
                abm.UnloadBundle(bundle);
            }

            abm.Dispose();
        }

        private void OnProgress(float progress)
        {
            Debug.Log("Current Progress: " + Math.Round(progress * 100, 2) + "%");
        }
    }
}