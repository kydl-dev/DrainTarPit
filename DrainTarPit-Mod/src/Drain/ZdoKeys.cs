namespace DrainTarPit.Drain
{
    /// <summary>
    /// World-save keys. User, 2026-10-20: the old "TPD" prefix (the mod's earlier name) is replaced by "DTP" everywhere. Worlds saved before still
    /// hold the old keys, so every read falls back to the old key and every write uses the new one: an old pool is carried over the first time it
    /// is written again, and nothing saved earlier is lost.
    /// </summary>
    internal static class ZdoKeys
    {
        internal static bool Bool(ZDO z, string key, string oldKey) { return z != null && (z.GetBool(key, false) || z.GetBool(oldKey, false)); }

        internal static int Int(ZDO z, string key, string oldKey)
        {
            if (z == null) return 0;
            int v = z.GetInt(key, 0);
            return v != 0 ? v : z.GetInt(oldKey, 0);
        }

        internal static float Float(ZDO z, string key, string oldKey)
        {
            if (z == null) return 0f;
            float v = z.GetFloat(key, 0f);
            return v > 0f ? v : z.GetFloat(oldKey, 0f);
        }

        internal static long Long(ZDO z, string key, string oldKey)
        {
            if (z == null) return 0L;
            long v = z.GetLong(key, 0L);
            return v > 0L ? v : z.GetLong(oldKey, 0L);
        }
    }
}
