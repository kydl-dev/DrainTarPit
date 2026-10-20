using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (user: "a 1 leg sign post which could be built on top of the pool ... a new sign model, not the one used in game, although it can
    /// look the same way"): the piece DTP_PoolSign = the game's sign board (cloned, its Sign component replaced by PoolSign) lifted onto one
    /// wooden post (the mesh of the game's wood_pole). Made at runtime from the game's own prefabs, so it looks like the game's sign.
    /// </summary>
    internal static class PoolSignPiece
    {
        internal const string PrefabName = "DTP_PoolSign";
        const float PostHeight = 1.4f;            // ground to the bottom of the board
        const float PostWidth = 0.11f;

        internal static void Register()
        {
            try
            {
                var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "sign");
                if (prefab == null) { ModLog.Error("PoolSignPiece: the game's 'sign' prefab was not found - no pool sign"); return; }
                var vanilla = prefab.GetComponent<Sign>();
                if (vanilla != null) Object.DestroyImmediate(vanilla);
                prefab.AddComponent<PoolSign>();

                float boardHalf = BoardHalfHeight(prefab);
                float lift = PostHeight + boardHalf;
                foreach (Transform child in prefab.transform) child.localPosition += Vector3.up * lift;
                AddPost(prefab, lift);

                var piece = prefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_groundOnly = false; piece.m_notOnFloor = false; piece.m_onlyInTeleportArea = false; piece.m_noInWater = false;
                    piece.m_canRotate = true;
                }
                var cfg = new PieceConfig
                {
                    Name = Loc.Token("dtp_piece_sign"),
                    Description = Loc.Token("dtp_piece_sign_desc"),
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Misc,
                    Requirements = new[] { new RequirementConfig("Wood", 3, 0, true), new RequirementConfig("Coal", 1, 0, true) },
                };
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, true, cfg));
                ModLog.Info("Registered piece " + PrefabName + " (board half height " + boardHalf.ToString("0.00") + " m, lifted " + lift.ToString("0.00") + " m, post " + PostHeight + " m)");
            }
            catch (System.Exception e) { ModLog.Error("PoolSignPiece: registering the pool sign failed: " + e); }
        }

        /// Half the board's height, measured from the meshes in the prefab's own space (renderer bounds are empty on a prefab that was never
        /// spawned). The game's sign is 0.55 m high with its pivot in the middle; 0.275 when nothing is found.
        static float BoardHalfHeight(GameObject prefab)
        {
            Bounds b;
            int n = MeshBounds(prefab.transform, prefab.transform, out b);
            if (n == 0 || b.extents.y < 0.05f || b.extents.y > 0.6f)
            { ModLog.VerboseWarn("PoolSignPiece: board size not usable (" + n + " mesh(es), " + b.size.ToString("0.00") + " m), 0.55 m assumed"); return 0.275f; }
            ModLog.Verbose("PoolSignPiece: board " + b.size.ToString("0.00") + " m from " + n + " mesh(es), centre " + b.center.ToString("0.00"));
            return b.extents.y;
        }

        /// Bounds of every mesh under `under`, in the space of `space`. Returns the number of meshes measured.
        static int MeshBounds(Transform under, Transform space, out Bounds b)
        {
            b = new Bounds();
            int n = 0;
            foreach (var mf in under.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds one = Corners(mf.sharedMesh.bounds, space.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                if (n == 0) b = one; else b.Encapsulate(one);
                n++;
            }
            return n;
        }

        static Bounds Corners(Bounds mb, Matrix4x4 m)
        {
            var r = new Bounds(m.MultiplyPoint3x4(mb.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                r.Encapsulate(m.MultiplyPoint3x4(p));
            }
            return r;
        }

        /// The post: the tallest mesh of the game's wood_pole (its log; the snow cap and the broken version are skipped), copied with its own
        /// rotation / scale and then fitted to PostWidth x `top`, standing on the ground under the board. Box collider for hits / hover.
        static void AddPost(GameObject prefab, float top)
        {
            var post = new GameObject("post");
            post.transform.SetParent(prefab.transform, false);
            post.layer = prefab.layer;
            var fit = new GameObject("post_fit");
            fit.transform.SetParent(post.transform, false);
            fit.layer = prefab.layer;

            var pole = PrefabManager.Instance.GetPrefab("wood_pole");
            MeshFilter best = null; float bestH = 0f;
            if (pole != null)
                foreach (var mf in pole.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.name.ToLowerInvariant().Contains("worn") || mf.GetComponent<MeshRenderer>() == null) continue;
                    float h = Corners(mf.sharedMesh.bounds, pole.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix).size.y;
                    if (h > bestH) { bestH = h; best = mf; }
                }
            if (best != null)
            {
                var copy = new GameObject("post_mesh");
                copy.layer = prefab.layer;
                copy.transform.SetParent(fit.transform, false);
                var rel = pole.transform.worldToLocalMatrix * best.transform.localToWorldMatrix;
                copy.transform.localPosition = rel.GetColumn(3);
                copy.transform.localRotation = Quaternion.Inverse(pole.transform.rotation) * best.transform.rotation;
                copy.transform.localScale = best.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = best.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = best.GetComponent<MeshRenderer>().sharedMaterials;
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.name = "post_mesh"; cube.layer = prefab.layer;
                cube.transform.SetParent(fit.transform, false);
                var boardR = prefab.GetComponentInChildren<MeshRenderer>(true);
                if (boardR != null) cube.GetComponent<MeshRenderer>().sharedMaterials = boardR.sharedMaterials;
                ModLog.VerboseWarn("PoolSignPiece: wood_pole mesh not found" + (pole == null ? " (no wood_pole prefab)" : "") + ", the post is a box with the board's material");
            }

            Bounds raw;
            MeshBounds(fit.transform, fit.transform, out raw);
            var scale = new Vector3(PostWidth / Mathf.Max(0.01f, raw.size.x), top / Mathf.Max(0.01f, raw.size.y), PostWidth / Mathf.Max(0.01f, raw.size.z));
            fit.transform.localScale = scale;
            fit.transform.localPosition = new Vector3(-raw.center.x * scale.x, top * 0.5f - raw.center.y * scale.y, -raw.center.z * scale.z);
            ModLog.Verbose("PoolSignPiece: post from " + (best != null ? "wood_pole mesh '" + best.sharedMesh.name + "'" : "a box") + ", raw " + raw.size.ToString("0.00") + " m, fitted to " + PostWidth + " x " + top.ToString("0.00") + " m");

            var col = post.AddComponent<BoxCollider>();
            col.size = new Vector3(PostWidth, top, PostWidth);
            col.center = new Vector3(0f, top * 0.5f, 0f);
        }
    }
}
