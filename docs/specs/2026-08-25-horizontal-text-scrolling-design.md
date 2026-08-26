# Design — Horizontal scrolling in a drill-in text row

Status: approved, not implemented.
Measured against Core Keeper `1.2.1.5` on 2026-08-25 with a throwaway spike;
every claim below that carries a line reference was read off the decompile and
checked, and every visual claim was seen in game.

## 1 · Goal

A list token wider than its row must stay fully reachable: the field scrolls
horizontally with the caret instead of showing a fixed prefix. This closes a
**data hazard**, not a comfort gap. Today a value too wide to render can be
viewed safely but destroyed by editing — the user types against the shortened
text they can see, and the invisible remainder is gone with nothing in the UI
hinting at it (`docs/roadmap.md` § "Horizontal scrolling in a text field", where
the case was measured with a 57-character token).

It is also a prerequisite rather than a follow-up for the per-row delete and
reorder buttons: those take 5.375 units off the row, which would make the trap
ordinary instead of rare.

## 2 · Decisions (locked)

| Decision | Choice |
|---|---|
| Scope | Viewport **plus** full cursor navigation — Home/End, Ctrl+←/→, click-to-place |
| Resting state of an over-long row | Hard cut at the mask edge. **No** ellipsis |
| Where the masks live | **In the prefab**, authored in the Editor — not `AddComponent` at runtime |
| Approach | Two masks with disjoint sorting ranges, per-frame intersection fit |
| Rejected | Substring window (§ 8) |

`BetterTextInput` was examined as a model and does **not** solve this: its only
scrolling code targets `ChatWindow`. For `RadicalMenuOptionTextInput` it adds
selection and clipboard handling, and its own `AppendString` rejects over-wide
input exactly like vanilla. What it did supply is the pointer to CK's own
implementation.

## 3 · Why the field cannot scroll today

`maxWidth` on `RadicalMenuOptionTextInput` is a **capacity**, not a viewport, and
it is enforced twice:

- **`Update` trims from the end, every frame** — `while (maxWidth > 0f && …)`
  (`Pug.Other:343398`). Gated, so `maxWidth = 0` switches it off cleanly.
- **`AppendString` rejects and rewinds** — `if (pugText.dimensions.width >
  maxWidth)` (`Pug.Other:343446`). **Not** gated.

That asymmetry is the load-bearing detail: setting `maxWidth = 0` disables the
trim but makes the reject condition true for every non-empty text, so **every
keystroke would be rolled back and the field would be unwritable**. A viewport
therefore *requires* replacing `AppendString`; this is not optional cleanup.

## 4 · Architecture — the two masks

The row's text and the row's chrome must be clipped by different rectangles: the
chrome vertically by the list viewport, the text horizontally by the field. Two
masks over one renderer combine as **OR**, so this needs disjoint custom ranges —
mechanism, the three silent failure modes, and the measurement are in
`docs/ck/ui-framework.md` § "Two masks over one renderer combine as OR, not AND".
Do not re-derive them here.

What this mod adds:

- **`ViewportMask`** (existing, on `ListDetailScreen.prefab`) gains a custom
  range covering everything *below* the glyph order, so it keeps governing frames
  and markers and releases the glyphs.
- **A second `SpriteMask` on `ItemTemplate`**, authored in the Editor, with a
  range from below the glyph order upward. Prefab-authored so sprite, material,
  both layer fields and the range are serialized rather than assembled at
  runtime. Reuse the sprite the existing `ViewportMask` references, and note its
  `.meta` requirement (`spritePixelsToUnits: 1`).
- **The bands must abut, not overlap.** The lower bound is exclusive, so the
  field mask starts at `9998` — the same value the viewport ends on, which
  belongs to the viewport and to nothing else. That leaves the glyphs (`9999`)
  and `CharacterMarkBlinker`, which shares their order, to the field mask, and
  nothing else falls in either band's way: the row frames sit on `0`.
  A band starting on `9999` would exclude the glyphs from **both** masks and the
  text disappears entirely — the failure the spike hit three times.

  The spike measured `9999` (fails) and `9899` (works); `9998` follows from the
  bound being exclusive rather than from a separate measurement. It is used
  because it abuts the viewport exactly and needs no arbitrary gap to explain.
  If the text is invisible after the prefab change, that inference is what to
  suspect first — widening to `9899` is the fallback, and it is measured.

**The only runtime geometry** is re-fitting each row mask, every frame, to the
intersection of its field rectangle with the viewport bounds. Without it the mask
scrolls out of the list with its row and keeps clipping outside — measured, and
visible as text standing above the title with no frame around it. An empty
intersection disables the mask, which is the correct disappearance rather than a
workaround.

