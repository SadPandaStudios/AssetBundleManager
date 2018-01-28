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
            yield return abm.InitializeAsync();

            AssetBundleAsync bundle = abm.GetBundleAsync("BundleNameHere");
            yield return bundle;

            if (bundle.AssetBundle != null) {
                // Do something with the bundle
            }
        }
    }
}