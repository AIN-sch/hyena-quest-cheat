# Hyena Quest Cheat (Delivery & Beyond)

> ⚠️ **Disclaimer**
>
> This plugin is for **learning and research purposes only** (BepInEx plugin development, Unity Netcode network mechanics).
> It must **not** be used for commercial purposes, to disrupt other players' experience, or in violation of any game/platform terms of service.
>
> - Any consequences arising from using, modifying, or redistributing this plugin (bans, losses, disputes, etc.) are **entirely the user's own responsibility**
> - The author is not liable for any direct or indirect damages, nor for any legal consequences
> - Do not use it in contexts that require fairness (tournaments, leaderboards, etc.)
> - Please retain this notice when redistributing
>
> Downloading and using this means you agree to the above. **Your actions, your problem.**

A BepInEx 5 plugin for *Delivery & Beyond* (in-game: *Hyena Quest*).
The game ships with BepInEx already — just drop the compiled `HyenaQuestCheat.dll`
into `<game folder>\BepInEx\plugins\`, restart the game, and press **INS** to toggle the menu (on by default).

Source is open. Take it, mod it, do whatever you want with it.

## Features

Everything that works, grouped by use:

- **Auto-farm**: one-click idle play (auto-win, host only) · one-click delivery (dial + grab + drop loop) · one-click vacuum (suck up every scrap on the map, can fill instantly) · auto-recycle to ship when the bag is full (host)
- **Survival**: god mode (health never drops below 1) · move speed multiplier (1–10x, smooth ramp)
- **Annoy others**: instant kill / re-kill (breaks D-SAFE) · shove / rapid shove · pin health (1 HP / full / loop) · steal items to your feet · force drop items · break glass · teleport players (host) · revive · suicide (manual only)
- **Fun**: spinning anti-aim (spin / offset / bow) · fly / noclip · chat spam
- **Menu**: INS to toggle · rebindable hotkeys (keyboard / mouse) · draggable and resizable

## Known Bugs

Honest answer: I can't test every feature on every machine in every match. There are bugs, for sure. Ones I know of:

- **Anti-aim is buggy.** What exactly? Can't be bothered to dig into it, can't be bothered to fix it. It works well enough — just use it.
- **One-click idle play** is a pipeline — if any step breaks (network, map not fully loaded), it might stall. There's a timeout fallback, but no guarantees.
- **Fly / noclip** interact with the physics system; you may occasionally get bounced back when passing through terrain quickly.

Anything else — let me know when you hit it. You can ping me in the group below.

## My Attitude

It's a small game. Not worth a lot of effort. **Whether a bug gets fixed is purely down to whether I feel like playing**: if it annoys me enough, I'll fix it; otherwise I'll leave it. If it works, it works. Don't expect support.

The UI is Unity's built-in IMGUI, thrown together. It looks crude on purpose — I'm too lazy to polish it.

## Modding / Forks

Source lives in `src/`. Open **`HyenaQuestCheat.sln`** directly in Visual Studio and go to town.
You'll need the game installed to build (the csproj references assemblies in the game folder); run `build.bat` or F5 in VS.
Use it however you like, distribute it however you like.

Want to understand the underlying exploit mechanics? See **[VULNS.md](VULNS.md)** — every vulnerability point and interface call is documented there (in Chinese).

## Star ⭐

This little plugin took a fair bit of time and thought to write. If it helps you, a star would be the best encouragement I could get. Thanks in advance.

## Tech Discussion

QQ group: **1102821216**
When joining, you **must** state where you came from (e.g. "saw it on GitHub"), otherwise your request won't be approved — for the group's safety.

## Build

```
build.bat
```

Needs dotnet SDK (8 or 9). The first build downloads `Microsoft.NETFramework.ReferenceAssemblies` automatically (needs internet once).
Or open `HyenaQuestCheat.sln` in Visual Studio and build there.

## Changelog

- **v1.2.6**: Added fly and noclip; source open-sourced, added .sln project
- **v1.2.1**: Fixed the loop-kill / rapid-shove crash that froze the whole lobby; fixed the one-click delivery infinite loop re-delivering the same order
- **v1.2.0**: Initial release
