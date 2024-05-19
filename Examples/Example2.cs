using System.Collections;
using UnityEngine;

namespace AssetBundles
{
    public class Example2 : MonoBehaviour
    {
        private IEnumerator Start()
        {
            AssetBundleManager abm = new AssetBundleManager();
            abm.SetBaseUri("https://www.example.com/bundles");
            var initializeAsync = abm.InitializeAsCoroutine();
            yield return initializeAsync;

            if (initializeAsync.Success) {
                AssetBundleRoutine bundle = abm.GetBundleAsCoroutine("BundleNameHere");
                yield return bundle;

                if (bundle.AssetBundle != null) {
                    // Do something with the bundle
                    abm.UnloadBundle(bundle.AssetBundle);
                }
            } else {
                Debug.LogError("Error initializing ABM.");
            }


            abm.Dispose();
        }
    }
}