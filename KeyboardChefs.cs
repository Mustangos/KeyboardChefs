using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using InControl;
using UnityEngine;

// Virtual InControl gamepads driven by keyboard keys, with an in-game menu to enable them and rebind
// keys. Overcooked! 2 picks up every device InControl attaches (PCPadInputProvider.OnDeviceAttached),
// so each pad can engage as a player.
[BepInPlugin("keyboardchefs", "KeyboardChefs", "1.1.0")]
public class KeyboardChefsPlugin : BaseUnityPlugin
{
    const int PadCount = 3;

    static readonly InputControlType[] Targets =
    {
        InputControlType.LeftStickUp, InputControlType.LeftStickDown, InputControlType.LeftStickLeft, InputControlType.LeftStickRight,
        InputControlType.Action1, InputControlType.Action2, InputControlType.Action3, InputControlType.Action4,
        InputControlType.LeftBumper, InputControlType.RightBumper, InputControlType.LeftTrigger, InputControlType.RightTrigger,
        InputControlType.DPadUp, InputControlType.DPadDown, InputControlType.DPadLeft, InputControlType.DPadRight,
        InputControlType.Start, InputControlType.Back,
    };

    static readonly string[] Labels =
    {
        "Move up", "Move down", "Move left", "Move right",
        "A  pick up / put down", "B  dash", "X  chop / throw", "Y  emote",
        "LB", "RB", "LT", "RT",
        "D-pad up", "D-pad down", "D-pad left", "D-pad right",
        "Start  pause", "Back",
    };

    static readonly KeyCode[][] Defaults =
    {
        new[]
        {
            KeyCode.T, KeyCode.G, KeyCode.F, KeyCode.H,
            KeyCode.O, KeyCode.P, KeyCode.I, KeyCode.U,
            KeyCode.V, KeyCode.Comma, KeyCode.None, KeyCode.None,
            KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None,
            KeyCode.None, KeyCode.None,
        },
        new[]
        {
            KeyCode.Keypad8, KeyCode.Keypad5, KeyCode.Keypad4, KeyCode.Keypad6,
            KeyCode.Keypad0, KeyCode.KeypadPlus, KeyCode.KeypadPeriod, KeyCode.KeypadMinus,
            KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None,
            KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None,
            KeyCode.None, KeyCode.None,
        },
        new KeyCode[18],
    };

    const BindingFlags AnyStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    static readonly FieldInfo AllSets = typeof(PCPadInputProvider).GetField("m_allDevices", AnyStatic);
    static readonly FieldInfo EngagedSets = typeof(PCPadInputProvider).GetField("s_engagedPads", AnyStatic);

    static bool inputBlocked;

    readonly KeyboardPad[] pads = new KeyboardPad[PadCount];
    ConfigEntry<KeyCode> menuKey;
    ConfigEntry<bool> showMenuOnStart;
    ConfigEntry<bool> logKeys;
    ConfigEntry<bool> selfTest;
    Dictionary<string, string> gameKeys;
    bool menuOpen;
    bool cursorWasVisible;
    CursorLockMode cursorWasLocked;
    int listenPad = -1;
    int listenControl = -1;
    Rect window = new Rect(40, 40, 0, 0);
    float hintUntil;
    bool selfTestShot;

    void Awake()
    {
        menuKey = Config.Bind("General", "MenuKey", KeyCode.F1, "Shows/hides the KeyboardChefs menu (fn+F1 on a MacBook).");
        showMenuOnStart = Config.Bind("General", "ShowMenuOnStart", false, "Opens the menu when the game starts. Off: a hint names the menu key for 10 seconds.");
        logKeys = Config.Bind("General", "LogKeys", false, "Logs every key press with its KeyCode name.");
        selfTest = Config.Bind("General", "SelfTest", false, "Debug: pulses A on pad 1 until Overcooked engages it and saves BepInEx/selftest.png. Keep off for play.");
        for (int i = 0; i < PadCount; i++) pads[i] = new KeyboardPad(Config, i + 1);
        SetMenu(showMenuOnStart.Value);
        Logger.LogInfo("Loaded; waiting for InControl to set up.");
    }

