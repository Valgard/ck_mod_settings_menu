# Design — Server sync and operator pins (MSM-19)

- **Date:** 2026-10-14
- **Status:** DRAFT. The decisions below were made with the user on
  2026-10-13/14; where a choice was the author's of this document rather than
  the user's, it says so ("*my call*"). Revised after a builder check and
  after three three-lane spec reviews (claims against the code,
  against the goals and criteria, internal coherence); every finding was checked
  at the source before it changed a sentence.
- **Roadmap point:** MSM-19. Its roadmap section carries the mechanism analysis
  this document builds on — GMCM's sync read from source, the façade, the wire
  key — and the operator-pin decisions in § "Operator pins". Where the two
  disagree, this document is the later and the corrected one; §13 lists the
  places.
- **Builds on:** ADR-012 (access level and restart flag cascade from the nearest
  declaration), ADR-013 (a lock describes the moment, not the row).
- **Contradicts:** ADR-001 (*"no server-sync reimplementation"*). A superseding
  ADR is a prerequisite, see §8.

## 1 · Intent

MSM began as a way to change this family's own mod settings in the game. From
the start it was also meant for other authors, and to be able to replace
General Mod Config Menu (GMCM). GMCM synchronises server-scoped settings across
the network; MSM does not, so a server-scoped change made in MSM's menu takes
effect locally and nowhere else. A replacement without sync is not one.

**The bar is parity, read as "nothing less than GMCM", not "the same as GMCM".**
Where GMCM's sync fails in silence — an entry identified by its list position,
a pending change dropped without a word while the player is still on the
server, a sync that only runs while the menu is open — MSM does it differently
on purpose. Where MSM goes beyond GMCM (operator pins), that is a decision of
its own, not a parity requirement.

**MSM does not choose which settings are pinned or pinnable.** Which may be
pinned is the mod author's to declare; which are pinned is the operator's to
choose. MSM carries both; it supplies the default for an author who says
nothing, and refuses the one declaration that cannot work (§6).

**Terms.** A *foreign entry* is a CoreLib config entry MSM did not bind itself
— another mod's, whether or not the menu shows it. *The owner* is a player at
`adminPrivileges >= 2` (§3). *The authority* is the process that owns the
world: a dedicated server, a host, singleplayer.

## 2 · Goals, non-goals, anti-goals

**Goals**

- A `Server`/`Admin`-scoped value changed in MSM's menu on someone else's
  server reaches the server, is judged there, and takes effect for every player.
- On joining a server, its values apply to the joining client without the menu
  being opened, and the player's own values return on leaving.
- An operator can pin a setting to the value in the server's own config file.
- An operator can leave server-scoped values to admins alone (`AdminOnly`).
- Every state a change passes through is visible on its row while the player
  is on the server; what outlives the row — a session ending mid-request, a
  save skipped on the server — is logged instead (§5, §7).

**Non-goals**

- **Running MSM and GMCM on the same server.** Both are `requiredOn: 3`, which
  makes each side demand the mod of the other, not one menu per server: an
  operator *can* install both. It is not supported. An operator picks one per
  server, and one running several servers sets each up on its own. MSM logs a
  warning on a server where it finds GMCM loaded beside it (*my call*) and does
  nothing more; on such a server pins hold only against changes made through
  MSM.
- An in-game editor for pins. MSM-19 ships the rules file alone.
- Pinning to a range or a subset of choices. A pin is one fixed value.
- Changing how Core Keeper assigns `adminPrivileges` stage 2.
- How a consumer that runs without MSM sees server values (MSM-38). The
  roadmap's MSM-38 section asks for it to be settled with MSM-19; the user set
  it aside for now, so that bullet is to be revisited when MSM-38 is taken up.

**Anti-goals** — it fails even if it works, if:

- a change made on a client is in effect locally before the server has accepted
  it, even briefly. (The moment between connecting and the first sync, while
  the player's own values still apply and no synchronised value can be changed,
  is accepted — *my call*. So is the moment between a foreign mod's direct
  write on a client and MSM undoing it (§4): MSM cannot refuse a write it does
  not route.)
- a server's value is written to the player's own config file;
- a change reaches the wrong entry, or is lost, without anyone being told;
- a pin is reported as active while the consumer never sees the value;
- MSM rewrites a line the operator wrote in a rules file, or silently replaces
  an operator's hand edit of a config file. (When a `Bind` adds a key while
  auto-save is on, CoreLib regenerates the file from memory, comments
  included; at a server's start that happens before MSM takes its baseline and
  is CoreLib's behaviour today, out of MSM's reach.)

