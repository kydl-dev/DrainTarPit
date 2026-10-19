namespace DrainTarPit.Pump
{
    /// The pump's ZDO keys and RPC names: who is mounted, the player-clip phase, the lever animator state, the stroke count and the
    /// nozzle pour. Every client that has the pump loaded reads them. Stored inside worlds: never rename the strings.
    internal static class PumpZdo
    {
        public const string Strokes = "DTP_strokes";
        public const string SplashSeq = "DTP_splashSeq", SplashVol = "DTP_splashVol", Nozzle = "DTP_nozzle", NozzleDir = "DTP_nozzleDir";
        public const string User = "DTP_user", UserName = "DTP_userName", Phase = "DTP_phase", PhaseSeq = "DTP_phaseSeq";
        public const string Active = "DTP_active", Pumping = "DTP_pumping", PumpSeq = "DTP_pumpSeq";
        public const string Liquid = "DTP_pumpLiquid";      // what the pump is moving (PumpLiquid as int), so every client colours the slug / pour the same way
        public const string RpcMount = "DTP_Mount", RpcMountReply = "DTP_MountReply";

        public static bool IsAlive(long playerId)
        {
            var pl = Player.GetPlayer(playerId);
            return pl != null && !pl.IsDead();
        }
    }
}
