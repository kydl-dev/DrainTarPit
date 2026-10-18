using UnityEditor;
using UnityEngine;

/// Menu: DTP / Build AssetBundle. Builds 'draintarpit' (prefabs, materials+textures, player clips) to Deliverables/AssetBundles.
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

        // DrainTarPit.csproj embeds Mod/Assets/draintarpit into the DLL: keep that copy identical to the real bundle in Mod/build.
        string modAssets = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "../DrainTarPit-Mod/Assets"));
        string builtBundle = System.IO.Path.Combine(outDir2, "draintarpit");
        if (System.IO.File.Exists(builtBundle))
        {
            System.IO.Directory.CreateDirectory(modAssets);
            System.IO.File.Copy(builtBundle, System.IO.Path.Combine(modAssets, "draintarpit"), true);
            Debug.Log("[DTP] AssetBundle copied to " + modAssets);
        }
        else
        {
            Debug.LogWarning("[DTP] Bundle 'draintarpit' not found in " + outDir2 + " - nothing copied to Mod/Assets (is anything assigned to the asset bundle?).");
        }
    }
}
