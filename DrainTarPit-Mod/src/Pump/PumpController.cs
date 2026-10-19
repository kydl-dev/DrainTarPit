using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Building;
using DrainTarPit.Drain;
using DrainTarPit.Effects;
using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// <summary>
    /// The hand pump. E on it mounts the player (Player.AttachStart, the same mechanism chairs use) on the prefab's
    /// stand_point, turned towards the lever. While mounted:
    ///   tap E   = one stroke (player Push clip then Pull clip, the lever does the same Push -> Pull on its own animator)
    ///   hold E  = continuous pumping (Cycle clip + lever Cycle state, one drain per loop) until E is released
    ///   Shift+E / Space / walking away = Exit clip, then the player is released
    /// Damage, death, being pushed too far from the stand point, or the pump being destroyed release the player at once.
    ///
    /// Sync: player clips and lever animator are started in the SAME frame (StartPhase) and the clips have the same lengths, so the
    /// hands and the lever move together. Hands are pinned to the lever handle with a two-bone IK in LateUpdate (PumpHandIK).
    /// Anchors in the prefab: model/stand_point, model/Lever/interact_point.
    ///
    /// Multiplayer: the pump's ZDO (PumpZdo) carries who is mounted, the player-clip phase, the lever animator state and the stroke
    /// count. PumpMountNet arbitrates mounting through the pump's owner; PumpObserver mirrors the ZDO on every other client.
    /// Helpers: PumpLever (lever animator), PumpStand (where the player stands, AutoReach). Hose links are derived from the placed
    /// pieces' positions (HoseNetwork), so nothing is stored for them.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // LateUpdate after the game's own scripts, so the hand IK has the last word on the bones
    public class PumpController : MonoBehaviour, Hoverable, Interactable
    {
        const float CycleLead = 0.1f;          // the continue/stop decision is taken this long before a Cycle loop ends

        static readonly Dictionary<Player, PumpController> Users = new();
        static readonly FieldInfo FAttachPoint = AccessTools.Field(typeof(Player), "m_attachPoint");
        static readonly MethodInfo MTakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        public static PumpController For(Player p) { PumpController c; return p != null && Users.TryGetValue(p, out c) ? c : null; }

        PumpLever _lever;
        PumpStand _stand;
        PumpMountNet _net;
        PumpObserver _observer;
        ZNetView _nview;
        DtpPiece _piece;
        bool _active;

        Player _user; bool _hasUser;
        PumpPhase _phase; bool _phaseStarted; float _phaseEnd;
        bool _forceStop, _destroying, _exitRequested;
        int _mountFrame;

        // hold-to-pump / buffering
        float _holdStart = -1f; bool _wantCycle, _blockCycle;
        float _queuedUntil;
        float _noClipStrokeReadyAt;            // only used while the player clips could not be detected
        // continuous pumping
        float _cycleLen, _cycleCheckAt;
        bool _cycleStopping;

        float _ikWeight;
        Player _ikPlayer;                      // whose fingers PumpHandIK has curled (restored in ReleaseIk)
        Transform _fxPoint;                    // fx_outlet (pump outlet) for the droplet / sound effects
        bool _lastDry;                         // the last TryStroke found nothing within reach (valid setup, empty pit area)

        public Transform StandPoint { get; private set; }
        public Transform InteractPoint { get; private set; }
        public bool IsActive { get { return _active; } }
        public bool InUse { get { return _hasUser && _user != null; } }

        void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<DtpPiece>();
            StandPoint = FindDeep(transform, "stand_point");
            InteractPoint = FindDeep(transform, "interact_point");
            _fxPoint = FindDeep(transform, "fx_outlet") ?? transform;
            _lever = new PumpLever(GetComponentInChildren<Animator>(true));
            _stand = new PumpStand(StandPoint, InteractPoint);
            _net = new PumpMountNet(this, _nview);
            _observer = new PumpObserver(this, _nview, _piece, _lever, _fxPoint);
        }

        void Start() { _net.Register(); }

        ZDO Zdo { get { return _net.Zdo; } }

        // --- pump prefab animator -------------------------------------------------------------------------------------
        public void SetActive(bool on) { _active = on; _lever.Set("active", on); _net.WriteFlag(PumpZdo.Active, on, _hasUser); }
        public void SetPumping(bool on) { _lever.Set("pumping", on); _net.WriteFlag(PumpZdo.Pumping, on, _hasUser); }
        public void Stroke()
        {
            _lever.Set("pump", true);
            var z = Zdo; if (_hasUser && z != null && _nview.IsOwner()) z.Set(PumpZdo.PumpSeq, z.GetInt(PumpZdo.PumpSeq, 0) + 1);
        }

        /// Called by PumpObserver: the lever shows what the player on the pump does.
        internal void MirrorActive(bool on) { _active = on; _lever.Set("active", on); }
        internal void ResetIkWeight() { _ikWeight = 0f; }

        public string DescribeState()
        {
            var z = Zdo; if (z == null) return "no ZDO";
            return "user=" + z.GetLong(PumpZdo.User, 0L) + " (" + z.GetString(PumpZdo.UserName, "") + ") owner=" + _nview.IsOwner() +
                   " phase=" + (PumpPhase)z.GetInt(PumpZdo.Phase, 0) + "#" + z.GetInt(PumpZdo.PhaseSeq, 0) +
                   " active=" + z.GetBool(PumpZdo.Active, false) + " pumping=" + z.GetBool(PumpZdo.Pumping, false) + " strokes=" + z.GetInt(PumpZdo.Strokes, 0) +
                   " local: hasUser=" + _hasUser + " observing=" + (_observer.Remote != null);
        }

        public string DescribeAnimator() { return _lever.Describe(); }

        // --- Hoverable / Interactable ---------------------------------------------------------------------------------
        public string GetHoverName() { return Loc.T("dtp_hand_pump"); }
        public float GetHoverOffset() { return 0f; }
        string _hoverDesc; float _hoverDescAt;
        public string GetHoverText()
        {
            string use;
            const string key = "\n[<color=yellow><b>$KEY_Use</b></color>] ", alt = "\n[<color=yellow><b>Shift + $KEY_Use</b></color>] ";
            string other = _net.OccupiedByOther(Player.m_localPlayer);
            if (StandPoint == null) use = _active ? key + Loc.T("dtp_pump") + alt + Loc.T("dtp_stop") : key + Loc.T("dtp_prime");
            else if (InUse && _user == Player.m_localPlayer) use = key + Loc.T("dtp_pump_hold") + alt + Loc.T("dtp_step_away");
            else if (_net.Pending) use = "\n...";
            else if (other != null) use = "\n" + Loc.T("dtp_in_use_by", other);
            else use = key + Loc.T("dtp_operate");
            string strokes = "";
            if (_nview != null && _nview.IsValid()) { int n = _nview.GetZDO().GetInt(PumpZdo.Strokes, 0); if (n > 0) strokes = "\n" + Loc.T("dtp_strokes", n.ToString()); }
            // the hose trace + pit sampling is not free: refresh the status text a few times per second, not every frame
            if (_piece != null && (_hoverDesc == null || Time.unscaledTime - _hoverDescAt > 0.25f)) { _hoverDesc = DrainService.Describe(_piece); _hoverDescAt = Time.unscaledTime; }
            return Localization.instance.Localize(Loc.T("dtp_hand_pump") + use + strokes + (_piece != null ? "\n" + _hoverDesc : ""));
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;          // continuous pumping reads the button itself (Update), not Interact(hold)
            var p = user as Player;
            if (StandPoint == null || p == null)       // no stand point in the prefab: old behaviour, no mounting
            {
                if (StandPoint == null) ModLog.Warn("Pump: prefab has no 'stand_point' - mounting disabled.");
                if (alt) { if (_active) SetActive(false); }
                else if (!_active) SetActive(true);
                else if (TryStroke(user)) Stroke();
                return true;
            }
            if (!InUse)
            {
                if (alt || _net.Pending) return true;
                var other = _net.OccupiedByOther(p);
                if (other != null) { Tell(p, Loc.T("dtp_msg_in_use_by", other)); return true; }
                _net.RequestMount(p);
                return true;
            }
            if (p != _user) { Tell(user, Loc.T("dtp_msg_in_use")); return true; }
            if (Time.frameCount <= _mountFrame + 1) return true;     // the press that mounted must not also pump
            if (alt) RequestExit(); else OnUsePressed();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) { return false; }

        // --- mounting -------------------------------------------------------------------------------------------------
        internal bool Mount(Player p)
        {
            if (p.IsAttached()) { Tell(p, Loc.T("dtp_msg_cant_use")); ModLog.Info("Pump: mount refused, player is already attached to something"); return false; }
            if (p.IsEncumbered()) { Tell(p, Loc.T("dtp_msg_too_heavy")); return false; }

            _stand.Begin();                                  // face the lever, StandHeight / StandForward / StandSide

            PlayerPumpAnimations.BeginDetect(p);
            p.AttachStart(StandPoint, gameObject, true, false, false, ModSettings.AttachBool.Value, new Vector3(0f, 0.1f, 0f), null);
            if (!p.IsAttached()) { ModLog.Warn("Pump: AttachStart did not attach the player."); _stand.Restore(); return false; }

            _user = p; _hasUser = true; Users[p] = this; _mountFrame = Time.frameCount;
            _exitRequested = false; _wantCycle = false; _cycleStopping = false; _queuedUntil = 0f; _noClipStrokeReadyAt = 0f; _ikWeight = 0f;
            _observer.ForgetRemote(); _holdStart = -1f; _blockCycle = true;   // the E that mounted may still be held: it must not start continuous pumping
            SetPhase(PumpPhase.Enter);                       // the lever's Enter animation starts together with the player's (StartPhase)
            ModLog.Info("Pump: " + p.GetPlayerName() + " mounted the pump (attach bool '" + ModSettings.AttachBool.Value + "', clips ready: " + PlayerPumpAnimations.Ready + ")");
            return true;
        }

        /// Ask to leave. The Exit clip plays once the current stroke / loop is finished, so lever and player stay in sync.
        void RequestExit() { if (_hasUser) { _exitRequested = true; _wantCycle = false; _queuedUntil = 0f; } }

        /// Called by the Player.AttachStop patch. false = swallow it (the Exit clip plays first, then we call it again).
        public bool AllowAttachStop()
        {
            if (_forceStop || !_hasUser || _destroying) return true;
            if (_user == null || _user.IsDead()) return true;                 // death etc.: leave immediately
            if (!PlayerPumpAnimations.Ready) { ModLog.Info("Pump: stepping off without the Exit clip (attach clip was never detected)"); return true; }
            RequestExit();
            return false;
        }

        /// Called after the player really left the stand point.
        public void OnDetached()
        {
            if (!_hasUser) return;
            var p = _user;
            _hasUser = false; _user = null; _phase = PumpPhase.None; _phaseStarted = false;
            _exitRequested = false; _wantCycle = false; _cycleStopping = false; _ikWeight = 0f; _holdStart = -1f;
            if ((object)p != null) { Users.Remove(p); if (p != null) PlayerPumpAnimations.Release(p); }
            _stand.End();
            _net.Release();                                  // other clients stop mirroring
            _lever.Set("pumping", false); MirrorActive(false);
        }

        /// Leaves immediately, no Exit clip (damage, distance, destroyed pump, ...).
        void FinishExit()
        {
            var p = _user;
            _forceStop = true;
            try { if (p != null && p.IsAttached()) p.AttachStop(); }
            catch (System.Exception e) { ModLog.Warn("Pump: AttachStop failed (" + e.GetType().Name + "), releasing the player anyway"); }
            finally { _forceStop = false; }
            OnDetached();
        }

        /// The mounted player got hurt: step off right away.
        public void OnUserDamaged()
        {
            if (!_hasUser) return;
            ModLog.Info("Pump: player was hit - stepping off");
            FinishExit();
        }

        void OnDestroy()
        {
            _destroying = true;
            try
            {
                if (_hasUser) FinishExit();
                else if (_observer != null && _observer.Remote != null) PlayerPumpAnimations.Release(_observer.Remote);
            }
            catch (System.Exception e)
            {   // quitting / logging out: the scene is torn down and the player's own components may already be gone
                ModLog.Verbose("Pump: cleanup on destroy skipped (" + e.GetType().Name + ")");
            }
        }

        // --- input ----------------------------------------------------------------------------------------------------
        // E pressed while mounted (Interact): one stroke when the pump is ready, otherwise the press is remembered for a
        // moment so tapping E in rhythm does not lose strokes.
        void OnUsePressed()
        {
            if (_exitRequested || _phase == PumpPhase.Exit || _phase == PumpPhase.Cycle) return;
            if (!PlayerPumpAnimations.Ready)           // clips not detected: still pump, the lever animator alone cannot overlap itself
            {
                if (Time.time < _noClipStrokeReadyAt) return;
                if (TryStroke(_user)) { Stroke(); _noClipStrokeReadyAt = Time.time + PumpClips.StrokeLength(); }
                return;
            }
            if (_phase == PumpPhase.Idle) { if (TryStroke(_user)) SetPhase(PumpPhase.Push); }
            else _queuedUntil = Time.time + 0.6f;      // Enter / Push / Pull still running
        }

        static bool InputAllowed(Player p)
        {
            if (p == null) return false;
            if (MTakeInput == null) return true;
            try { return (bool)MTakeInput.Invoke(p, null); } catch { return true; }
        }

        // hold E = continuous pumping. Only the local player's button is read.
        void ReadHold()
        {
            if (_user != Player.m_localPlayer || !InputAllowed(_user)) { _holdStart = -1f; _wantCycle = false; return; }
            bool down = ZInput.GetButton("Use") || ZInput.GetButton("JoyUse");
            if (down && !_exitRequested)
            {
                if (_holdStart < 0f) _holdStart = Time.time;
                if (!_blockCycle && Time.time - _holdStart >= ModSettings.HoldToCycleSeconds.Value) _wantCycle = true;
            }
            else { _holdStart = -1f; _wantCycle = false; _blockCycle = false; }
        }

        // --- player clip state machine --------------------------------------------------------------------------------
        public void StartCycle() { if (_hasUser && _phase == PumpPhase.Idle && TryStroke(_user) && !_lastDry) SetPhase(PumpPhase.Cycle); }
        public void StopCycle() { _wantCycle = false; _blockCycle = true; }

        void SetPhase(PumpPhase p) { _phase = p; _phaseStarted = false; }

        // Starts a phase: the player's clip and the lever's animator parameter change in this same call, so both begin
        // on the same frame. (Enter: lever "active" -> Enter; Push: lever "pump" trigger -> Push then Pull by itself;
        // Cycle: lever "pumping" -> Cycle loop; Exit: lever "active" off -> Exit.)
        void StartPhase()
        {
            _phaseStarted = true;
            string clip = PumpClips.ClipOf(_phase);
            _net.WritePhase(_phase);
            if (clip != null) PlayerPumpAnimations.Play(_user, clip);

            bool loops = _phase == PumpPhase.Idle || _phase == PumpPhase.Cycle;
            _phaseEnd = (clip == null) ? 0f : loops ? float.MaxValue : Time.time + Mathf.Max(0.1f, PlayerPumpAnimations.Length(clip));

            switch (_phase)
            {
                case PumpPhase.Enter: SetActive(true); break;
                case PumpPhase.Push: Stroke(); break;
                case PumpPhase.Exit: SetActive(false); break;
                case PumpPhase.Cycle:
                    _cycleLen = PlayerPumpAnimations.Length("pumpcycle");
                    if (_cycleLen < 0.2f) _cycleLen = ModSettings.CycleStrokeSeconds.Value;
                    _cycleCheckAt = Time.time + _cycleLen - CycleLead;
                    _cycleStopping = false;
                    SetPumping(true);
                    break;
            }
        }

        void Update()
        {
            if (_hasUser) { UserUpdate(); return; }
            if (_net.TimedOut()) Tell(Player.m_localPlayer, Loc.T("dtp_msg_no_answer"));
            _observer.Update();
        }

        void UserUpdate()
        {
            if (_user == null || _user.IsDead() || !_user.IsAttached() || (FAttachPoint.GetValue(_user) as Transform) != StandPoint)
            {   // left by some other route (teleport, another attach, destroyed, logout)
                ModLog.Info("Pump: player is no longer attached to the pump (dead=" + (_user != null && _user.IsDead()) + ", attached=" + (_user != null && _user.IsAttached()) + ")");
                OnDetached();
                return;
            }
            if (StandPoint != null && Vector3.Distance(_user.transform.position, StandPoint.position) > ModSettings.MaxPumpDistance.Value)
            {   // pushed / pulled away from the pump
                ModLog.Info("Pump: player is too far from the stand point - stepping off");
                FinishExit();
                return;
            }

            ReadHold();

            if (!PlayerPumpAnimations.Ready)
            {
                PlayerPumpAnimations.TryDetect(_user);
                if (!PlayerPumpAnimations.Ready) { if (_exitRequested) FinishExit(); return; }
            }

            // continuous pumping: one drain per loop; the decision to go on (or stop) is made just before the loop ends,
            // so the lever's Cycle state (which only leaves at the end of a loop) and the player clip end together
            if (_phaseStarted && _phase == PumpPhase.Cycle && !_cycleStopping && Time.time >= _cycleCheckAt)
            {
                bool go = _wantCycle && !_exitRequested;
                if (go && (!TryStroke(_user) || _lastDry)) { go = false; _wantCycle = false; _blockCycle = true; }   // message was shown
                if (go) _cycleCheckAt += _cycleLen;
                else { _cycleStopping = true; SetPumping(false); _phaseEnd = _cycleCheckAt + CycleLead; }
            }

            if (_phaseStarted && Time.time >= _phaseEnd)
            {
                switch (_phase)
                {
                    case PumpPhase.Exit: FinishExit(); return;
                    case PumpPhase.Push: SetPhase(PumpPhase.Pull); break;            // the lever goes Push -> Pull on its own
                    default: SetPhase(PumpPhase.Idle); break;                        // Enter / Pull / Cycle finished
                }
            }

            if (_phase == PumpPhase.Idle)       // ready for the next thing; whatever is picked starts in this same frame
            {
                if (_exitRequested) SetPhase(PumpPhase.Exit);
                else if (_wantCycle) { if (TryStroke(_user) && !_lastDry) SetPhase(PumpPhase.Cycle); else { _wantCycle = false; _blockCycle = true; } }
                else if (Time.time < _queuedUntil) { _queuedUntil = 0f; if (TryStroke(_user)) SetPhase(PumpPhase.Push); }
            }

            if (!_phaseStarted) StartPhase();
        }

        void LateUpdate()
        {
            if (InteractPoint == null || !ModSettings.HandIK.Value) { ReleaseIk(); return; }
            Player who; PumpPhase ph;
            if (_hasUser && _user != null) { who = _user; ph = _phase; }
            else if (_observer.Remote != null) { who = _observer.Remote; ph = _observer.RemotePhase; }
            else { _ikWeight = 0f; ReleaseIk(); return; }
            float target = (ph == PumpPhase.None || ph == PumpPhase.Exit) ? 0f : 1f;
            _ikWeight = Mathf.MoveTowards(_ikWeight, target, Time.deltaTime / Mathf.Max(0.05f, ModSettings.HandIKBlendSeconds.Value));
            if (_ikWeight > 0.001f)
            {
                float need = PumpHandIK.Apply(who, InteractPoint, _ikWeight); _ikPlayer = who;
                if (_hasUser && who == _user) _stand.AutoReach(need, _phase);
            }
            else ReleaseIk();
        }

        // puts the fingers back once the hands are off the handle
        void ReleaseIk()
        {
            if (_ikPlayer == null) return;
            PumpHandIK.Release(_ikPlayer);
            _ikPlayer = null;
        }

        // --- pumping --------------------------------------------------------------------------------------------------
        // One stroke: stamina, validate hoses + pit, ask the pit owner to remove tar (the tar pours out at the nozzle when the
        // owner confirms), count it. false = nothing happened (no stamina / invalid setup, the reason is shown).
        bool TryStroke(Humanoid user)
        {
            if (_piece == null || _nview == null || !_nview.IsValid()) return true;     // nothing to validate (should not happen)
            var player = user as Player;
            float cost = ModSettings.StaminaPerStroke.Value;
            if (player != null && cost > 0f && !player.HaveStamina(cost)) { Tell(user, Loc.T("dtp_msg_tired")); return false; }

            if (!_nview.IsOwner()) _nview.ClaimOwnership();          // the stroke counter + nozzle pour live in the pump's ZDO
            var r = DrainService.Stroke(_piece, _nview);
            _lastDry = false;
            if (!r.Valid) { Tell(user, r.Message); return false; }

            if (player != null && cost > 0f) player.UseStamina(cost);
            var zdo = _nview.GetZDO();
            if (zdo != null) zdo.Set(PumpZdo.Strokes, zdo.GetInt(PumpZdo.Strokes, 0) + 1);
            _lastDry = r.Dry;
            PumpFx.Stroke(_fxPoint);
            if (!r.SlugStarted) TarFlow.Stroke(_piece, false);      // dry stroke: the slug is shown, no pour follows it (a stroke that takes tar started its slug in DrainService.Stroke)
            if (r.Dry) Tell(user, r.Message);
            return true;
        }

        internal static void Tell(Humanoid user, string msg)
        {
            if (user != null && !string.IsNullOrEmpty(msg)) user.Message(MessageHud.MessageType.Center, msg);
        }

        static Transform FindDeep(Transform root, string n)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }
    }

    /// Lets the pump play the Exit clip before the player is released, and cleans up when the player leaves.
    [HarmonyPatch(typeof(Player), "AttachStop")]
    static class Patch_PlayerAttachStop
    {
        static bool Prefix(Player __instance)
        {
            var c = PumpController.For(__instance);
            return c == null || c.AllowAttachStop();
        }

        static void Postfix(Player __instance)
        {
            var c = PumpController.For(__instance);
            if (c != null && !__instance.IsAttached()) c.OnDetached();
        }
    }

    /// Getting hit while pumping throws the player off the pump (no Exit clip). Player.OnDamaged runs when damage was applied.
    [HarmonyPatch(typeof(Player), "OnDamaged")]
    static class Patch_PumpUserDamaged
    {
        static void Postfix(Player __instance)
        {
            var c = PumpController.For(__instance);
            if (c != null) c.OnUserDamaged();
        }
    }
}

namespace DrainTarPit.Pump
{
    /// While the hands are pinned to the lever the vanilla look-at IK (CharacterAnimEvent.OnAnimatorIK: head + body follow the camera) is skipped.
    /// It twisted the spine towards the camera every frame, so the shoulders - and with them the arm IK and the back bend - moved with every
    /// camera movement (flicker, worst with the camera at the side). The head simply keeps the animation's pose while pumping.
    [HarmonyPatch]
    static class Patch_NoLookIkOnPump
    {
        static readonly FieldInfo FChar = AccessTools.Field(typeof(CharacterAnimEvent), "m_character");

        static bool Prepare() { return FChar != null && AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK") != null; }
        static MethodBase TargetMethod() { return AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK"); }

        static bool Prefix(CharacterAnimEvent __instance)
        {
            if (PumpHandIK.Pinned.Count == 0) return true;
            var p = FChar.GetValue(__instance) as Player;
            return !PumpHandIK.IsPinned(p);
        }
    }
}
