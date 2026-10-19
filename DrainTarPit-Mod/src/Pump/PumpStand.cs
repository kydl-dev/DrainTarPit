using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Where the mounted player stands: stand_point at its prefab position + the configured offsets (StandHeight / StandForward / StandSide)
    /// + the AutoReach shift. The player is attached to stand_point, so moving it moves the player.
    internal sealed class PumpStand
    {
        readonly Transform _point;          // stand_point
        readonly Transform _lever;          // interact_point, the lever handle
        readonly Vector3 _baseLocal;        // stand_point's local position as authored in the prefab
        readonly bool _baseKnown;
        Vector3 _fwd = Vector3.forward;     // horizontal direction player -> lever, fixed when mounting
        float _pendingNeed;                 // AutoReach shortfall seen during a stroke, applied when the pose is calm
        float _autoStep, _autoRaise;        // AutoReach: extra metres towards the lever / up so both hands reach the handle

        public PumpStand(Transform point, Transform lever)
        {
            _point = point; _lever = lever;
            if (point != null) { _baseLocal = point.localPosition; _baseKnown = true; }
        }

        Vector3 BaseWorld()
        {
            if (_point == null) return Vector3.zero;
            return _point.parent != null && _baseKnown ? _point.parent.TransformPoint(_baseLocal) : _point.position;
        }

        /// Turns the stand point towards the lever, forgets the old AutoReach shift and moves it to the configured spot.
        public void Begin()
        {
            if (ModSettings.FacePump.Value && _lever != null)
            {
                var dir = _lever.position - _point.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    _point.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, ModSettings.StandYawOffset.Value, 0f);
            }
            ResetAuto();
            if (_lever != null && _point != null)
            {
                var toLever = _lever.position - BaseWorld(); toLever.y = 0f;
                if (toLever.sqrMagnitude > 0.0001f) _fwd = toLever.normalized;
            }
            Apply();
        }

        /// The player left: back to the prefab position.
        public void End() { ResetAuto(); Restore(); }

        void ResetAuto() { _autoStep = 0f; _autoRaise = 0f; _pendingNeed = 0f; }

        public void Restore()
        {
            if (_point != null && _baseKnown) _point.localPosition = _baseLocal;
        }

        void Apply()
        {
            if (_point == null) return;
            Vector3 side = Vector3.Cross(Vector3.up, _fwd);       // the player's right while facing the lever
            float up = ModSettings.StandHeight.Value + _autoRaise;
            _point.position = BaseWorld() + Vector3.up * up + _fwd * (ModSettings.StandForward.Value + _autoStep) + side * ModSettings.StandSide.Value;
        }

        /// Hands can only stretch so far: if the handle (at the top of the stroke) is out of reach, step the player towards the lever until it
        /// is, and only then lift him a little. Only ever grows. The player must not slide while pumping: during Push / Pull / Cycle the shortfall
        /// is only remembered and applied in the calm Enter / Idle phases, so after a stroke or two nothing moves any more.
        public void AutoReach(float need, PumpPhase phase)
        {
            if (!ModSettings.AutoReach.Value || _point == null) return;
            if (phase == PumpPhase.Push || phase == PumpPhase.Pull || phase == PumpPhase.Cycle)
            {
                if (need > _pendingNeed) _pendingNeed = need;
                return;
            }
            if (phase == PumpPhase.Exit) return;
            need = Mathf.Max(need, _pendingNeed);
            if (need <= 0.005f) { _pendingNeed = 0f; return; }
            float d = Mathf.Min(need, 0.5f * Time.deltaTime);              // smooth, at most 0.5 m/s
            float stepRoom = ModSettings.MaxAutoStep.Value - _autoStep, raiseRoom = ModSettings.MaxAutoRaise.Value - _autoRaise;
            if (stepRoom > 0.001f) _autoStep += Mathf.Min(d, stepRoom);
            else if (raiseRoom > 0.001f) _autoRaise += Mathf.Min(d, raiseRoom);
            else { _pendingNeed = 0f; return; }                            // limits reached: nothing more to do
            _pendingNeed = Mathf.Max(0f, _pendingNeed - d);
            Apply();
        }
    }
}
