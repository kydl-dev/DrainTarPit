using System.IO;
using UnityEditor;
using UnityEngine;

public static class DrainTarPitBundleBuilder
{
    // Menu button removed on user's request (2026-10-17): the only bundle builder is DTP/Build AssetBundle (DTP_BuildAssetBundle.cs, compressed).
    // This uncompressed builder is no longer reachable from the menu; the file can be deleted in the Unity Project window.
    public static void Build()
    {
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AssetBundles");
        Directory.CreateDirectory(outDir);
        var m = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("[DrainTarPit] Bundles built to " + outDir + " : " + string.Join(", ", m == null ? new string[0] : m.GetAllAssetBundles()));
    }
}