## 3 · Platform facts the design rests on

Each is stated with how it is known. *Read*: decompile or source, not run.
*Handbook*: `../docs/ck/` in the parent repository, not re-checked for this
document. Items still to verify are collected in §9.

- **CoreLib's scope** (*read*, `ConfigScope.cs`): `ConfigScope` has the
  instance fields `accessLevel` and `requireReload`, and a shared static
  `Empty`, built with the constructor's default level **`Server`**. So a
  foreign entry bound without a scope is server-scoped: it is in the sync set
  and pinnable by default. `ConfigAccessLevel` is
  `ViewOnly = -1, Client, Server, Admin`; `ShouldSync => accessLevel > 0`.
  `Changeable()` allows `Server` on `!player.guestMode` and `Admin` on
  `!player.guestMode && player.adminPrivileges > 0` (`ConfigScope.cs:38-39`),
  and a player is a guest only on a guest-mode world and below stage 1
  (*read*, `PlayerController.guestMode`). It reads `Manager.main.player` — the **local** player — so it
  cannot judge a remote sender on the server.
- **CoreLib corrects or drops invalid values silently** (*read*): `ClampValue`
  moves a number to the nearest bound; `AcceptableValueList` falls back to its
  first value; a value that does not parse is logged and ignored.
- **CoreLib writes a whole file from memory** (*read*, `ConfigFile.Save()`):
  every bound entry through `GetSerializedValue()`, plus lines it read but no
  entry claims (`OrphanedEntries`). The value setter saves when
  `SaveOnConfigSet` is on; `Bind` saves when it adds a new key and the flag is
  on.
- **`ConfigFile.Reload()` only touches entries that have a line in the file**
  (*read*): an entry with no line keeps its in-memory value.
- **MSM's own value writes go through `ConfigStore.Persist`**, because GMCM
  switches `SaveOnConfigSet` off (repo `CLAUDE.md`). Other mods' writes and
  CoreLib's own saves do not.
- **Config files are not one per mod** (*observed* in this machine's `mods/`
  folder): MSM consumers use `<ModId>/config.cfg`, foreign mods choose their own
  names and may have several — `PlacementPlus/PlacementPlus.cfg`;
  `CoreLib/CoreLib.cfg` and `CoreLib/CoreLib.TilesetID.cfg`, the only mod here
  with several. Not every file there is a CoreLib config:
  `PlacementPlus/SledgeState.cfg` is two bare `key=value` lines with no
  section, written by the mod itself, and is outside everything here.
- **CoreLib's own entries are all bound without a scope** (*read*), so all of
  them are `Server`: the tile ID assignments in `[ID Binds]`, read once at load
  (`IdBindConfigFile.cs:53`), and the chat-command security settings
  `EnableCommandSecurity`, `AllowUnknownClientCommands` and one permission per
  command (`CommandModule.cs:251-292`).
- **`adminPrivileges`** (*handbook*, `multiplayer-and-server.md`): `0` none,
  `1` granted and revocable, `2` the host or first player and not revocable,
  `int.MaxValue` offline. Stage 2 goes to whoever connects while the admin list
  is empty, or to a host's local player (*read*, `DedicatedServer/Pug.Other`,
  `NetworkingManager.OnPlayerConnect`:
  `adminList.adminList.Count == 0 || isLocalPlayer`). `PlayerAdminEntry`
  carries a `privileges` field (*read*).
- **Permissions cannot be tested in singleplayer** (*handbook*): the one player
  there is always an admin.
- **A local chat line exists** (*read*, `Pug.Other`, `ChatWindow.AddInfoText`):
  it renders a line in the local chat without the network, but takes a
  `MessageTextType` from a fixed enum plus format fields, not free text.
- **The game does not compare mod builds** (*handbook*,
  `multiplayer-and-server.md` § mod handshake): a server's mod is matched on
  `modId` or `modGuid`, and the guid is the manifest's, which does not change
  between builds. Adding RPC types changes the protocol hash, but two MSM builds
  with the same RPC types connect. **Between such builds, MSM's own protocol
  version (§5) is the only guard.**
- **What already exists** (*read*): `Unity.Entities.TypeHash.FNV1A64(string)`;
  `OnWorldDestroyed` beside `OnWorldCreated` in `PugMod.SDK.Runtime`.

