using UnityEditor;
using UnityEngine;

/// Menu: DTP / Build AssetBundle. Builds 'draintarpit' (prefabs, materials+textures, player clips) to Deliverables/AssetBundles and copies it to
/// DrainTarPit-Mod/Assets (embedded in the DLL). T-071: the water assets are in WaterAndTarPools' own Unity project (WaterAndTarPools-Unity).
public static class DTP_BuildAssetBundle
{
    [MenuItem("DTP/Build AssetBundle")]
    public static void Build()
    {
        string root = System.IO.Path.GetDirectoryName(Application.dataPath);
        string outDir = System.IO.Path.Combine(root, "Deliverables/AssetBundles");
        string outDir2 = "E:/Projects/Valheim/DrainTarPit/DrainTarPit-Mod/build";
        System.IO.Directory.CreateDirectory(outDir);
        System.IO.Directory.CreateDirectory(outDir2);
        BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        BuildPipeline.BuildAssetBundles(outDir2, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        Debug.Log("[DTP] AssetBundle built: " + outDir);
        Debug.Log("[DTP] AssetBundle built2: " + outDir2);

        // DrainTarPit.csproj embeds DrainTarPit-Mod/Assets/draintarpit into the DLL: keep that copy identical to the real bundle in DrainTarPit-Mod/build
        CopyTo(outDir2, "draintarpit", System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "../DrainTarPit-Mod/Assets")));
    }

    static void CopyTo(string builtDir, string bundle, string modAssets)
    {
        string built = System.IO.Path.Combine(builtDir, bundle);
        if (!System.IO.File.Exists(built)) { Debug.LogWarning("[DTP] Bundle '" + bundle + "' not found in " + builtDir + " - nothing copied (is anything assigned to it?)."); return; }
        System.IO.Directory.CreateDirectory(modAssets);
        System.IO.File.Copy(built, System.IO.Path.Combine(modAssets, bundle), true);
        Debug.Log("[DTP] AssetBundle '" + bundle + "' copied to " + modAssets);
    }
}
