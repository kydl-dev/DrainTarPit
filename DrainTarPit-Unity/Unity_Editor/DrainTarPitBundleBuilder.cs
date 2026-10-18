using System.IO;
using UnityEditor;
using UnityEngine;

public static class DrainTarPitBundleBuilder
{
    [MenuItem("DrainTarPit/Build AssetBundle")]
    public static void Build()
    {
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AssetBundles");
        Directory.CreateDirectory(outDir);
        var m = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("[DrainTarPit] Bundles built to " + outDir + " : " + string.Join(", ", m == null ? new string[0] : m.GetAllAssetBundles()));
    }
}
