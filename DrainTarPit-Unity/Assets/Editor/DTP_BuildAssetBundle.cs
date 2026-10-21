using UnityEditor;
using UnityEngine;

/// Menu: DTP / Build AssetBundle. Builds 'draintarpit' (prefabs, materials+textures, player clips) and, T-069, 'waterandtarpools' (the pool water
/// shader + style materials of the WaterAndTarPools mod) to Deliverables/AssetBundles and copies each into its mod's Assets folder (embedded in the DLL).
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

        // each mod's csproj embeds <Mod>/Assets/<bundle> into its DLL: keep those copies identical to the real bundles in DrainTarPit-Mod/build
        CopyTo(outDir2, "draintarpit", System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "../DrainTarPit-Mod/Assets")));
        CopyTo(outDir2, "waterandtarpools", "E:/Projects/Valheim/WaterAndTarPools/WaterAndTarPools-Mod/Assets");
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
