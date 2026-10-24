using Kydl.Common;
using Jotunn.Managers;
using UnityEngine;

namespace DrainTarPit.Building
{
    /// Turns raw bundle prefabs into something Valheim accepts as a buildable piece.
    internal static class PieceSetup
    {
        public static void Prepare(GameObject prefab)
        {
            // ZNetView so it persists and can be placed/removed in multiplayer
            var nv = prefab.GetComponent<ZNetView>();
            if (nv == null) nv = prefab.AddComponent<ZNetView>();
            nv.m_persistent = true;

            // Piece component (Jotunn's PieceConfig fills the rest)
            if (prefab.GetComponent<Piece>() == null) prefab.AddComponent<Piece>();
            prefab.GetComponent<Piece>().m_canRotate = true;   // scroll yaw + tilt (PieceTilt) need this
            if (prefab.GetComponent<DtpPiece>() == null) prefab.AddComponent<DtpPiece>();   // kind + snap registry
            ReplaceMeshColliders(prefab);
            AddWear(prefab);

            // Valheim only looks at DIRECT children of the piece root for snap points (Piece.GetSnapPoints),
            // but the bundle keeps them under "model". Move them to the root, keeping world positions.
            var snaps = new System.Collections.Generic.List<Transform>();
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                if (t != prefab.transform && t.name.StartsWith("snappoint")) snaps.Add(t);
            foreach (var t in snaps) t.SetParent(prefab.transform, true);

            int pieceLayer = LayerMask.NameToLayer("piece");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = pieceLayer;                      // needed for placement raycasts / hover
                if (t.name.StartsWith("snappoint")) t.tag = "snappoint"; // Valheim snap system looks for this tag
            }
            if (ModSettings.DebugLog.Value)
                ModLog.Info("Prepared " + prefab.name + ": " + snaps.Count + " snappoints, " +
                    prefab.GetComponentsInChildren<Renderer>(true).Length + " renderers, " +
                    prefab.GetComponentsInChildren<Collider>(true).Length + " colliders");
        }

        /// Health + destruction: pieces can be hit and broken, drop their building materials when destroyed (WearNTear),
        /// and are refunded when removed with the hammer (the requirements are registered with Recover = true).
        /// They never collapse from missing support, rain or fire.
        /// m_supports MUST stay true: Player.UpdatePlacementGhost marks a placement Invalid (red ghost) when the cursor is over a
        /// WearNTear that has m_supports == false, which made hoses unplaceable while aiming at the pump / other hoses.
        /// m_noSupportWear / m_noRoofWear are the "WEAR WHEN ..." switches (true = the piece takes damage without support /
        /// without a roof; WearNTear.UpdateWear deals 100 damage per tick when m_noSupportWear is set and support is short).
        /// They must be FALSE here: with true, a hose hanging off the pump's top outlet (no ground / no neighbour support)
        /// broke the moment it was placed.
        static void AddWear(GameObject prefab)
        {
            if (prefab.GetComponent<WearNTear>() != null) return;
            var w = prefab.AddComponent<WearNTear>();
            w.m_health = ModSettings.PieceHealth.Value * (prefab.name.Contains("Pump") ? 2f : 1f);
            w.m_materialType = WearNTear.MaterialType.Wood;
            w.m_supports = true; w.m_noSupportWear = false; w.m_noRoofWear = false; w.m_burnable = false;
            w.m_autoCreateFragments = false;
            w.m_hitEffect = Fx("vfx_SawDust", "sfx_wood_hit");
            w.m_destroyedEffect = Fx("vfx_SawDust", "sfx_wood_break");
            var piece = prefab.GetComponent<Piece>();
            if (piece != null) piece.m_placeEffect = Fx("sfx_build_hammer_wood");
        }

        /// Effect list from the vanilla prefabs that exist (unknown names are skipped, nothing breaks).
        static EffectList Fx(params string[] names)
        {
            var list = new System.Collections.Generic.List<EffectList.EffectData>();
            foreach (var n in names)
            {
                var p = PrefabManager.Cache.GetPrefab<GameObject>(n);
                if (p != null) list.Add(new EffectList.EffectData { m_prefab = p });
                else ModLog.Verbose("Vanilla effect prefab '" + n + "' not found - skipped.");
            }
            return new EffectList { m_effectPrefabs = list.ToArray() };
        }

        /// No renderer / audio on a dedicated server or with -nographics.
        public static bool Headless { get { return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null; } }