## 4 · Who decides where a value goes

One send façade answers this for every call site in MSM, from two questions:
the entry's **effective scope** and whether **this process is the authority**
(`Manager.ecs.ServerWorld != null` — singleplayer and hosting both are, so they
are one case).

**Effective scope** is the declared scope, except that a **pinned `Client`
entry counts as `Server`** while connected to the server that pins it — its
value then belongs to that server like any server-scoped one (*my call*; it is
what lets the owner change a pinned `Client` value from a dedicated server's
client).

| Effective scope | Authority here? | What happens |
|---|---|---|
| `ViewOnly` | either | never written; refused without the network |
| `Client` | either | written locally, disk and memory |
| `Server` / `Admin` | yes | written locally — this process is the server — then broadcast |
| `Server` / `Admin` | no | sent, awaited; on acceptance **memory only** is set |

**The sync set** is every `Server`/`Admin` entry plus every pinned `Client`
entry, across **every CoreLib config file except CoreLib's own** (the paths
starting with `CoreLib/`), not only MSM's registered consumers — independent of `ForeignConfigDiscovery` and of the player's
`showForeignConfigs` display option, which stays display-only (*my call*;
GMCM, too, enumerates CoreLib's file list rather than its own consumers, but
skips every file whose path starts with `CoreLib` — `ConfigSyncSystem.cs:44`).
Unpinned `Client` entries never travel; the roadmap records why echoing them
through the server was rejected.

**CoreLib's own files stay out, as in GMCM** (user): neither synchronised nor
pinnable, and given no rules file. Their entries are all `Server` by default
(§3), so syncing them would let every stage-0 player on a world without
`AdminOnly` switch off the server's command security, and would present ID
assignments that only take effect at load as changeable values.

**Entries with a restart flag are synchronised like any other** (user). A
remote change to one is accepted and written to the server's config file, and
its row shows that it takes effect after the server restarts.

**On a non-authority client, no code changes a synchronised value locally.**

- A consumer's own write through `SettingHandle<T>.Value` is refused (user):
  the setter is synchronous and cannot await a verdict, so the write does not
  happen and a warning names the mod and the key. Until the first sync has
  arrived, a pinnable `Client` entry counts as synchronised for this rule, as
  its row does in §5 (*my call*).
- A foreign mod's direct write to a synchronised entry cannot be refused before
  it happens, so it is undone: MSM sees it through `SettingChanged`, writes the
  server's value back, and a warning names the entry (*my call*, the same rule
  applied where no setter of MSM's is involved).
- On the authority, code writes are ordinary writes and are broadcast (§5).

### Keeping the player's disk untouched

On a non-authority client the player's config file stays their preference;
while on someone else's server, the in-memory entry holds the server's value and
the row shows it. CoreLib does not separate the two (§3), so while a server's
value sits in an entry, every save of that file would write it to the player's
disk — CoreLib's auto-save, `ConfigStore.Persist` for a *different* entry of the
same file, a `Bind`, another mod's write. The roadmap's "the disk was never
touched" assumed otherwise.

Decided (user):

- **On joining**, before the first server value is applied, MSM **snapshots**
  the in-memory values of every synchronised entry — which at that moment are
  the player's own. The join notice of §7 counts differences against the same
  snapshot.
- **During the session, a patch on CoreLib's save path** makes every save of an
  affected file write the snapshot value for each synchronised entry, and the
  in-memory value for every other entry. `SaveOnConfigSet` is not touched, so
  auto-save keeps working for everything else, other mods' writes to
  unsynchronised entries included, and MSM's convention of leaving the flag as
  GMCM set it stands unchanged.
- **On leaving**, MSM writes the snapshot values back into the synchronised
  entries. Not `Reload()`: it would leave an entry that has no line in the file
  on the server's value, and would re-read unsynchronised entries for no
  reason.
- **Only on a non-authority client.** On the authority the in-memory value *is*
  the server's value, so it is written to disk as it is.

## 5 · The two directions

**Send a change, apply the verdict.** The order is send → await → write. The
row never writes first and rolls back; nothing is written until the server
says yes. A request carries a **request id**, the key, the new value and the
**value the client showed when the player changed it**; a verdict, and the
broadcast of a change it caused, carry that request id (*my call* — the shown
value makes "overridden" decidable, the id lets a client recognise its own
change arriving late).

