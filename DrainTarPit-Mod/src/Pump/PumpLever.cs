using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// The pump prefab's lever Animator: sets a parameter whatever its declared type is (bool or trigger) and warns once about a missing one.
    internal sealed class PumpLever
    {
        readonly Animator _anim;

        // Animator.parameters allocates a new array on every access and Set runs several times per frame, so the declared types are cached.
        readonly Dictionary<string, AnimatorControllerParameterType> _paramTypes = new();
        readonly HashSet<string> _paramWarned = new();
        RuntimeAnimatorController _paramsFor;

        public PumpLever(Animator anim) { _anim = anim; }

        public void Set(string param, bool value)
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

        public string Describe()
        {
            if (_anim == null) return "no Animator";
            var s = _anim.runtimeAnimatorController == null ? "no controller" : _anim.runtimeAnimatorController.name;
            foreach (var p in _anim.parameters) s += " [" + p.name + ":" + p.type + "]";
            return s;
        }
    }
}
