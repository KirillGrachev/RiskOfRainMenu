# RiskOfRainMenu

One dll, host-authoritative sandbox menu for Risk of Rain 2: nine tabs over
the live game catalogs (unlocks, clones, characters, items, drops, stage
interactables, per-player cheats, run control), a client-side ESP overlay and
free flight, and a CI that compiles every push against the real game assembly,
so a game update fails the build instead of your lobby.

The menu is IMGUI drawn by the plugin itself: no asset bundle, no R2API, no
game prefabs. Every world write runs on the server through one request
contract; a client whose host does not run the mod still keeps the local
halves (ESP, flight, catalog browsing) because they never touch the world.

---

## Why

Trainer-grade mods for RoR2 tend to break in the same places:

| What a naive trainer does | How it ends |
|---|---|
| Patches by name and hopes | A game update renames a method and the cheat silently never fires |
| Runs everything on the pressing client | Desynced lobbies, kicked players, doubled items |
| Ships game binaries in the repo or the release | Takedown risk, and a stale dll nobody trusts |
| Keeps cheat state in one god object | No way to tell mid-run why a flag stopped applying |

This plugin pins each of those down: Harmony targets are verified at startup
and re-verified by tests against the real `RoR2.dll`; world writes are
server-side with every numeric field clamped; the game assembly lives only in
the Actions cache of this repository; per-player state is a pure
`CheatState` store that unit tests drive without Unity.

---

## Tabs

| Tab | What it touches |
|---|---|
| Unlocks | Profile unlocks granted and revoked reversibly, counts refreshed on open |
| Clones | Per-player clone groups: follow, copy inventory, copy loadout, cap 50, auto-respawn off by default |
| Characters | Survivor catalog browser and body swap, validated server-side through `BodyCatalog` |
| Items | Item catalog with search, give/remove stacks, give-all and clear behind two-click confirms |
| Drops | Pickup catalog: drop any pickup as a droplet near the target, plus cleanup of menu drops |
| Props | Live interactables of the stage (chests, shrines, barrels) from spawn card instances |
| Info | Run and per-player summary, runtime self-check warnings |
| Cheats | God mode per player and for all, one-hit kill, no cooldowns, damage/attack/move multipliers 10..1000%, heal, kill by radius, buffs, money, lunar and void coins, team level, flight and ESP toggles |
| Run | Timer freeze and set, difficulty steps including Eclipse, stage jump, teleporter charge, artifact toggles, player teleports, landmark teleports with three waypoint slots, game speed |

---

## Install

Requirements: Risk of Rain 2 with BepInEx 5 x64. CI compiles against the game
build of 2026-10-18 (v5.4.23.4+4); other builds work until the API check in
CI says otherwise.

```bash
# 1. Take the dll from the continuous release and drop it into the plugins folder.
#    The release carries SHA256SUMS.txt next to the dll; check it.
cp RoR2SandboxMenu.dll "<game>/BepInEx/plugins/"

# 2. Optional: the ru/en override table. Without it the plugin creates an empty
#    file on first start and falls back to the built-in pairs.
cp plugin/lang/lang.tsv "<game>/BepInEx/plugins/lang/"
```

The menu opens with `Delete` (`General.MenuKey`). The same key closes it,
except while the search field has focus: typing a query would otherwise
collapse the window.

---

## Menu behavior

- Window state persists in the config: last tab, collapsed flag, position.
  Collapsed is a one-line title bar, usable mid-fight.
- `General.MenuScale` (1.0..2.0) draws the whole menu in virtual coordinates
  for HiDPI and 4K.
- The status line shows run, role (server/client) and the selected target;
  the target selector lists every connected player.
- Destructive buttons arm on the first click ("SURE to clear?") and fire on
  the second within 4 s: clear inventory, give all, kill enemies.
- While the menu is open the OS cursor is forced visible, unlocked and
  hardware-drawn, so it stays above the window; closing restores exactly the
  cursor state the game had.

## Network contract

- One `SandboxRequest` struct, `ProtoVersion` 1, HLAPI messages
  `General.MessageBaseId`..+3 (default 31337..31340, move it if another mod
  occupies the range).