**Fetch everything on joining.** Independent of the menu — GMCM's initial sync
lives in its menu's `Update`, which is enough for display but not for values
that are meant to apply. On connection the client requests the full sync set:
**every synchronised entry's value and whether it is pinned**. On disconnection
it restores the snapshot (§4). The candidate event pair is `OnWorldCreated` /
`OnWorldDestroyed` (§9); polling, as GMCM does, is the fallback. **Until the
first sync has arrived, rows of synchronised and pinnable entries are yellow and
locked** (*my call*): the client does not yet know which entries are pinned,
and must not let a change through that the server would hold.

**The sync handshake carries a protocol version of MSM's own** (*my call*) —
the only guard against mismatched MSM builds (§3). On a mismatch the client
does not sync and keeps its own values; the rows of server-scoped entries are
**withheld** (greyed, ADR-013) with a section note naming the reason, so no
change can take effect locally only; and the chat says so once. Pins do not
reach such a client: its `Client` entries stay its own and editable, which is
never reported as a pin (*my call*).

**A value the client cannot accept for its own build** — out of range, not a
known choice, not parseable, because the consumer differs between the two sides
— is not applied: the row is withheld with that reason and an error names the
entry (*my call*).

**The server broadcasts every change to a synchronised entry, to all clients**,
hung off `SettingChanged` on those entries rather than off the façade — so the
server's own code writes and other mods' writes reach the clients too (*my
call*; the façade alone would miss them). That is also what makes the outcomes
in §7 self-correcting: a client that timed out still receives the truth.

**The server judges a request in this order** (*my call*):

1. unknown key → refuse;
2. pinned and the sender below the owner → refuse as pinned;
3. the sender's rights for the entry's scope (§9, item 4), with `AdminOnly`
   (§6) raising a `Server` entry to admins only → refuse;
4. the entry's current value differs from the value the client showed → refuse
   as **overridden** — something changed it first;
5. a value outside the entry's acceptable values, or not parseable → refuse with
   a reason, rather than letting CoreLib clamp or drop it silently — client and
   server may run different builds of the same consumer;
6. the config file edited on disk since the server last read or wrote it →
   refuse (below);
7. otherwise accept, write, and the save and broadcast follow.

**A config file edited by hand on a running server is never silently replaced**
(user). `Save()` writes every entry from memory, so the next save after an
operator edited the file would undo their edit — and on a dedicated server
another player, or another mod, triggers that save without the operator ever
learning of it. MSM therefore keeps the content of each config file as it was
last read or written (`API.ConfigFilesystem.Read`) and, through the same
save-path patch as §4, compares it with the file **before every save on the
server**, whoever causes it:

- a difference before a save MSM causes for a request refuses the request
  (item 6 above) with the reason "this mod's settings file was edited on the
  server; restart the server";
- a difference before any other save **skips that save**, and an error names
  the file and the caller (user). The other mod's change then lives in memory
  until the server restarts; the operator's edit stays on disk and takes effect
  at the next start, as for the rules file (§6);
- the baseline is refreshed after every save that does happen;
- content, not a timestamp, is compared, so nothing depends on how
  `GetFileTime` resolves under Wine.

**The key on the wire is a hash, never a list position**:
`Unity.Entities.TypeHash.FNV1A64` over `"<file>/Section/Key"`, where `<file>` is
the config file's path relative to the `API.ConfigFilesystem` root —
`FasterTalents/config.cfg`, `PlacementPlus/PlacementPlus.cfg` — so it works for
any file without resolving which mod owns it (*my call*; the roadmap proposed
`ModId`, which a foreign file does not always yield and which does not tell two
files of one mod apart). Resolved by dictionary lookup; an unknown key is
refused and named in the log. Two keys that hash alike are detected when the
sync set is built, both are excluded, and an error names them (*my call*).
GMCM's list index silently shifts its ids when the two sides' sets of
server-scoped entries differ.

## 6 · Operator pins

Settled in the roadmap's § "Operator pins"; restated here as the builder needs
it.

**The author declares what may be pinned.** `operatorLockable` is a nullable
parameter on `ModSettings.Section`, `SectionBuilder.Group` and every widget
method, innermost statement wins, plus an `.OperatorLockable()` modifier after a
widget. It follows `requiresRestart` at group and widget level; **at section
level it differs**: `requiresRestart` there is a plain `bool = false` and
cannot be left unstated, while `operatorLockable` must be `bool?`, or the
scope-derived default below could never apply.

