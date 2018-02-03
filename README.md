# Asset Bundle Manager (ABM)
Yet another asset bundle manager for Unity.


## Why
We felt the AssetBundleManager provided by Unity was complicated and out-dated...  So like typical programmers we decided to write our own!  We wanted something with few frills and easier to trace & maintain.


## Requirements
 - Unity 2017.1 or greater.


## Recommendations
This module pairs well with Unity's [Asset Bundle Browser](https://github.com/Unity-Technologies/AssetBundles-Browser) (ABB) tool.


## How

### Initializing
First you need to build your bundles.  By default the ABB puts bundles in `PROJECT\AssetBundles\PLATFORM` and ABM can take advantage of it.

Once the bundles are built you can start accessing them with the manager.

When you are done testing your bundles you need to upload them to a server.  They can go anywhere in the server as long as they are contained in a `PLATFORM` folder.  For example, builds for iOS bundles should be accessable from ```http://www.example.com/AssetBundles/iOS```.  The full list of supported targets can be found in [AssetBundleUtility.cs](https://github.com/SadPandaStudios/AssetBundleManager/blob/master/AssetBundleManager/AssetBundleUtility.cs).


```csharp
var abm = new AssetBundleManager()

if (Application.isEditor)
    abm.UseSimulatedUri();
else
    abm.SetBaseUri("https://www.example.com/bundles");

abm.Initialize(OnAssetBundleManagerInitialized);
```

`UseSimulatedUri()` configures ABM to use ABB's default folder structure to retrieve bundles.  This convenience means you don't have to upload your bundles to a remote server in order to test them, you can use your local files instead.

The `SetBaseUri(...)` function configures ABM to point to a remote server that contains your bundles.

Calling `Initialize(...)` causes ABM to download the manifest file for your bundles.  Once this file is downloaded and processed you are ready to begin downloading bundles.

If you prefer to use a coroutine instead of a callback for initializing:

```csharp
var abm = new AssetBundleManager();
// ...
var initializeAsync = abm.InitializeAsyn();
yield return initializeAsync;
if (initializeAsync.Success) {
    // ...
}
```

Both initialize calls will return a boolean to indicate whether the manifest was downloaded successfully or not.


### Downloading
Just like initializing you can use either callbacks or coroutines to download bundles.

#### Callback
```csharp
public function GetMyBundle() 
{
    abm.GetBundle("MyBundle", OnBundleDownloaded);
}

public function OnBundleDownloaded(AssetBundle bundle)
{
    if (bundle != null) {
        // Do something with the bundle
        abm.UnloadBundle(bundle);
    }
}
```

#### Coroutine
```csharp
/// Coroutine
var bundle = abm.GetBundleAsync(MyBundle);
yield return bundle;

if (bundle.AssetBundle != null) {
    // Do something with bundle.AssetBundle
    abm.UnloadBundle(bundle);
}
```

If ABM is unable to download the bundle it will log an error describing the problem and return a `null` bundle.  Therefore it's important to check whether the bundle is `null` before attempting to use it.

By default bundles are cached using Unity's caching system.  The exception to this is the manifest file, which is never cached and always downloaded fresh on initialization.  You can override this behaviour on non-manifest bundles by including the DownloadSettings parameter on a GetBundle call:

```csharp
var bundle = abm.GetBundle("MyBundle", OnBundleDownloaded, DownloadSettings.DoNotUseCache);
```

Notice that the above examples call `UnloadBundle(...)` after they are done using the bundle.  This is to help ABM manage memory and bundle usage.  If two scripts download the same bundle then that bundle is reused for both scripts and will remain in memory until BOTH of those scripts unload the bundle.  If memory usage is important to you then you must ensure that every script that loads a bundle also unloads the bundle.  If you want to keep the bundle in memory and available at any time then feel free to skip the `UnloadBundle(...)` call.


### StreamingAssets
ABM supports pre-caching your bundles with the use of the StreamingAssets folder in Unity.  Once your bundles are built you can copy the manifest and any number of bundles to the `StreamingAsests\PLATFORM` folder in your project.  For example if you wanted to pre-cache the `SomeBundle` iOS bundles you would have a structure like:

```
PROJECT
  \Assets
    \StreamingAssets
      \iOS
        \iOS
        \iOS.manifest
        \SomeBundle
        \SomeBundle.manifest
```

When you make a `GetBundle(...)` call ABM will check to see if that bundle exists in the StreamingAssets folder first and use it if its hash matches the hash of the remote server.  If the file does not exist OR the hash is different then the remote bundle is used.  You can change this behaviour when initializing ABM by changing the prioritization strategy:

```csharp
abm.SetPrioritizationStrategy(PrioritizationStrategy.PrioritizeStreamingAssets);
```

This will tell ABM to always use the StreamingAssets bundle if it exists. If the bundle doesn't exist in StreamingAssets the remote one will be used.

### Cleanup
There are two patterns you should follow when using ABM.  The first, as mentioned before, is to always unload the bundle when you are finished with it:

```csharp
abm.UnloadBundle(bundle);
```

If no other scripts are using this bundle it will be unloaded from memory.  Likewise, when you are completely done with ABM (maybe because you're switching scenes and don't need the bundles anymore) you can dispose of it:

```csharp
abm.Dispose();
```

This will force ALL bundles (and their objects) to be unloaded.