- The server clamps every numeric field (money, radius, stacks, timer,
  percent ranges); dual-meaning fields are clamped by the executor that
  understands them.
- Rate limit: 25 requests per 5 s per sender.
- `Network.RemoteTargets`: `any` (default) or `self` - who a client may act on.
- Clients keep an optimistic mirror of flags and sliders so the UI does not
  flip back between frames; the server stays the authority.
- World actions are refused with a chat line when the host lacks the mod.
  ESP and flight are render- and input-only and work regardless.

## Runtime self-checks

- `VerifyHarmonyTargets` at startup: every Harmony target must resolve and be
  patched; misses go to chat, `BepInEx/LogOutput.log` and the Info tab instead
  of degrading silently.
- `GameRefsTests` in CI reads the real `RoR2.dll` through
  `MetadataLoadContext` and asserts patch targets, reflection chains and
  catalog counters. 31 tests; the refs fixture skips without `GAME_REFS_DIR`,
  so a local `dotnet test` without the game stays green.
- Every per-tick body is wrapped: a misbehaving target produces a warning
  line, never a game crash.

---

## Builds and releases

- `build.yml` compiles every push to `main` against the game assembly restored
  from the Actions cache (key `ror2-managed-v1-<sha256 of RoR2.dll>`) and
  republishes the continuous `latest` release with the dll and
  `SHA256SUMS.txt`. That compile is the API check: a renamed game method
  fails here, not in your lobby.
- `v*` tags produce numbered releases; the dll version is injected from the
  tag, so the file version cannot drift from the release name.
- `sync-game-dll.yml` seeds the cache three ways: a self-hosted runner with
  the game folder, an `ROR2_REFS_URL` secret, or `refs.zip` attached to an
  unpublished draft release (tag `game-refs`). The game binaries never enter
  the repository, the artifacts or the releases.

## Configuration

`BepInEx/config/local.ror2.sandboxmenu.cfg`:

| Key | Default | Meaning |
|---|---|---|
| `General.MenuKey` | `Delete` | Menu toggle |
| `General.Language` | `ru` | Menu and chat language, `ru` or `en` |
| `General.MenuScale` | `1.0` | UI scale, 1.0..2.0 |
| `General.ChatMessages` | `true` | One short chat line per action |
| `General.MessageBaseId` | `31337` | HLAPI message base, base..base+3 |
| `Network.RemoteTargets` | `any` | Client targeting: `any` or `self` |
| `Clone.MaxClones` | `50` | Hard cap of clones per player |
| `Clone.RespawnDelaySeconds` | `5` | Pause before a dead clone returns, only with keep-count on |
| `Clone.FollowByDefault` | `true` | New clone groups follow their owner |
| `Cheats.FlySpeed` | `20` | Flight speed in m/s, 5..100, Shift gives x3 |
| `Overlay.Esp` | `false` | Object overlay: teleporter, chests, shrines, bosses |
| `Overlay.EspMobs` | `false` | Box live monsters as well |
| `Overlay.EspBoxes` | `true` | Bounds boxes instead of center dots |
| `Overlay.EspLabels` | `true` | Name and distance labels next to markers |
| `Overlay.EspMaxDistance` | `400` | Marker range in meters, 50..1000 |

---

## Limits

- Flight is player-authoritative and deliberately not in the protocol: it
  works on clients whose host lacks the mod, and it doubles as noclip because
  `KinematicCharacterMotor.SetPosition` does not sweep collisions.
- ESP refreshes 4 times per second and caps at the 48 nearest markers; it is
  pure render, no world writes.
- The HLAPI stub is a hand-written approximation of the shipping networking
  assembly; where they differ (host detection via `NetworkManager`), the code
  degrades to a warning line in the Info tab instead of throwing.
- Clones are AI masters: they fight with the copied loadout and items, not
  with human input.
- One game build at a time is what CI proves; the compile-time check is
  exactly the guard for the next update.

## License

MIT, see [LICENSE.txt](LICENSE.txt).
