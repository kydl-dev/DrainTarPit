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
    /// Sync: player clips and lever animator are started in the SAME frame (StartPhase) and the clips have the same
    /// lengths, so the hands and the lever move together. The lever's animator is never poked from anywhere else while
    /// somebody is mounted. Hands are then pinned to the lever handle with a two-bone IK in LateUpdate (PumpHandIK).
    ///
    /// Anchors in the prefab: model/stand_point, model/Lever/interact_point.
    /// Multiplayer (section 3): the pump's ZDO carries who is mounted (DTP_user), the player-clip phase (DTP_phase + seq),
    /// the lever animator state (DTP_active / DTP_pumping / DTP_pumpSeq) and the stroke count. Mounting is arbitrated by
    /// the pump's current owner (RPC DTP_Mount -> DTP_MountReply); the player that gets it claims the ZDO and drives it.
    /// Every other client that has the pump loaded mirrors the ZDO (lever animator, the mounted player's clips, hand IK).
    /// Hose links are derived from the placed pieces' positions (HoseNetwork), so nothing is stored for them.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // LateUpdate after the game's own scripts, so the hand IK has the last word on the bones
    public class PumpController : MonoBehaviour, Hoverable, Interactable
    {
        enum Phase { None, Enter, Idle, Push, Pull, Cycle, Exit }

        const string ZdoStrokes = "DTP_strokes";
        internal const string ZSplashSeq = "DTP_splashSeq", ZSplashVol = "DTP_splashVol", ZNozzle = "DTP_nozzle", ZNozzleDir = "DTP_nozzleDir";   // nozzle pour, mirrored to other clients
        const string ZUser = "DTP_user", ZUserName = "DTP_userName", ZPhase = "DTP_phase", ZPhaseSeq = "DTP_phaseSeq";
        const string ZActive = "DTP_active", ZPumping = "DTP_pumping", ZPumpSeq = "DTP_pumpSeq";
        internal const string ZPumpLiquid = "DTP_pumpLiquid";      // what the pump is moving (PumpLiquid as int), so every client colours the slug / pour the same way
        const string RpcMount = "DTP_Mount", RpcMountReply = "DTP_MountReply";
        const float CycleLead = 0.1f;          // the continue/stop decision is taken this long before a Cycle loop ends

        static readonly Dictionary<Player, PumpController> Users = new();
        static readonly FieldInfo FAttachPoint = AccessTools.Field(typeof(Player), "m_attachPoint");
        static readonly MethodInfo MTakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        public static PumpController For(Player p) { PumpController c; return p != null && Users.TryGetValue(p, out c) ? c : null; }

        Animator _anim;
        ZNetView _nview;
        DtpPiece _piece;
        bool _active;

        Player _user; bool _hasUser;
        Phase _phase; bool _phaseStarted; float _phaseEnd;
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
        Vector3 _standBaseLocal; bool _standBaseKnown;   // stand_point's position as authored in the prefab
        Vector3 _standFwd = Vector3.forward;             // horizontal direction player -> lever, fixed when mounting
        float _pendingNeed;                              // AutoReach shortfall seen during a stroke, applied when the pose is calm
        float _autoStep, _autoRaise;                     // AutoReach: extra metres towards the lever / up so both hands reach the handle
        Player _ikPlayer;                      // whose fingers PumpHandIK has curled (restored in ReleaseIk)

        // multiplayer
        bool _mountPending; float _mountPendingUntil;
        Player _remote;                        // the player mounted on this pump when that is somebody else (we only mirror)
        int _seenPhaseSeq = -1, _seenPumpSeq = -1;
        Phase _remotePhase;
        int _seenStrokes = -1;
        int _seenSplash = -1;
        Transform _fxPoint;                    // fx_outlet (pump outlet) for the droplet / sound effects
        bool _lastDry;                         // the last TryStroke found nothing within reach (valid setup, empty pit area)

        public Transform StandPoint { get; private set; }
        public Transform InteractPoint { get; private set; }
        public bool IsActive { get { return _active; } }
        public bool InUse { get { return _hasUser && _user != null; } }

        void Awake()
        {
            _anim = GetComponentInChildren<Animator>(true);
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<DtpPiece>();
            StandPoint = FindDeep(transform, "stand_point");
            InteractPoint = FindDeep(transform, "interact_point");
            if (StandPoint != null) { _standBaseLocal = StandPoint.localPosition; _standBaseKnown = true; }
            _fxPoint = FindDeep(transform, "fx_outlet") ?? transform;
        }

        // RPCs are registered in Start: ZNetView.Awake (which creates / loads the ZDO) is guaranteed to have run by then.
        void Start()
        {
            if (_nview != null && _nview.GetZDO() != null)
            {
                _nview.Register<ZPackage>(RpcMount, OnMountRequest);
                _nview.Register<ZPackage>(RpcMountReply, OnMountReply);
            }
        }

        ZDO Zdo { get { return _nview != null && _nview.IsValid() ? _nview.GetZDO() : null; } }

        // --- who is on the pump (ZDO) -----------------------------------------------------------------------------------
        static bool IsAlive(long playerId)
        {
            var pl = Player.GetPlayer(playerId);
            return pl != null && !pl.IsDead();
        }

        /// Name of the player that holds the pump, or null when it is free / the holder is gone / it is `me`.
        string OccupiedByOther(Player me)
        {
            var z = Zdo; if (z == null) return null;
            long id = z.GetLong(ZUser, 0L);
            if (id == 0L || (me != null && id == me.GetPlayerID()) || !IsAlive(id)) return null;
            return z.GetString(ZUserName, "someone");
        }

        // Runs on the pump's owner: grants the pump to one player at a time.
        void OnMountRequest(long sender, ZPackage pkg)
        {
            long id = pkg.ReadLong(); string name = pkg.ReadString();
            var z = Zdo; bool ok = false;
            if (z != null)
            {
                long cur = z.GetLong(ZUser, 0L);
                ok = cur == 0L || cur == id || !IsAlive(cur);
                if (ok) { z.Set(ZUser, id); z.Set(ZUserName, name); z.Set(ZPhase, 0); z.Set(ZActive, false); z.Set(ZPumping, false); }
            }
            var reply = new ZPackage(); reply.Write(ok);
            _nview.InvokeRPC(sender, RpcMountReply, reply);
        }

        // Runs on the player that asked.
        void OnMountReply(long sender, ZPackage pkg)
        {
            bool ok = pkg.ReadBool();
            if (!_mountPending) { if (ok) ReleaseZdo(); return; }      // answer came too late
            _mountPending = false;
            var p = Player.m_localPlayer;
            if (!ok) { Tell(p, Loc.T("dtp_msg_in_use")); return; }
            if (p == null) { ReleaseZdo(); return; }
            _nview.ClaimOwnership();                                   // from now on this client writes the pump's state
            if (!Mount(p)) ReleaseZdo();
        }

        void RequestMount(Player p)
        {
            if (Zdo == null) { Mount(p); return; }                     // no ZDO (should not happen): local only
            _mountPending = true; _mountPendingUntil = Time.time + 3f;
            var pkg = new ZPackage(); pkg.Write(p.GetPlayerID()); pkg.Write(p.GetPlayerName());
            _nview.InvokeRPC(RpcMount, pkg);
        }

        /// Clears the holder + lever state in the ZDO (called by whoever holds the pump).
        void ReleaseZdo()
        {
            var z = Zdo; if (z == null) return;
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            z.Set(ZUser, 0L); z.Set(ZUserName, ""); z.Set(ZActive, false); z.Set(ZPumping, false);
            z.Set(ZPhase, 0); z.Set(ZPhaseSeq, z.GetInt(ZPhaseSeq, 0) + 1);
        }

        void WritePhase()
        {
            var z = Zdo; if (z == null || !_nview.IsOwner()) return;
            z.Set(ZPhase, (int)_phase); z.Set(ZPhaseSeq, z.GetInt(ZPhaseSeq, 0) + 1);
        }

        void WriteFlag(string key, bool v) { var z = Zdo; if (_hasUser && z != null && _nview.IsOwner()) z.Set(key, v); }

        // --- pump prefab animator -------------------------------------------------------------------------------------
        public void SetActive(bool on) { _active = on; Set("active", on); WriteFlag(ZActive, on); }
        public void SetPumping(bool on) { Set("pumping", on); WriteFlag(ZPumping, on); }
        public void Stroke()
        {
            Set("pump", true);
            var z = Zdo; if (_hasUser && z != null && _nview.IsOwner()) z.Set(ZPumpSeq, z.GetInt(ZPumpSeq, 0) + 1);
        }

        // Animator.parameters allocates a new array on every access, and Set runs several times per frame, so the declared
        // parameter types are cached once per controller.
        readonly Dictionary<string, AnimatorControllerParameterType> _paramTypes = new();
        readonly HashSet<string> _paramWarned = new();
        RuntimeAnimatorController _paramsFor;

        // sets a parameter whatever its declared type is (bool or trigger), warns once if it does not exist
        void Set(string param, bool value)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return;
            if (_paramsFor != _anim.runtimeAnimatorController)
            {
                _paramsFor = _anim.runtimeAnimatorController;
                _paramTypes.Clear();
                foreach (var ap in _anim.parameters) _paramTypes[ap.name] = ap.type;
            }
            AnimatorControllerParameterType type;
            if (_paramTypes.TryGetValue(param, out type))
            {
                if (type == AnimatorControllerParameterType.Trigger)
                {
                    if (value) _anim.SetTrigger(param); else _anim.ResetTrigger(param);
                }
                else _anim.SetBool(param, value);
                return;
            }
            if (_paramWarned.Add(param)) ModLog.Warn("Pump animator has no parameter '" + param + "'");
        }

        public string DescribeState()
        {
            var z = Zdo; if (z == null) return "no ZDO";
            return "user=" + z.GetLong(ZUser, 0L) + " (" + z.GetString(ZUserName, "") + ") owner=" + _nview.IsOwner() + " phase=" + (Phase)z.GetInt(ZPhase, 0) + "#" + z.GetInt(ZPhaseSeq, 0) +
                   " active=" + z.GetBool(ZActive, false) + " pumping=" + z.GetBool(ZPumping, false) + " strokes=" + z.GetInt(ZdoStrokes, 0) +
                   " local: hasUser=" + _hasUser + " observing=" + (_remote != null);
        }

        public string DescribeAnimator()
        {
            if (_anim == null) return "no Animator";
            var s = _anim.runtimeAnimatorController == null ? "no controller" : _anim.runtimeAnimatorController.name;
            foreach (var p in _anim.parameters) s += " [" + p.name + ":" + p.type + "]";
            return s;
        }

        // --- Hoverable / Interactable ---------------------------------------------------------------------------------
        public string GetHoverName() { return Loc.T("dtp_hand_pump"); }
        public float GetHoverOffset() { return 0f; }
        string _hoverDesc; float _hoverDescAt;
        public string GetHoverText()
        {
            string use;
            const string key = "\n[<color=yellow><b>$KEY_Use</b></color>] ", alt = "\n[<color=yellow><b>Shift + $KEY_Use</b></color>] ";
            string other = OccupiedByOther(Player.m_localPlayer);
            if (StandPoint == null) use = _active ? key + Loc.T("dtp_pump") + alt + Loc.T("dtp_stop") : key + Loc.T("dtp_prime");
            else if (InUse && _user == Player.m_localPlayer) use = key + Loc.T("dtp_pump_hold") + alt + Loc.T("dtp_step_away");
            else if (_mountPending) use = "\n...";
            else if (other != null) use = "\n" + Loc.T("dtp_in_use_by", other);
            else use = key + Loc.T("dtp_operate");
            string strokes = "";
            if (_nview != null && _nview.IsValid()) { int n = _nview.GetZDO().GetInt(ZdoStrokes, 0); if (n > 0) strokes = "\n" + Loc.T("dtp_strokes", n.ToString()); }
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
                if (alt || _mountPending) return true;
                var other = OccupiedByOther(p);
                if (other != null) { Tell(p, Loc.T("dtp_msg_in_use_by", other)); return true; }
                RequestMount(p);
                return true;
            }
            if (p != _user) { Tell(user, Loc.T("dtp_msg_in_use")); return true; }
            if (Time.frameCount <= _mountFrame + 1) return true;     // the press that mounted must not also pump
            if (alt) RequestExit(); else OnUsePressed();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) { return false; }

        // --- mounting -------------------------------------------------------------------------------------------------
        bool Mount(Player p)
        {
            if (p.IsAttached()) { Tell(p, Loc.T("dtp_msg_cant_use")); ModLog.Info("Pump: mount refused, player is already attached to something"); return false; }
            if (p.IsEncumbered()) { Tell(p, Loc.T("dtp_msg_too_heavy")); return false; }

            if (ModSettings.FacePump.Value && InteractPoint != null)       // turn the stand point so the player looks at the lever
            {
                var dir = InteractPoint.position - StandPoint.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    StandPoint.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, ModSettings.StandYawOffset.Value, 0f);
            }

            _autoStep = 0f; _autoRaise = 0f; _pendingNeed = 0f;
            if (InteractPoint != null && StandPoint != null)
            {
                var toLever = InteractPoint.position - StandPointBaseWorld(); toLever.y = 0f;
                if (toLever.sqrMagnitude > 0.0001f) _standFwd = toLever.normalized;
            }
            ApplyStand();                                    // StandHeight / StandForward / StandSide (+ AutoReach lift while pumping)

            PlayerPumpAnimations.BeginDetect(p);
            p.AttachStart(StandPoint, gameObject, true, false, false, ModSettings.AttachBool.Value, new Vector3(0f, 0.1f, 0f), null);
            if (!p.IsAttached()) { ModLog.Warn("Pump: AttachStart did not attach the player."); RestoreStand(); return false; }

            _user = p; _hasUser = true; Users[p] = this; _mountFrame = Time.frameCount;
            _exitRequested = false; _wantCycle = false; _cycleStopping = false; _queuedUntil = 0f; _noClipStrokeReadyAt = 0f; _ikWeight = 0f;
            _remote = null; _holdStart = -1f; _blockCycle = true;            // the E that mounted may still be held: it must not start continuous pumping
            SetPhase(Phase.Enter);                           // the lever's Enter animation starts together with the player's (StartPhase)
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
            _hasUser = false; _user = null; _phase = Phase.None; _phaseStarted = false;
            _exitRequested = false; _wantCycle = false; _cycleStopping = false; _ikWeight = 0f; _holdStart = -1f;
            if ((object)p != null) { Users.Remove(p); if (p != null) PlayerPumpAnimations.Release(p); }
            _autoStep = 0f; _autoRaise = 0f; _pendingNeed = 0f; RestoreStand();
            ReleaseZdo();                                    // other clients stop mirroring
            Set("pumping", false); _active = false; Set("active", false);
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
                else if (_remote != null) PlayerPumpAnimations.Release(_remote);
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
            if (_exitRequested || _phase == Phase.Exit || _phase == Phase.Cycle) return;
            if (!PlayerPumpAnimations.Ready)           // clips not detected: still pump, the lever animator alone cannot overlap itself
            {
                if (Time.time < _noClipStrokeReadyAt) return;
                if (TryStroke(_user)) { Stroke(); _noClipStrokeReadyAt = Time.time + StrokeLength(); }
                return;
            }
            if (_phase == Phase.Idle) { if (TryStroke(_user)) SetPhase(Phase.Push); }
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
        public void StartCycle() { if (_hasUser && _phase == Phase.Idle && TryStroke(_user) && !_lastDry) SetPhase(Phase.Cycle); }
        public void StopCycle() { _wantCycle = false; _blockCycle = true; }

        void SetPhase(Phase p) { _phase = p; _phaseStarted = false; }

        static string ClipOf(Phase p)
        {
            switch (p)
            {
                case Phase.Enter: return "pumpenter";
                case Phase.Idle: return "pumpidle";
                case Phase.Push: return "pumppush";
                case Phase.Pull: return "pumppull";
                case Phase.Cycle: return "pumpcycle";
                case Phase.Exit: return "pumpexit";
            }
            return null;
        }

        static float StrokeLength() { return Mathf.Max(0.1f, PlayerPumpAnimations.Length("pumppush") + PlayerPumpAnimations.Length("pumppull")); }

        // Starts a phase: the player's clip and the lever's animator parameter change in this same call, so both begin
        // on the same frame. (Enter: lever "active" -> Enter; Push: lever "pump" trigger -> Push then Pull by itself;
        // Cycle: lever "pumping" -> Cycle loop; Exit: lever "active" off -> Exit.)
        void StartPhase()
        {
            _phaseStarted = true;
            string clip = ClipOf(_phase);
            WritePhase();
            if (clip != null) PlayerPumpAnimations.Play(_user, clip);

            bool loops = _phase == Phase.Idle || _phase == Phase.Cycle;
            _phaseEnd = (clip == null) ? 0f : loops ? float.MaxValue : Time.time + Mathf.Max(0.1f, PlayerPumpAnimations.Length(clip));

            switch (_phase)
            {
                case Phase.Enter: SetActive(true); break;
                case Phase.Push: Stroke(); break;
                case Phase.Exit: SetActive(false); break;
                case Phase.Cycle:
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
            if (!_hasUser) { ObserverUpdate(); return; }
            UserUpdate();
        }

        // --- other players on the pump: mirror the ZDO -------------------------------------------------------------------
        // The lever follows DTP_active / DTP_pumping / DTP_pumpSeq, the mounted player's clips follow DTP_phase / seq
        // (same clips, started when the change arrives), hands get the same IK.
        void ObserverUpdate()
        {
            if (_mountPending && Time.time > _mountPendingUntil) { _mountPending = false; Tell(Player.m_localPlayer, Loc.T("dtp_msg_no_answer")); }
            if (PieceSetup.Headless) return;                    // dedicated server: nothing to show (mount arbitration is the RPC)
            var z = Zdo;
            if (z == null) return;
            long uid = z.GetLong(ZUser, 0L);
            var me = Player.m_localPlayer;
            bool occupied = uid != 0L && !(me != null && uid == me.GetPlayerID()) && IsAlive(uid);
            if (!occupied) { if (_remote != null || _seenPhaseSeq != -1 || _active) EndObserve(); return; }

            var pl = Player.GetPlayer(uid);
            if (pl != _remote) { if (_remote != null) PlayerPumpAnimations.Release(_remote); _remote = pl; _seenPhaseSeq = -1; }

            _active = z.GetBool(ZActive, false); Set("active", _active);
            Set("pumping", z.GetBool(ZPumping, false));
            int st = z.GetInt(ZdoStrokes, 0);
            if (_seenStrokes == -1) _seenStrokes = st;
            else if (st != _seenStrokes) { _seenStrokes = st; PumpFx.Stroke(_fxPoint); TarFlow.Stroke(_piece, true, (PumpLiquid)z.GetInt(ZPumpLiquid, (int)PumpLiquid.Tar)); }
            int sp = z.GetInt(ZSplashSeq, 0);
            if (_seenSplash == -1) _seenSplash = sp;
            else if (sp != _seenSplash)
            {
                _seenSplash = sp;
                var np = z.GetVec3(ZNozzle, Vector3.zero);
                if (np != Vector3.zero) PumpFx.PourWhenArrives(_piece, np, z.GetVec3(ZNozzleDir, Vector3.down), z.GetFloat(ZSplashVol, 1f), null, (PumpLiquid)z.GetInt(ZPumpLiquid, (int)PumpLiquid.Tar));       // the liquid coming out at the nozzle, when this client's copy of the slug gets there
            }
            int ps = z.GetInt(ZPumpSeq, 0);
            if (_seenPumpSeq == -1) _seenPumpSeq = ps;
            else if (ps != _seenPumpSeq) { _seenPumpSeq = ps; Set("pump", true); }

            int seq = z.GetInt(ZPhaseSeq, 0);
            if (_remote != null && seq != _seenPhaseSeq)
            {
                if (!PlayerPumpAnimations.Ready) PlayerPumpAnimations.TryDetect(_remote);
                if (PlayerPumpAnimations.Ready)
                {
                    _remotePhase = (Phase)z.GetInt(ZPhase, 0);
                    string clip = ClipOf(_remotePhase);
                    if (clip != null) PlayerPumpAnimations.Play(_remote, clip);
                    _seenPhaseSeq = seq;
                }
            }
        }

        void EndObserve()
        {
            if (_remote != null) PlayerPumpAnimations.Release(_remote);
            _remote = null; _seenPhaseSeq = -1; _seenPumpSeq = -1; _seenStrokes = -1; _seenSplash = -1; _remotePhase = Phase.None; _ikWeight = 0f;
            Set("pumping", false); _active = false; Set("active", false);
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
            if (_phaseStarted && _phase == Phase.Cycle && !_cycleStopping && Time.time >= _cycleCheckAt)
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
                    case Phase.Exit: FinishExit(); return;
                    case Phase.Push: SetPhase(Phase.Pull); break;                    // the lever goes Push -> Pull on its own
                    default: SetPhase(Phase.Idle); break;                            // Enter / Pull / Cycle finished
                }
            }

            if (_phase == Phase.Idle)       // ready for the next thing; whatever is picked starts in this same frame
            {
                if (_exitRequested) SetPhase(Phase.Exit);
                else if (_wantCycle) { if (TryStroke(_user) && !_lastDry) SetPhase(Phase.Cycle); else { _wantCycle = false; _blockCycle = true; } }
                else if (Time.time < _queuedUntil) { _queuedUntil = 0f; if (TryStroke(_user)) SetPhase(Phase.Push); }
            }

            if (!_phaseStarted) StartPhase();
        }

        void LateUpdate()
        {
            if (InteractPoint == null || !ModSettings.HandIK.Value) { ReleaseIk(); return; }
            Player who; Phase ph;
            if (_hasUser && _user != null) { who = _user; ph = _phase; }
            else if (_remote != null) { who = _remote; ph = _remotePhase; }
            else { _ikWeight = 0f; ReleaseIk(); return; }
            float target = (ph == Phase.None || ph == Phase.Exit) ? 0f : 1f;
            _ikWeight = Mathf.MoveTowards(_ikWeight, target, Time.deltaTime / Mathf.Max(0.05f, ModSettings.HandIKBlendSeconds.Value));
            if (_ikWeight > 0.001f)
            {
                float need = PumpHandIK.Apply(who, InteractPoint, _ikWeight); _ikPlayer = who;
                if (_hasUser && who == _user) AutoReach(need);
            }
            else ReleaseIk();
        }

        // --- stand position (height / forward / side) ---------------------------------------------------------------
        Vector3 StandPointBaseWorld()
        {
            if (StandPoint == null) return transform.position;
            return StandPoint.parent != null && _standBaseKnown ? StandPoint.parent.TransformPoint(_standBaseLocal) : StandPoint.position;
        }

        /// Puts stand_point at its prefab position + the configured offsets (+ the AutoReach lift). The mounted player
        /// is attached to stand_point, so moving it moves the player.
        void ApplyStand()
        {
            if (StandPoint == null) return;
            Vector3 side = Vector3.Cross(Vector3.up, _standFwd);       // the player's right while facing the lever
            float up = ModSettings.StandHeight.Value + _autoRaise;
            StandPoint.position = StandPointBaseWorld() + Vector3.up * up + _standFwd * (ModSettings.StandForward.Value + _autoStep) + side * ModSettings.StandSide.Value;
        }

        void RestoreStand()
        {
            if (StandPoint != null && _standBaseKnown) StandPoint.localPosition = _standBaseLocal;
        }

        // Hands can only stretch so far: if the handle (at the top of the stroke) is out of reach, step the player towards the
        // lever until it is, and only then lift him a little. Only ever grows. The player must not slide while the lever is being
        // pumped: during Push / Pull / Cycle the shortfall is only remembered (_pendingNeed) and applied in the calm Enter / Idle
        // phases, so after a stroke or two the position has settled and nothing moves any more.
        void AutoReach(float need)
        {
            if (!ModSettings.AutoReach.Value || StandPoint == null) return;
            if (_phase == Phase.Push || _phase == Phase.Pull || _phase == Phase.Cycle)
            {
                if (need > _pendingNeed) _pendingNeed = need;
                return;
            }
            if (_phase == Phase.Exit) return;
            need = Mathf.Max(need, _pendingNeed);
            if (need <= 0.005f) { _pendingNeed = 0f; return; }
            float d = Mathf.Min(need, 0.5f * Time.deltaTime);              // smooth, at most 0.5 m/s
            float stepRoom = ModSettings.MaxAutoStep.Value - _autoStep, raiseRoom = ModSettings.MaxAutoRaise.Value - _autoRaise;
            if (stepRoom > 0.001f) _autoStep += Mathf.Min(d, stepRoom);
            else if (raiseRoom > 0.001f) _autoRaise += Mathf.Min(d, raiseRoom);
            else { _pendingNeed = 0f; return; }                            // limits reached: nothing more to do
            _pendingNeed = Mathf.Max(0f, _pendingNeed - d);
            ApplyStand();
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
            if (zdo != null) zdo.Set(ZdoStrokes, zdo.GetInt(ZdoStrokes, 0) + 1);
            _lastDry = r.Dry;
            PumpFx.Stroke(_fxPoint);
            if (!r.SlugStarted) TarFlow.Stroke(_piece, false);      // dry stroke: the slug is shown, no pour follows it (a stroke that takes tar started its slug in DrainService.Stroke)
            if (r.Dry) Tell(user, r.Message);
            return true;
        }

        static void Tell(Humanoid user, string msg)
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