| Resolved `access` | unstated | `true` | `false` |
|---|---|---|---|
| `Client` | not pinnable | pinnable | not pinnable |
| `Server` / `Admin` | pinnable | pinnable | not pinnable |

- Opting a `Client` value in is the author's promise that the consumer reads
  `handle.Value` live and copes with it changing mid-session. The tutorial says
  so.
- Foreign entries cannot declare: the default applies, so their `Server`/`Admin`
  entries — scope-less ones included (§3) — are pinnable. A pinned `Server`
  value is the server's own: the one it loaded at start, or the one the owner
  set since. Its consumer sees it exactly as it would see that value unpinned;
  a pin adds a lock, not a delivery path.
- **`operatorLockable` together with `requiresRestart` is refused on a
  `Client` entry** (user): a pinned `Client` value arrives on a client
  mid-session and could not take effect before a restart there. The setting
  stays, not pinnable, and an error names the mod, the key and where each value
  came from — section, group, widget, or the scope default — including when the
  conflict arises only through inheritance. On a `Server`/`Admin` entry the
  combination is allowed: there a pinned value takes effect after the server
  restarts exactly like an unpinned one. A foreign `Client` entry with
  `requireReload` is not pinnable by default anyway.
- This is the third value through the cascade. Before adding it, the `??`
  chain in `SectionBuilder.BindGuarded` becomes one mechanism (ADR-012 names
  this point), and resolution returns the value **and** the level it came from.
- Where the flag is stored is left to the builder: in `SettingDef`, and
  optionally in a `ConfigScope` subclass so it travels with the CoreLib entry.
  That works only for entries MSM binds — each gets a fresh scope instance —
  not for foreign scope-less entries, which share `ConfigScope.Empty`; those
  never carry a declaration anyway.

**The operator chooses what is pinned**, in a **rules file beside each config
file**, in the same folder and named after it (*my call*, refining the user's
"beside the mod's config file" for mods with several or differently named
files): `FasterTalents/config.cfg` → `FasterTalents/config.rules.cfg`,
`PlacementPlus/PlacementPlus.cfg` → `PlacementPlus/PlacementPlus.rules.cfg`. The
naming pattern is not settled.

~~~ini
General/enabled = true
Rules/xpFactor  = false
~~~

- The file says only *whether*. The value is the server's own config file entry,
  and CoreLib handles an invalid one there as it does today.
- `false` means not pinned, the same as an absent line.
- MSM creates the file at server start, on a dedicated server and when a player
  hosts — in practice also in many singleplayer runs, where a file of `false`
  lines does nothing. Every pinnable key is listed as `false`; keys a mod update
  adds are appended. Existing lines — values, order, comments — are never
  rewritten: MSM only reads the file and appends to it, never regenerates it the
  way `ConfigFile.Save()` does. A key that no longer exists stays and is
  reported. A config file with nothing pinnable gets no rules file.
- An unknown key, a key that is not pinnable, or a value other than `true` or
  `false` is ignored, leaving the entry unpinned, and an error names the file,
  the line and the reason. A bad line never stops the server.
- A pinned `Client` value needs the mod installed on the server too.
- **A hand edit of the file takes effect at the next server start** (*my call*),
  for three reasons. The config file beside it is only read at start as well,
  and a pin is made of both files — a live pin over a stale value would fix the
  old number. The sandbox offers no file watch, only polling through
  `API.ConfigFilesystem`. And a pin changing mid-session moves an entry to
  another row of the façade (§4) while requests may be outstanding.
- **The server holds the pin state in memory**, loaded from the file at start
  (*my call*). A later in-game editor then changes the state through MSM itself,
  takes effect at once and broadcasts it; how it writes the change back without
  rewriting the operator's lines is that editor's design, not this one's.

**A pin holds every player below the owner.** Of the players, only the owner
may change a pinned value — the first player or host, and offline play. A pin
holds players, not code: on the authority a mod's own write is an ordinary
write and is broadcast (§4). The owner changes it
through the ordinary row; the server writes it into its config file and
broadcasts it. The pin itself changes only in the rules file. Operator
documentation tells a dedicated server's operator to enter themselves at stage 2
in `Admins.json` before anyone connects; otherwise whoever connects first is the
owner.

### `AdminOnly` — leaving server values to admins