## 5 · Architecture — the text offset

Core Keeper already scrolls a text field horizontally: `ChatWindow`, via
`inputFieldMask` plus an offset on the text transform
(`ChatWindow.AdjustInputFieldPosition`, `Pug.Other:317599`). Vanilla follows the
text *end*, because chat only ever appends — `ChatWindow.MoveCharMarker` has an
empty body. A caret-following variant of the same formula is what
`BetterTextInput` patches in, and it is the shape to port.

**The caret position is readable without the index.** `currentCharIndex` is
`private` on `RadicalMenuOptionTextInput` (`Pug.Other:343320`), so a subclass
cannot see it — but `Update` writes the caret's x into the public
`characterMarkBlinker` every frame (`Pug.Other:343386`–`343388`). The offset
needs the *position*, not the index:

```
caretLocalX = blinker.position.x − pugText.transform.position.x
```

That difference is in text-local space and therefore independent of the offset
being computed, which keeps the calculation non-circular. **No access DLL is
needed** — the route `BetterTextInput` takes for its own reasons.

## 6 · Cursor navigation

- **Home / End** need no index either. `MoveCharMarker(int relativeChange)` is
  public and clamped (`Pug.Other:343455`), so `MoveCharMarker(±GetTextLength())`
  lands exactly on 0 or the end, with no overflow.
- **Word jumps and click-to-place** do need the index, recovered backwards: find
  the nearest entry in the public `pugText.localCharacterEndPositions` to the
  caret offset (or to the mouse x), then hand the difference to
  `MoveCharMarker`. The same list answers "which character is under the pointer".

## 7 · Edge cases

- **The caret is not clipped.** `CharacterMarkBlinker` carries
  `maskInteraction: None` in the prefab and shares the glyphs' sorting order, so
  it would wander out of the field while scrolling and stay visible. It has to be
  brought under the field mask.
- **An empty row measures as nothing.** `PugText.Render` returns
  `dimensions = Rect.zero` for an empty string, and blank rows are a legitimate
  state here — anything derived from text metrics must not collapse.
- **Controller sessions bypass all of this.** Text arrives through the on-screen
  keyboard in one synchronous callback; the caret sits at the end afterwards, so
  the viewport needs no controller-specific path. Cursor navigation is
  keyboard/mouse only.
- **`MenuPatch`'s two prefixes gate on the active field being a
  `ListDetailItem`.** A future `SettingKind.Text` sharing this widget must be
  added to that gate, or the main screen reproduces the focus-stealing bug they
  were written for.

## 8 · Rejected

- **Substring window** — clip by rendering only the visible characters, no second
  mask. Rejected because it makes the `PugText` stop being the truth, and
  `ListDetailItem`'s edit detector reads exactly that text to tell a user's
  keystroke from CK's width trim. Scrolling would change the visible text without
  a keystroke and defeat the guard this feature exists to protect. It would also
  need `AppendString`, `RemoveCharAtMarker` and `RemoveCharBehindMarker` all
  replaced, and `GetInputText` is a non-virtual interface member that cannot be
  redirected.
- **Ellipsis on overflow** — costs a sprite over the mask edge or a character in
  the text, and the latter corrupts the `dimensions.width` measurement the
  capacity logic reads. Retrofittable later without touching the viewport.

## 9 · Verification (manual, in-game)

Build with `MOD_DEV_FLAGS=TestFixtures` and use the `Overlong` fixture, whose
token is the same 57-character identifier the original hazard was measured with.

1. The token is fully reachable by moving the caret to the end.
2. Typing at the end appends to the **whole** value, not to the visible prefix —
   compare the owning `config.cfg` afterwards.
3. Home/End jump to both ends; Ctrl+←/→ move by word; a click places the caret.
4. Scrolling the list leaves nothing outside it, at either edge.
5. Neighbouring rows (`Before`, `After`) are unchanged after an edit.

## 10 · References

Paths starting `docs/ck/` are the shared handbook in the **parent** repository
(`core_keeper/`), not this one.

- `docs/ck/ui-framework.md` — the mask mechanics, and § "A text row in a menu"
  for the capacity-versus-wrapping distinction
- `docs/adrs/003-list-widget-editing.md` — the editable row this extends
- `docs/adrs/005-drill-in-row-model.md` — the row model the masks hang in
- `docs/roadmap.md` — the hazard, and the button geometry that depends on this
