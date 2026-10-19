namespace DrainTarPit.Pump
{
    /// What the mounted player (and with it the lever) is doing. Every phase except None has a player clip (PumpClips.ClipOf).
    internal enum PumpPhase { None, Enter, Idle, Push, Pull, Cycle, Exit }
}