        /// The bundle's MeshColliders use non-readable, un-baked meshes, which do not work at runtime (no collision,
        /// and the piece could not be placed/hovered). Swap them for primitives sized from the mesh bounds.
        static void ReplaceMeshColliders(GameObject prefab)
        {
            foreach (var mc in prefab.GetComponentsInChildren<MeshCollider>(true))
            {
                var go = mc.gameObject;
                var mesh = mc.sharedMesh;
                if (mesh == null) { mc.enabled = false; continue; }
                var b = mesh.bounds;
                if (prefab.name.Contains("Elbow"))
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = b.center; box.size = b.size;
                }
                else
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.direction = 2;                                  // Z axis: hoses run along Z
                    cap.center = b.center;
                    cap.radius = Mathf.Max(b.extents.x, b.extents.y);
                    cap.height = b.size.z;
                }
                try { Object.DestroyImmediate(mc, true); } catch { mc.enabled = false; }
                ModLog.Verbose("Replaced MeshCollider on " + prefab.name + " with a " +
                    "primitive collider.");
            }
        }

        // --- icon view -------------------------------------------------------------------------------------------------------
        // The renderer looks along the piece's Z axis, and every hose / strainer / nozzle runs along Z, so the icons were all the end of a pipe (a ring) and
        // looked alike. The pipe pieces are turned to be seen from the SIDE, tilted a little so the pipe runs diagonally through the square icon and the
        // shape of each piece shows: straight pipe, strainer, nozzle, elbow. The pump keeps the default view.
        // Pipes are also drawn to scale against the 2 m hose (softened with a square root, so the short nozzle does not shrink to a dot): a 1 m hose
        // looks shorter than a 2 m one. Rotation angles: edit the two Quaternion.Euler lines below (pitch, yaw, roll in degrees).
        const float IconRefLength = 2f;                 // m: the longest piece (2 m hose) fills the icon
        const float IconMinScale = 0.45f;               // smallest relative size a short piece gets

        static void ApplyIconView(GameObject prefab, RenderManager.RenderRequest req)
        {
            var kind = DtpPiece.KindOf(prefab.name);
            if (kind == DtpKind.Pump || kind == DtpKind.Other) return;
            req.Rotation = kind == DtpKind.Elbow ? Quaternion.Euler(45f, 0f, 0f)      // both legs of the bend visible
                                                 : Quaternion.Euler(20f, 90f, 0f);     // straight pieces side-on
            float len = LongestEdge(prefab);
            if (len > 0.05f)
            {
                float scale = Mathf.Clamp(Mathf.Sqrt(len / IconRefLength), IconMinScale, 1f);
                req.DistanceMultiplier /= scale;                // further away = smaller
            }
            ModLog.Verbose("Icon view " + prefab.name + ": length " + len.ToString("0.00") + " m");
        }

        /// Longest edge (m) of the box around all meshes of the prefab, in the prefab's own space (0 when it has no mesh).
        static float LongestEdge(GameObject prefab)
        {
            try
            {
                var inv = prefab.transform.worldToLocalMatrix;
                Bounds b = default; bool any = false;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    var m = inv * mf.transform.localToWorldMatrix;
                    var c = mesh.bounds.center; var e = mesh.bounds.extents;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                return any ? Mathf.Max(b.size.x, b.size.y, b.size.z) : 0f;
            }
            catch (System.Exception ex) { ModLog.VerboseWarn("Icon size not measured for " + prefab.name + ": " + ex.Message); return 0f; }
        }

        /// Jotunn rejects a piece without m_icon, so an icon must exist BEFORE PieceManager.AddPiece.
        public static Sprite Placeholder()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(70, 55, 40, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }

        /// Renders the real icon. Called a few seconds after a world is entered (textures resident, lighting ready);
        /// rendering during startup produced all-white icons.
        public static Sprite RenderIcon(GameObject prefab)
        {
            try
            {
                var req = new RenderManager.RenderRequest(prefab) { Width = 128, Height = 128, UseCache = false };
                ApplyIconView(prefab, req);
                var sprite = RenderManager.Instance.Render(req);
                if (sprite == null) ModLog.Warn("Icon render returned null for " + prefab.name);
                return sprite;
            }
            catch (System.Exception e) { ModLog.Warn("Icon render failed for " + prefab.name + ": " + e.Message); return null; }
        }
    }
}