MSM-19 ships it (user), with GMCM's semantics. Without it, CoreLib's rule for a
`Server` entry is `!guestMode`, so on an ordinary world every stage-0 player
may change every unpinned server value. The two existing answers are too
coarse for that: a pin also holds stage-1 admins, and guest mode restricts play
itself.

- **One `bool` entry, default `false`, bound at `ConfigAccessLevel.Admin` in
  MSM's own config file** — a `Toggle` with `access: ConfigAccessLevel.Admin`
  beside `showForeignConfigs` in MSM's `Client`-scoped section, which the
  cascade already allows per widget. With it set, a `Server` entry needs
  `adminPrivileges > 0`; `Admin` entries need that anyway, and `Client` entries
  are not affected — the switch governs *who* may change a server-scoped value,
  not *which* values are server-scoped.
- **It travels like any other value.** It is a row in MSM's own section, an
  admin toggles it, the server judges and broadcasts it, and the operator can
  set it in the file. Being `Admin`-scoped, it can be pinned like any other
  `Admin` entry, which hands it to the owner alone.
- **It cannot lock admins out of server values.** A non-admin cannot change
  it, and while it is not pinned an admin who sets it keeps the right to unset
  it. Pinned, it moves to the owner, which is what pinning it is for.
- **On a client, a change of it re-evaluates every synchronised row at once**,
  so a stage-0 player's `Server` rows become withheld or editable without
  reopening the menu. It is the one entry with a special case in the receive
  path. `AccessLock.Reason` takes the switch into account and answers
  `ConditionUnmet` for such a row: a missing right, which asking an admin can
  change, and already transient.

Pins and `AdminOnly` are of different strength and neither replaces the other:
`AdminOnly` delegates server values to every admin, a pin holds one value for
everyone below the owner.

## 7 · What the player sees

| Moment | Row |
|---|---|
| Connected, first sync not yet arrived | synchronised and pinnable rows **yellow and locked** |
| A change is sent and unanswered | **yellow and locked**, showing the **requested** value. One request per entry at a time. |
| The server accepts | normal again; the requested value stays and is in effect |
| The server refuses | back to the previous value; the withheld rendering of ADR-013 with the reason |
| Refused as overridden | the value that won is shown, with a notice that the player's own input was overridden |
| No answer within the timeout | normal again, back to the previous value, with a notice naming the timeout |
| Accepted after the timeout | the accepted value arrives by broadcast carrying the request's id, with a notice that the change went through late (*my call*) |
| The session ends while a request is outstanding | the request is dropped and logged. The screen closes with the world, so there is no row left to show it on; the value belonged to the server just left, and the player's own values return (*my call*). |
| The value is pinned and the player is below the owner | greyed out (ADR-013), showing the server's value; the section note says the server operator set it |
| An accepted value whose entry needs a restart | normal, showing the new value, marked as taking effect after the **server** restarts |
| The MSM protocol versions differ | server-scoped rows withheld, the section note naming the mismatch (§5) |
| A server value this client's build cannot accept | that row withheld, naming the reason (§5) |

**Actions that write several entries** (*my call*):

- **Section reset** sends one request per entry; each row goes through the
  states above on its own. Rows that are not editable at that moment — pinned,
  or otherwise withheld — are skipped, as `SectionReset` already skips every
  row that is not editable (`ListEditing.ReadOnly` lists are editable in this
  sense and are not skipped today either).
- **The list drill-in** is locked while a request for its entry is outstanding,
  which is the single-entry rule applied to a screen that edits one entry.

- **Yellow is a state of its own**, not `GRAYED_OUT`: grey means "you may not",
  yellow "you asked, or the server has not answered yet". It occurs only while
  connected to a server that is not this process, so a locally answered value
  never flashes yellow.
- **Pinned is a new `LockReason.OperatorLocked`**, which `AccessLock.IsTransient`
  must classify as transient — it belongs to the session. It is kept apart from
  `ConditionUnmet` because asking an admin helps with one and not the other.
  The protocol-mismatch and invalid-value withholdings of §5 are reasons of the
  same kind and need the same classification.
- **A row the client already knows to be locked is never sent.** A guest on a
  guest-mode world is withheld before any request (`AccessLock`, today's
  behaviour), and so is a stage-0 player's `Server` row while `AdminOnly` is
  set; the server's own check is the defence against a client that skips
  it, not the path a normal player sees.

**On joining, a chat line**, through the local chat (§3), when at least one
synchronised value differs from the snapshot of the player's own (§4) — once
per join, giving how many settings now come from the server and from how many
mods, a mod being counted as the first path segment of its config files
(*my call*; count-only content and once-per-join are *my call* too). No line
when nothing differs.