    void Update()
    {
        inputBlocked = menuOpen || !Application.isFocused;

        if (InputManager.IsSetup)
        {
            if (hintUntil == 0f) hintUntil = Time.unscaledTime + 10f;
            foreach (var pad in pads) SyncPad(pad);
        }

        if (listenControl >= 0) ListenForKey();
        else if (Input.GetKeyDown(menuKey.Value)) SetMenu(!menuOpen);

        if (menuOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (logKeys.Value && Input.anyKeyDown)
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                if (Input.GetKeyDown(k)) Logger.LogInfo("Key pressed: " + k);

        if (selfTest.Value && !selfTestShot && Time.unscaledTime > 8f)
        {
            selfTestShot = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath, "selftest.png"));
            Logger.LogInfo("SelfTest screenshot saved.");
        }
    }

    void SyncPad(KeyboardPad pad)
    {
        if (pad.Enabled.Value != pad.IsAttached)
        {
            if (pad.Enabled.Value) InputManager.AttachDevice(pad);
            else InputManager.DetachDevice(pad);
            pad.GameKnows = pad.Engaged = false;
            Logger.LogInfo("Pad " + pad.Number + (pad.IsAttached ? " attached." : " detached."));
            if (gameKeys == null) ReadGameKeys();
        }
        if (!pad.IsAttached) return;

        if (!pad.GameKnows && Time.unscaledTime >= pad.NextCheck)
        {
            pad.NextCheck = Time.unscaledTime + 1f;
            var sets = AllSets.GetValue(null) as List<StandardActionSet>;
            pad.GameKnows = sets != null && sets.Exists(s => s.Device == pad);
            if (pad.GameKnows) Logger.LogInfo("Overcooked registered pad " + pad.Number + ".");
        }
        if (!pad.GameKnows) return;

        bool engaged = false;
        var engagedSets = EngagedSets.GetValue(null) as Dictionary<ControlPadInput.PadNum, StandardActionSet>;
        if (engagedSets != null)
            foreach (var kv in engagedSets)
                if (kv.Value != null && kv.Value.Device == pad)
                {
                    engaged = true;
                    if (!pad.Engaged) Logger.LogInfo("Overcooked engaged pad " + pad.Number + " as " + kv.Key + ".");
                }
        if (pad.Engaged && !engaged) Logger.LogInfo("Overcooked disengaged pad " + pad.Number + ".");
        pad.Engaged = engaged;
        pad.SelfTestA = selfTest.Value && pad.Number == 1 && !engaged && Time.unscaledTime % 2f < 0.3f;
    }

    void SetMenu(bool open)
    {
        if (open && !menuOpen)
        {
            hintUntil = -1f;
            cursorWasVisible = Cursor.visible;
            cursorWasLocked = Cursor.lockState;
        }
        else if (!open && menuOpen)
        {
            Cursor.visible = cursorWasVisible;
            Cursor.lockState = cursorWasLocked;
            listenControl = -1;
        }
        menuOpen = open;
    }

    void ListenForKey()
    {
        if (!Input.anyKeyDown) return;
        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
        {
            // Mouse and joystick codes start at Mouse0.
            if (k >= KeyCode.Mouse0 || !Input.GetKeyDown(k)) continue;
            if (k != KeyCode.Escape)
                pads[listenPad].Keys[listenControl].Value = k == KeyCode.Backspace ? KeyCode.None : k;
            listenControl = -1;
            return;
        }
    }

    // Our keys still reach the game's own keyboard handling, so collect the game's bindings to flag shared keys.
    void ReadGameKeys()
    {
        gameKeys = new Dictionary<string, string>();
        try
        {
            foreach (bool split in new[] { true, false })
            {
                string mode = split ? "split " : "combined ";
                foreach (ControlPadInput.Button b in Enum.GetValues(typeof(ControlPadInput.Button)))
                    if (b != ControlPadInput.Button.Invalid)
                        AddGameKeys(mode + b, PCPadInputProvider.GetBindings(b, split));
                foreach (ControlPadInput.Value v in Enum.GetValues(typeof(ControlPadInput.Value)))
                    if (v != ControlPadInput.Value.Invalid)
                        foreach (bool positive in new[] { true, false })
                            AddGameKeys(mode + v + (positive ? "+" : "-"), PCPadInputProvider.GetBindings(v, positive, split));
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not read the game's keyboard bindings: " + ex.Message);
        }
        for (int p = 0; p < PadCount; p++)
            if (pads[p].Enabled.Value)
                for (int c = 0; c < Targets.Length; c++)
                {
                    string conflict = Conflict(p, c);
                    if (conflict != null) Logger.LogWarning(conflict);
                }
    }

    void AddGameKeys(string control, List<Key> keys)
    {
        if (keys == null) return;
        foreach (var k in keys)
        {
            string name = k.ToString();
            Logger.LogInfo("Game keyboard " + control + ": " + name);
            string existing;
            gameKeys[name] = gameKeys.TryGetValue(name, out existing) ? existing + ", " + control : control;
        }
    }

    string Conflict(int p, int c)
    {
        KeyCode key = pads[p].Keys[c].Value;
        if (key == KeyCode.None) return null;
        for (int q = 0; q < PadCount; q++)
            for (int d = 0; d < Targets.Length; d++)
                if ((q != p || d != c) && pads[q].Enabled.Value && pads[q].Keys[d].Value == key)
                    return "Pad " + (p + 1) + " " + Labels[c] + ": " + key + " is also pad " + (q + 1) + " " + Labels[d] + ".";
        string game;
        if (gameKeys != null && gameKeys.TryGetValue(key.ToString(), out game))
            return "Pad " + (p + 1) + " " + Labels[c] + ": " + key + " is also the game's keyboard " + game + ".";
        return null;
    }

    void OnGUI()
    {
        float scale = Mathf.Max(1f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        if (!menuOpen)
        {
            if (Time.unscaledTime < hintUntil)
            {
                var hint = new Rect(20, 20, 320, 28);
                DrawSolid(hint);
                GUI.Label(hint, "  KeyboardChefs: press " + menuKey.Value + " to set up keyboard pads");
            }
            return;
        }
        // The default window skin is see-through; back it with a solid rect from the last frame's layout.
        DrawSolid(window);
        window = GUILayout.Window(0x4B43, window, DrawWindow, "KeyboardChefs: keyboard gamepads");
    }

    static void DrawSolid(Rect rect)
    {
        GUI.color = new Color(0.13f, 0.13f, 0.13f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(160));
        foreach (var pad in pads)
        {
            bool on = GUILayout.Toggle(pad.Enabled.Value, " Pad " + pad.Number + (pad.Engaged ? " (playing)" : ""), GUILayout.Width(130));
            if (on != pad.Enabled.Value) pad.Enabled.Value = on;
        }
        GUILayout.EndHorizontal();

        for (int c = 0; c < Targets.Length; c++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Labels[c], GUILayout.Width(160));
            for (int p = 0; p < PadCount; p++)
            {
                KeyCode key = pads[p].Keys[c].Value;
                bool listening = listenPad == p && listenControl == c;
                string conflict = listening ? null : Conflict(p, c);
                GUI.color = listening ? Color.cyan : conflict != null ? Color.yellow : Color.white;
                string text = listening ? "press a key..." : key == KeyCode.None ? "-" : key.ToString();
                if (GUILayout.Button(new GUIContent(text, conflict), GUILayout.Width(130)))
                {
                    listenPad = p;
                    listenControl = c;
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);
        GUILayout.Label(GUI.tooltip != "" ? GUI.tooltip : "Click a key, then press the new one. Esc cancels, Backspace clears. Yellow = key used twice.");
        GUILayout.Label("Pads join like controllers: close this menu and press A in the game's lobby.");
        GUILayout.BeginHorizontal();
        GUILayout.Label(menuKey.Value + " shows/hides this menu.");
        if (GUILayout.Button("Close", GUILayout.Width(90))) SetMenu(false);
        GUILayout.EndHorizontal();

        // Unity 2017 places the mouse above the real cursor on notched MacBooks; mark where clicks land.
        if (Application.platform == RuntimePlatform.OSXPlayer && Event.current.type == EventType.Repaint)
        {
            Vector2 m = Event.current.mousePosition;
            GUI.DrawTexture(new Rect(m.x - 6, m.y - 0.5f, 13, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(m.x - 0.5f, m.y - 6, 1, 13), Texture2D.whiteTexture);
        }
        GUI.DragWindow();
    }

    class KeyboardPad : InputDevice
    {
        public readonly int Number;
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<KeyCode>[] Keys = new ConfigEntry<KeyCode>[Targets.Length];
        public bool GameKnows;
        public bool Engaged;
        public bool SelfTestA;
        public float NextCheck;

        public KeyboardPad(ConfigFile config, int number) : base("KeyboardChefs Pad " + number)
        {
            Number = number;
            string section = "Pad" + number;
            Enabled = config.Bind(section, "Enabled", number == 1, "Attaches this virtual gamepad to the game.");
            for (int i = 0; i < Targets.Length; i++)
            {
                AddControl(Targets[i], Targets[i].ToString());
                Keys[i] = config.Bind(section, Targets[i].ToString(), Defaults[number - 1][i], "Key for " + Labels[i] + " (Unity KeyCode name, None = unused).");
            }
        }

        public override bool IsKnown { get { return true; } }

        public override bool IsSupportedOnThisPlatform { get { return true; } }

        public override void Update(ulong updateTick, float deltaTime)
        {
            for (int i = 0; i < Targets.Length; i++)
            {
                KeyCode key = Keys[i].Value;
                bool down = !inputBlocked && key != KeyCode.None && Input.GetKey(key);
                if (SelfTestA && Targets[i] == InputControlType.Action1) down = true;
                GetControl(Targets[i]).UpdateWithState(down, updateTick, deltaTime);
            }
        }
    }
}
