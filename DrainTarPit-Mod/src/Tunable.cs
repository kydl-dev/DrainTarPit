using System;
using Kydl.Common;

namespace DrainTarPit;

/// <summary>
/// T-068 (user, 2026-10-21, config review): a setting that is NOT in the player's config file. It has a fixed value chosen by the user; the code
/// reads it like a config entry (`.Value`), and the user's own tools mod (KydlTools) can change it at runtime for testing (SettingChanged fires
/// like a config entry's). Nothing is ever saved: every start begins with the fixed value.
/// </summary>
public sealed class Tunable<T>
{
    readonly T _fixed;
    T _value;

    public string Name { get; }
    public string Section { get; }
    public string Description { get; }
    public T Default { get { return _fixed; } }
    /// Set from another mod's config (WaterAndTarPools sets DrainTarPit's water values): KydlTools leaves it alone.
    public bool External { get; private set; }

    public Tunable(string section, string name, T value, string description = "")
    {
        Section = section; Name = name; Description = description ?? "";
        _fixed = value; _value = value;
    }

    /// Marks the value as set from another mod's config (External). Returns this Tunable.
    public Tunable<T> SetFromConfigOf(string mod)
    {
        External = true;
        ModLog.Verbose("Tunable: " + Section + "." + Name + " is set from the config of " + mod);
        return this;
    }

    public T Value
    {
        get { return _value; }
        set
        {
            if (Equals(_value, value)) return;
            var was = _value;
            _value = value;
            ModLog.Verbose("Tunable: " + Section + "." + Name + " = " + value + " (was " + was + ", fixed value " + _fixed + ")");
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// Back to the fixed value. Returns the value now in use.
    public T Reset() { Value = _fixed; return _value; }

    public event EventHandler SettingChanged;

    public override string ToString() { return Section + "." + Name + " = " + _value; }
}