New player-facing text needs loc terms, English and German, in the framework's
own `localization.yaml`.

## 8 · Prerequisites

1. **An ADR superseding ADR-001's no-sync clause**, recording why MSM now
   carries its own sync. It is written when the implementation lands (the
   repository's spec → ADR convention), but the contradiction has to be named
   in the plan from the start.
2. **The cascade refactor** of §6, before `operatorLockable` is added.

## 9 · To verify before planning

Each is a gap in what is known, not a decision. The plan starts with them, and
with the first one above all: the design has no fallback for it.

1. **The save-path patch** (§4, §5): where in `ConfigFile.Save()` /
   `GetSerializedValue()` the snapshot substitution and the before-save
   comparison fit, how a save is skipped, how its caller can be named, and above
   all that a Harmony patch on CoreLib's assembly binds from MSM's sandboxed
   source. Two goals (disk untouched, hand edits survive) rest on it alone; if
   it cannot bind, stop and bring the design back.
2. That `OnWorldCreated` / `OnWorldDestroyed` fire at the right moments for a
   client joining and leaving a remote server — they exist (§3); their timing
   relative to the connection is not established.
3. That `Unity.Entities.TypeHash.FNV1A64(string)` is reachable from MSM's
   sandboxed source (`Unity.Entities` is a referenced assembly; not compiled).
4. How the server evaluates a **remote** sender's rights: CoreLib's
   `Changeable()` reads the local player, so the server needs the sender's
   `adminPrivileges` and the world's guest mode from the connection.
5. `PugSimulationSystemBase` / `isServer` — from a subagent report, not opened
   first-hand. (`Get`/`SetSerializedValue` over `TomlTypeConverter` for
   serialising values is confirmed: `ConfigEntryBase.cs:109`, `:112`.)
6. The `adminPrivileges` stages against the decompile, and that a stage-2 entry
   written into `Admins.json` by hand is applied on connect.
7. That `API.ConfigFilesystem` roots at the same `mods/` folder on a dedicated
   server as on a client, and that a foreign mod's CoreLib config file has the
   same relative path on both — the wire key (§5) depends on it.
8. A way to put MSM's own text through `ChatWindow.AddInfoText`: a value
   outside `MessageTextType` plus a patch, the pattern MSM already uses for
   `RadicalMenu.MenuType`, or another route.
9. Whether Core Keeper has a yellow text colour or option state to reuse.
10. Whether singleplayer and hosting can be told apart — for the operator
    documentation only; the decision to create the rules file in both stands.

## 10 · Left to the builder

- The timeout length (*my call*: a few seconds, set once a real round trip under
  CrossOver has been measured).
- The rules files' naming pattern, and the wording of every notice.
- RPC layout, chunking of large values (a precedent exists), and where the
  façade, the sync system and the rules-file reader live in the code.
- The test hooks the criteria need, behind the existing `TestFixtures` build
  flag: holding back the server's verdict **and** its broadcast to the
  requesting client, since the broadcast carries the request id and would
  otherwise resolve the row before the timeout (§12, item 4); sending a request
  past the client's own lock (items 2, 17); delaying one request on the server
  so a second overtakes it (item 3); and declaring two keys that are forced to
  hash alike (item 9).

## 11 · Verification

Permissions and the transport cannot be checked in singleplayer; the
declaration rules, the rules file's creation and the logs can. The walk needs
the local dedicated server (`../utils/server.sh`) and players at stage 0, at
stage 1 and at stage 2 — `docs/manual-tests.md` already states the admin level
the permission walk needs. §12 is the list it has to tick off.

## 12 · Acceptance criteria

Each is a pass condition, checked on the dedicated server unless it says
otherwise. The anti-goal each one guards, where it guards one, is named.

1. **A change travels.** A stage-0 player on a non-guest world changes an
   unpinned `Server` value: the row is yellow until the answer, then normal; a
   second connected client shows the new value without opening the menu; the
   server's config file holds it. The same holds for a foreign mod's
   `Server` entry, and on a world a player hosts, where the host's own change
   is local and reaches the other client by broadcast.
2. **A refusal is visible.** On a guest-mode world a stage-0 player's
   server-scoped row is withheld before anything is sent; a request sent past
   that lock by a test hook is refused by the server, and the row returns to the
   previous value with the ADR-013 rendering and a reason.
3. **An override is visible.** Two clients change one entry at once: one is
   accepted, the other is refused as overridden and shows the winning value
   with the notice.
4. **A timeout is visible.** With the server's answer held back by a test hook,
   the row returns to the previous value with the timeout notice; when the
   answer is released, the acceptance arrives with its request id and the late
   notice.
5. **Nothing takes effect early** (anti-goal 1). While a row is yellow, the
   client's in-memory value of that entry is the previous one; a consumer's own
   write to a synchronised value on a client is refused with a warning; a
   foreign mod's direct write is undone with a warning.
6. **Joining applies the server's values.** A client whose own values differ
   joins: the values apply without the menu, one chat line gives the counts,
   synchronised and pinnable rows are locked until the first sync; joining with
   identical values produces no line.
7. **Leaving restores the player's own values**, including an entry that has no
   line in the player's file.
8. **The player's disk is untouched** (anti-goal 2). After a session in which
   the player changed an unsynchronised `Client` setting of a mod that also has
   synchronised entries, that file holds the player's own values for every
   synchronised entry and the new value for the changed one.
9. **Nothing reaches the wrong entry** (anti-goal 3). A request for a key the
   server does not know is refused and named; two keys hashing alike are both
   excluded and named; a value out of the entry's range is refused by the
   server, and a server value out of the client build's range is withheld on
   the client — each with its reason.
10. **Pins hold.** A pinned `Server`, a pinned `Admin` and a pinned `Client`
    value are greyed out for stages 0 and 1 and changeable at stage 2; the
    owner's change reaches the other clients.
11. **A pin reaches the consumer** (anti-goal 4). A test consumer that reads a
    pinned `Client` value live shows the server's value on a stage-0 client,
    and the owner's change while it runs.
12. **The rules file is the operator's** (anti-goal 5). It is created with every
    pinnable key at `false`, appended after a pinnable declaration is added, and
    otherwise byte-identical across restarts; a bad line is logged with file,
    line and reason and leaves the entry unpinned; CoreLib's own files get
    none, and their entries never travel. (Singleplayer suffices for the
    file; the second half needs the server.)
13. **A hand edit survives** (anti-goal 5). The operator edits a config file on
    the running server: the next in-game change to it is refused with its
    reason, a save caused by anything else is skipped and logged, and the edit
    is still in the file afterwards.
14. **Declarations are checked.** `operatorLockable` with `requiresRestart` on a
    `Client` entry is refused with the origin of both values, also through
    inheritance; on a `Server` entry it is accepted. (Singleplayer suffices.)
15. **A restart entry is marked.** A remote change to a restart-flagged `Server`
    entry is accepted and its row says it takes effect after the server
    restarts.
16. **A protocol mismatch is said.** A client with a different sync protocol
    version joins: no sync, its own values stay, server-scoped rows are
    withheld with the reason, and the chat says why.
17. **`AdminOnly` holds stage 0 only.** With it set by a stage-1 admin, a
    stage-0 player's `Server` rows become withheld without reopening the menu,
    a request sent past that lock by a test hook is refused, and the stage-1
    admin can still change the same values and unset the switch; `Client` rows
    stay editable. Unset, the stage-0 rows become editable again without
    reopening the menu.
18. **Actions on several entries hold.** A section reset on the server skips
    pinned and withheld rows and takes each other row through its states on
    its own; the list drill-in of an entry stays locked while a request for it
    is outstanding.
19. **A session ending mid-request is safe.** Leaving while a request is
    outstanding drops and logs it, and the player's own values return.

## 13 · Where the roadmap is behind this document

- § "Send first, then write": *"Coming back is cheap: `ConfigFile.Reload()`
  re-reads the file … the disk was never touched."* — neither holds; see §4. Its
  façade table has no broadcast on the authority's row; see §4.
- § "Two directions": the join and leave event pair is called unverified; a
  candidate pair exists, §3 and §9.
- § "The key on the wire": `ModId` in the key — replaced by the file path; and
  the string-hash utility is called unsearched — it exists, §3.
- § MSM-38: asks for what a contract consumer sees on a server to be settled
  with MSM-19; this document sets it aside (§2, non-goals).

§ "Operator pins" was corrected in the same change as this document: one rules
file per config file, and the restart refusal limited to `Client` entries. Both
open points on `AdminOnly` were settled in that change too — MSM brings the
switch itself, and it ships beside pins (§6).
