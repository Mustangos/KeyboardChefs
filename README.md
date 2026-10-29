# Keyboard2Gamepad

A third Overcooked! 2 player on a Mac with no gamepad: a small BepInEx plugin adds a virtual
gamepad inside the game and drives it with keyboard keys. Players 1 and 2 keep the game's own
split-keyboard controls.

Personal, single-machine setup. Tested on a MacBook Pro M4, macOS 26.6.2, Overcooked! 2 from Steam
(Unity 2017.4.8, x86_64 under Rosetta), BepInEx 5.4.23.5.

## Why not a real system-wide virtual gamepad

The original goal was a virtual HID gamepad that macOS and Steam see. That is not possible on a
stock Mac without Apple's approval:

- Creating a virtual HID device (`IOHIDUserDeviceCreateWithProperties`, CoreHID `HIDVirtualDevice`)
  requires the entitlement `com.apple.developer.hid.virtual.device`.
- Root does not bypass it: `IOHIDResourceDeviceUserClient::initWithTask` in Apple's IOHIDFamily
  source checks only for that entitlement (or an Apple-internal one), never for root.
- The entitlement is restricted. A binary that claims it without an Apple-issued provisioning
  profile is killed by AMFI at launch.
- Karabiner's signed DriverKit extension only offers a virtual keyboard and pointing device.
- The remaining route, disabling SIP and AMFI, weakens the whole Mac and was ruled out.

Tested on this Mac:

| Test | Result |
|---|---|
| `IOHIDUserDeviceCreateWithProperties` as a normal user | returns NULL |
| same binary ad-hoc signed with the entitlement | killed at launch (exit 137, SIGKILL) |

Overcooked! 2 reads controllers through InControl, which supports devices added in code
(`InputManager.AttachDevice`), and the game registers every device InControl attaches
(`PCPadInputProvider.OnDeviceAttached`). So the virtual gamepad lives inside the game instead.
Steam and macOS never see it.

## Requirements

- Overcooked! 2 from Steam
- .NET SDK (any recent version) to compile the plugin, e.g. without sudo:

  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel LTS --install-dir ~/.dotnet
  ```

## Install

```bash
./install.sh
```

It downloads BepInEx 5.4.23.5 for macOS (checksum-verified) into the game folder once, compiles
`Keyboard2Gamepad.cs` against the game's own assemblies and copies the DLL to
`BepInEx/plugins/`. Re-run it after editing the source. Set `OC2_DIR` if the game lives elsewhere.

Then, once, in Steam: Overcooked! 2 > Properties > Launch Options, paste the line `install.sh` prints:

```
"/Users/<you>/Library/Application Support/Steam/steamapps/common/Overcooked! 2/run_bepinex.sh" %command%
```

Without it the game starts without the plugin. Launching `run_bepinex.sh` directly also does not
work: the game restarts itself through Steam and drops the mod (unless `SteamAppId=728880` is set).

## Default keys

| Gamepad | Key | Overcooked! 2 |
|---|---|---|
| Left stick up / left / down / right | `T` / `F` / `G` / `H` | move |
| A | `O` | pick up / put down |
| X | `I` | chop / throw |
| B | `P` | dash |
| Y | `U` | emote (unverified) |
| LB / RB | `V` / `,` | |
| Start/stop the virtual gamepad | `F8` (`fn+F8` on a MacBook) | |

The game's split keyboard uses WASD, E, T, Tab, Left Shift/Control/Alt (player 1) and the arrows,
Right Shift, Right Alt, `/`, `I`, `P`, keypad Enter (player 2). `T`, `I` and `P` are therefore
shared with the keyboard players (T = player 1 left stick press, I = player 2 Y, P = player 2
right stick press); rebind those in the game's keyboard options to separate them.

## Configure

Edit `BepInEx/config/local.keyboard2gamepad.cfg` in the game folder (created on first launch).
Values are Unity `KeyCode` names; `None` disables a control. Set `LogKeys = true` to log the name
of every key you press. Restart the game after changes.

The plugin does not hide its keys from the game. On start it logs the game's keyboard bindings
and warns about any key that both uses.

`SelfTest = true` makes the virtual gamepad pulse A by itself until the game engages it (at the
title screen it becomes the first player). It checks the plugin without anyone at the keyboard;
keep it off for play.

## Logs

`BepInEx/LogOutput.log` in the game folder. Lines to look for:

- `Virtual gamepad attached.`: InControl has the device.
- `Overcooked registered the virtual gamepad`: the game sees it as a pad.
- `Key X is also bound to keyboard ...`: a key conflict.
- `Overcooked engaged the virtual gamepad as pad ...`: the pad joined as a player.

## Test results (2026-10-29)

Automated, game launched with `SteamAppId=728880 ./run_bepinex.sh`:

| Check | Result |
|---|---|
| BepInEx loads under Rosetta on macOS 26.6.2 | yes, `Chainloader startup complete` |
| Plugin attaches the device to InControl | yes, `Virtual gamepad attached. InControl devices: 1` |
| Game creates an input set for it | yes, `Overcooked registered the virtual gamepad (2 input sets)` |
| Virtual A press engages it as a player | yes, `Overcooked engaged the virtual gamepad as pad One` (SelfTest, title screen) |
| Earlier defaults (`UHJK`, `N B M G`) free of the game's bindings | yes, no conflict warnings |

Needs a person at the keyboard (not verified yet):

- launching through the Steam launch option,
- three players joining a local game (two on split keyboard, one on these keys),
- moving, picking up, chopping and throwing with all three at once.

## Uninstall

Remove the Steam launch option. To remove everything, delete `BepInEx/`, `run_bepinex.sh`,
`libdoorstop.dylib`, `.doorstop_version` and `changelog.txt` from the game folder.

## Known limitations

- Works only in Overcooked! 2; Steam, macOS and other games do not see the gamepad.
- Mapped keys are not suppressed; pick keys the game's keyboard players do not use.
- Keys are read by Unity, so they work only while the game window is focused. Losing focus or
  pressing the toggle key releases every control.
- Key names follow Unity's `KeyCode`; on non-US layouts verify them with `LogKeys`.
- A game update that changes InControl or `PCPadInputProvider` can break the plugin.
