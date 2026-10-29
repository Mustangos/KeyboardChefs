using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using InControl;
using UnityEngine;

// A virtual InControl gamepad driven by keyboard keys. Overcooked! 2 picks up every device
// InControl attaches (PCPadInputProvider.OnDeviceAttached), so this pad can engage as a player.
[BepInPlugin("local.keyboard2gamepad", "Keyboard2Gamepad", "1.0.0")]
public class Keyboard2GamepadPlugin : BaseUnityPlugin
{
    static readonly InputControlType[] Targets =
    {
        InputControlType.LeftStickUp, InputControlType.LeftStickDown, InputControlType.LeftStickLeft, InputControlType.LeftStickRight,
        InputControlType.Action1, InputControlType.Action2, InputControlType.Action3, InputControlType.Action4,
        InputControlType.LeftBumper, InputControlType.RightBumper, InputControlType.LeftTrigger, InputControlType.RightTrigger,
        InputControlType.DPadUp, InputControlType.DPadDown, InputControlType.DPadLeft, InputControlType.DPadRight,
        InputControlType.Start, InputControlType.Back,
    };

    static readonly KeyCode[] Defaults =
    {
        KeyCode.T, KeyCode.G, KeyCode.F, KeyCode.H,
        KeyCode.O, KeyCode.P, KeyCode.I, KeyCode.U,
        KeyCode.V, KeyCode.Comma, KeyCode.None, KeyCode.None,
        KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None,
        KeyCode.None, KeyCode.None,
    };

    readonly Dictionary<InputControlType, ConfigEntry<KeyCode>> keys = new Dictionary<InputControlType, ConfigEntry<KeyCode>>();
    ConfigEntry<KeyCode> toggleKey;
    ConfigEntry<bool> logKeys;
    ConfigEntry<bool> selfTest;
    KeyboardPad pad;
    bool running = true;
    bool gameKnowsPad;
    float nextGameCheck;
    bool engaged;
    bool selfTestA;

    const BindingFlags AnyStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    static readonly FieldInfo AllSets = typeof(PCPadInputProvider).GetField("m_allDevices", AnyStatic);
    static readonly FieldInfo EngagedSets = typeof(PCPadInputProvider).GetField("s_engagedPads", AnyStatic);

    void Awake()
    {
        for (int i = 0; i < Targets.Length; i++)
            keys[Targets[i]] = Config.Bind("Keys", Targets[i].ToString(), Defaults[i],
                "Keyboard key for this gamepad control (Unity KeyCode name, None = unused). Action1=A, Action2=B, Action3=X, Action4=Y.");
        toggleKey = Config.Bind("General", "ToggleKey", KeyCode.F8, "Starts/stops the virtual gamepad. Stopping releases all its controls.");
        logKeys = Config.Bind("General", "LogKeys", false, "Logs every key press with its KeyCode name, to find names for the [Keys] section.");
        selfTest = Config.Bind("General", "SelfTest", false, "Debug: pulses A on the virtual gamepad until Overcooked engages it. Keep off for play.");
        pad = new KeyboardPad(this);
        Logger.LogInfo("Loaded; waiting for InControl to set up.");
    }

    void Update()
    {
        if (InputManager.IsSetup && !pad.IsAttached)
        {
            InputManager.AttachDevice(pad);
            Logger.LogInfo("Virtual gamepad attached. InControl devices: " + InputManager.Devices.Count);
            LogKeyboardConflicts();
            gameKnowsPad = false;
        }

        if (pad.IsAttached && !gameKnowsPad && Time.unscaledTime >= nextGameCheck)
        {
            nextGameCheck = Time.unscaledTime + 1f;
            var sets = AllSets.GetValue(null) as List<StandardActionSet>;
            gameKnowsPad = sets != null && sets.Exists(s => s.Device == pad);
            if (gameKnowsPad) Logger.LogInfo("Overcooked registered the virtual gamepad (" + sets.Count + " input sets).");
        }

        if (gameKnowsPad)
        {
            bool nowEngaged = false;
            var engagedSets = EngagedSets.GetValue(null) as Dictionary<ControlPadInput.PadNum, StandardActionSet>;
            if (engagedSets != null)
                foreach (var kv in engagedSets)
                    if (kv.Value != null && kv.Value.Device == pad)
                    {
                        nowEngaged = true;
                        if (!engaged) Logger.LogInfo("Overcooked engaged the virtual gamepad as pad " + kv.Key + ".");
                    }
            if (engaged && !nowEngaged) Logger.LogInfo("Overcooked disengaged the virtual gamepad.");
            engaged = nowEngaged;
            selfTestA = selfTest.Value && !engaged && Time.unscaledTime % 2f < 0.3f;
        }

        if (Input.GetKeyDown(toggleKey.Value))
        {
            running = !running;
            Logger.LogInfo(running ? "Started." : "Stopped; all virtual gamepad controls released.");
        }

        if (logKeys.Value && Input.anyKeyDown)
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                if (Input.GetKeyDown(k)) Logger.LogInfo("Key pressed: " + k);
    }

    public bool IsPressed(InputControlType target)
    {
        if (selfTestA && target == InputControlType.Action1) return true;
        KeyCode key = keys[target].Value;
        return running && Application.isFocused && key != KeyCode.None && Input.GetKey(key);
    }

    // Our keys still reach the game's own keyboard handling, so warn when one is bound to a keyboard player.
    void LogKeyboardConflicts()
    {
        try
        {
            var ours = new HashSet<string>();
            foreach (var e in keys.Values) if (e.Value != KeyCode.None) ours.Add(e.Value.ToString());
            ours.Add(toggleKey.Value.ToString());

            foreach (bool split in new[] { true, false })
            {
                foreach (ControlPadInput.Button b in Enum.GetValues(typeof(ControlPadInput.Button)))
                    if (b != ControlPadInput.Button.Invalid)
                        Report(split, b.ToString(), PCPadInputProvider.GetBindings(b, split), ours);
                foreach (ControlPadInput.Value v in Enum.GetValues(typeof(ControlPadInput.Value)))
                    if (v != ControlPadInput.Value.Invalid)
                        foreach (bool positive in new[] { true, false })
                            Report(split, v + (positive ? "+" : "-"), PCPadInputProvider.GetBindings(v, positive, split), ours);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not read the game's keyboard bindings: " + ex.Message);
        }
    }

    void Report(bool split, string control, List<Key> bound, HashSet<string> ours)
    {
        if (bound == null || bound.Count == 0) return;
        var names = bound.ConvertAll(k => k.ToString());
        Logger.LogInfo((split ? "Split" : "Combined") + " keyboard " + control + ": " + string.Join(", ", names.ToArray()));
        foreach (var n in names)
            if (ours.Contains(n)) Logger.LogWarning("Key " + n + " is also bound to keyboard " + control + " (" + (split ? "split" : "combined") + ").");
    }

    class KeyboardPad : InputDevice
    {
        readonly Keyboard2GamepadPlugin owner;

        public KeyboardPad(Keyboard2GamepadPlugin owner) : base("Keyboard2Gamepad")
        {
            this.owner = owner;
            foreach (var t in Targets) AddControl(t, t.ToString());
        }

        public override bool IsKnown { get { return true; } }

        public override bool IsSupportedOnThisPlatform { get { return true; } }

        public override void Update(ulong updateTick, float deltaTime)
        {
            foreach (var t in Targets) GetControl(t).UpdateWithState(owner.IsPressed(t), updateTick, deltaTime);
        }
    }
}
