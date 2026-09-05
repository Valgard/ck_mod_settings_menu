using System.Linq;
using PugMod;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Horizontal viewport for one text row: keeps the row's field mask inside the list viewport
    /// and offsets the text so the caret stays visible. Standalone rather than part of
    /// ListDetailItem, because the planned SettingKind.Text row will need the same behaviour. That
    /// row does not exist yet (docs/roadmap.md), so the split is a forward bet, not a second caller.
    /// </summary>
    internal sealed class TextFieldViewport
    {
        private RadicalMenuOptionTextInput _input;
        private PugText _text;
        private SpriteMask _fieldMask;
        private SpriteMask _viewportMask;
        private CharacterMarkBlinker _blinker;
        private float _fieldWidth;
        private float _fieldHeight;
        private float _fieldOriginX;

        // The text and the blinker are taken OFF the row rather than passed alongside it, because
        // TryCaretIndex needs the row itself and the other two are its own public fields — handing
        // all three in would let a caller bind a viewport to one row's caret and another's glyphs.
        //
        // The field rectangle comes from the mask's OWN prefab transform, never from the frame.
        // The frame is 16.625 units centred at 7.8125 and spans [-0.5, 16.125], while the text starts
        // at 0 — sizing the clip from it lets the text run half a unit PAST the frame, which is what a
        // first attempt did. The prefab mask is authored at 15.625 units from 0, which keeps that half
        // unit of air at both ends.
        //
        // Those two numbers are the prefab's and have moved once already: the row was 22 and 21 until
        // the three buttons arrived and took the space from 17.625 rightwards. Re-measure them there
        // rather than trusting this paragraph if the row is ever re-laid-out again.
        //
        // Read once, here: re-fitting moves the mask every frame, so its live transform stops being
        // a witness to its authored geometry after the first Tick.
        public void Bind(RadicalMenuOptionTextInput input, SpriteMask fieldMask, SpriteMask viewportMask)
        {
            _input = input;
            _text = input.pugText;
            _blinker = input.characterMarkBlinker;
            _fieldMask = fieldMask;
            _viewportMask = viewportMask;
            var t = fieldMask.transform;
            _fieldWidth = t.localScale.x;
            _fieldHeight = t.localScale.y;
            _fieldOriginX = t.localPosition.x - _fieldWidth / 2f;
        }

        /// <summary>Call once per frame from the owning row's Update. <paramref name="isActive"/> is
        /// whether THIS row currently holds Manager.input.activeInputField — see ApplyOffset for why
        /// that matters.</summary>
        public void Tick(bool isActive)
        {
            FitMaskToViewport();
            ApplyOffset(isActive);

            // For the warning alone, and only while this row is the one being edited. Reading the
            // caret from its counter left IndexSpaceIsSound reachable from the mouse click and
            // nowhere else — so a keyboard-only or controller-only session would never learn that
            // the glyph list has gone empty, and that fault is not confined to clicking: vanilla
            // places the blinker from the same list (Pug.Other:343387), so it also pins the drawn
            // caret to the row's start and kills the scroll-follow with it. The verdict is discarded
            // because nothing here consumes an index; the call is for its latched log line.
            if (isActive)
                IndexSpaceIsSound();
        }

        // The caret's x in text-local space — independent of the offset applied below, which is what
        // keeps the calculation non-circular. Private: ApplyOffset is its only consumer, and the
        // blinker is a frame-stale source for anything asking about an INDEX (see TryCaretIndex), so
        // publishing it would leave the route this class deliberately stopped using one call away.
        private float CaretLocalX => _blinker != null && _text != null ? _blinker.transform.position.x - _text.transform.position.x : 0f;

        // RadicalMenuOptionTextInput.currentCharIndex (Pug.Other:343320) — the caret's own string
        // index, which is what vanilla itself inserts and deletes at. Private, and reached here
        // through the SDK's reflection surface rather than reconstructed.
        //
        // Two different gates have to be cleared for that, and confusing them sends the next reader
        // to the wrong place. The Roslyn sandbox permits the SOURCE, simply because PugMod.MemberInfo
        // and API.Reflection are on none of its deny lists (docs/ck/sandbox.md § "Reaching a private
        // member"). InvokeChecker.CheckType (PugMod.Loader:552) is a RUNTIME gate inside
        // API.Reflection.GetValue: it inspects the member's declaring type, never the calling mod, so
        // it can only make a sandbox-legal read throw — it cannot make an illegal one compile. It
        // refuses [DisallowPatching] types (five in the whole game, four filesystem and one
        // networking) and PugMod.Loader itself, and admits by assembly-name prefix — Pug, Unity,
        // SpriteInstancing, I2, Rewired. RadicalMenuOptionTextInput lives in Pug.Other, so it passes.
        //
        // The lookup goes to the DECLARING type, never to ListDetailItem: GetMembersChecked calls
        // type.GetMembers(Instance|Static|Public|NonPublic) (PugMod.SDK.Runtime:644), and a private
        // field is reported only by the type that declares it, so asking the subclass finds nothing.
        // The lookup itself does NOT go through API.Reflection — only Invoke/GetValue/SetValue are
        // checked, whatever the Checked suffix suggests.
        //
        // Resolved once and held, because the scan allocates an array of every member of the type on
        // each call. null when the field is gone — see TryCaretIndex. Wrapped because a throwing
        // static initialiser would be far worse than a null: the CLR caches the
        // TypeInitializationException permanently, and both warning latches below are statics of this
        // same class, so they would die with the fault they exist to report.
        //
        // Cost, measured in game rather than assumed: 3.57 us per read once resolved, and 0.404 ms
        // for the first TryCaretIndex call — which is where the scan actually lands, since the class
        // is beforefieldinit and initialises on first static access rather than at load. That first
        // call may also pay InvokeChecker's one-off walk over every type of every loaded assembly
        // (PugMod.Loader:541-548), but only if this mod is the first thing in the process to touch
        // API.Reflection: the checker is a field of the single shared ModAPIReflection
        // (Pug.Other:392596), so a sibling mod calling first pays it instead. A keystroke costs one
        // read — 0.02 % of a 60 fps frame — and even the first call stays inside one frame. Measured
        // under Wine; no non-Wine baseline was taken, so this is one host's number, not a ratio.
        private static readonly MemberInfo CurrentCharIndexField = ResolveCurrentCharIndexField();

        private static MemberInfo ResolveCurrentCharIndexField()
        {
            try
            {
                return typeof(RadicalMenuOptionTextInput).GetMembersChecked().FirstOrDefault(m => m.GetNameChecked() == "currentCharIndex");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>The caret's character index, read from the row's own counter. Authoritative:
        /// this is the same field vanilla's AppendString inserts at (Pug.Other:343442) and
        /// MoveCharMarker clamps (Pug.Other:343458), so it carries no frame lag — unlike the
        /// blinker, which only catches up once per frame in Update (Pug.Other:343386-343388). It
        /// needs no glyph-space check either, since it is a string index rather than a position
        /// translated into one; it is not thereby guaranteed to be IN range, because only
        /// MoveCharMarker clamps it and a SetText that skips the marker does not (vanilla guards the
        /// same overrun on its own way in, Pug.Other:343431-343434).
        ///
        /// False has two causes and the log tells them apart: an unbound viewport, which says
        /// nothing here because Bind()'s caller already named that wiring fault, and a counter that
        /// could not be read, which warns once. Deliberately no fallback onto the position-based
        /// recovery below: the two answer as of different moments within a frame, and a caller
        /// compensating for that lag (MenuPatch's word jump did) cannot compensate for "whichever
        /// source replied".</summary>
        public bool TryCaretIndex(out int index)
        {
            index = 0;
            // An unbound viewport has no row to ask. Silent here for the same reason IndexSpaceIsSound
            // is: Bind()'s caller logs that wiring fault by name.
            if (_input == null)
                return false;
            if (CurrentCharIndexField == null)
            {
                WarnCaretUnreadableOnce("RadicalMenuOptionTextInput has no member named 'currentCharIndex'");
                return false;
            }

            // A blanket catch, which is right HERE and would not be elsewhere: API.Reflection's read
            // signals every refusal by throwing and never by returning (Pug.Other:392654-392670), so
            // there is no narrower channel to listen on — and the alternative is not a logged
            // exception but a silently swallowed keystroke, because this runs inside a Harmony prefix
            // that returns before vanilla's own AppendString. The name match alone cannot rule any of
            // it out: it matches a member of any KIND, and a member of any TYPE. So a Core Keeper
            // update that keeps the name and changes the shape — a method, a property of another
            // type, an enum, a short — throws here, once per keystroke, where the whole point of the
            // fallback is that a game update costs a convenience rather than the ability to type.
            //
            // e.ToString() rather than e.GetType().Name: Type.Name IS MemberInfo.Name, so the tidier
            // form is a System.Reflection reference and fails the sandbox at compile time
            // (docs/ck/sandbox.md). ToString() is an object override and carries the type anyway.
            object raw;
            try
            {
                raw = CurrentCharIndexField.GetValueChecked(_input);
            }
            catch (System.Exception e)
            {
                WarnCaretUnreadableOnce("reading it threw — " + e.ToString());
                return false;
            }
            if (raw is not int value)
            {
                WarnCaretUnreadableOnce("'currentCharIndex' is no longer an int");
                return false;
            }
            index = value;
            return true;
        }

        /// <summary>Recovers a character index from a position in text-local space — what a mouse
        /// click needs, and the one question the counter above cannot answer, since a pointer has a
        /// position and no index.
        ///
        /// A Try, not a plain int, and that shape is the point: a caller cannot use the value without
        /// having seen the verdict. The int form this replaced could not stop a caller from using an
        /// answer it had no business trusting, and every one of them fed it into a Mathf.Clamp —
        /// which maps anything out of band to 0, the FRONT of the string, i.e. exactly the reversal
        /// the verdict exists to prevent. A sentinel return would have kept that trap open.</summary>
        public bool TryCaretIndexFromLocalX(float localX, out int index)
        {
            index = 0;
            if (!IndexSpaceIsSound())
                return false;
            // Empty text is sound (see below) and 0 is its answer, but the list may still hold a
            // PREVIOUS render's entries, so it must not be consulted here.
            if (_text.GetTextLength() == 0)
                return true;
            var ends = _text.localCharacterEndPositions;
            // The blinker sits at dimensions.xMin + 1/32 plus the previous character's end, so the
            // same two terms come off again before comparing.
            float target = localX - _text.dimensions.xMin - 1f / 32f;
            int best = 0;
            float bestDelta = Mathf.Abs(target);
            for (int i = 0; i < ends.Count; i++)
            {
                float delta = Mathf.Abs(ends[i].x - target);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i + 1;
                }
            }
            index = best;
            return true;
        }

        // Whether an index recovered from a glyph position may be used as a STRING index.
        //
        // Vanilla never has to ask, and neither does TryCaretIndex any more: currentCharIndex is a
        // string index by construction (MoveCharMarker clamps it against pugText.GetTextLength(),
        // Pug.Other:343457-343458), and vanilla indexes localCharacterEndPositions with it directly
        // (Pug.Other:343387). A POSITION is the one thing that counter cannot answer, so a mouse
        // click still has to cross from glyph space into string space — and that crossing rests on
        // an assumption a counter does not make: that entry k of the glyph list ends character k.
        // This method is that assumption, written down and testable.
        //
        // The test is a count, and it is sufficient because the list can only ever come up SHORT.
        // TextManager.Render walks the string it was handed and appends exactly one entry at the
        // bottom of its loop (Pug.Other:350695); every path that leaves the bottom early costs one
        // entry in silence — a character the font has no glyph for (Pug.Other:350600-350602), a pause
        // sign (Pug.Other:350579-350581), a colour tag, whose `i += 2` / `i += 10` (Pug.Other:350588,
        // 350594) swallows several characters for one entry, and the glyph or container pool running
        // dry mid-string (Pug.Other:350534, 350609). Nothing there can add a second entry for one
        // character, so equal counts mean every character got its own entry, in order.
        //
        // Compared against GetText(), NOT against displayedTextString — deliberately, even though
        // displayedTextString is literally the string the list was built from (PugText.Render derives
        // it from textString and then takes font.Render's own formatted result back out into the same
        // field, Pug.Other:351867 and 351902). The callers insert into and scan GetText(), so the
        // property they need is that the glyph space matches THAT string, and ProcessText sits
        // between the two (Pug.Other:351731-351787), where a localisation lookup (351744), a
        // string.Format over the format fields (351766), a capitalisation switch (351776) and
        // textSuffix (351784) can each change the length. Testing the middle link would report sound
        // while the link the callers actually use was broken.
        //
        // Empty text is sound, and it needs stating because the two counts do NOT agree there: a
        // render of an empty string returns before font.Render ever runs (Pug.Other:351862-351866),
        // and the Clear() just above that (Pug.Other:351855) empties the glyphs, the pooled
        // transforms and displayedTextString but leaves localCharacterEndPositions untouched
        // (Pug.Other:351943-351967) — so a field the player has just emptied still carries the
        // previous render's entries. There is no character to be wrong about: 0 is the only index an
        // empty string has, and it is the right one.
        //
        // What this catches in practice, and why it is worth a guard at all: the row's PugText can
        // land on TMP's dynamic-font path (PugText.SetFont, Pug.Other:351532), where the list is
        // filled only under `if (trackDynamicTextCharacterEndPositions)` (Pug.Other:352043) — a flag
        // the row prefab leaves at 0. The list is then EMPTY while the text is not, and every query
        // answers 0 — so a click anywhere in the row would drag the caret to the text's START
        // instead of to what was pointed at, and the next keystroke would land there, silently.
        // Reading the caret rather than reconstructing it has narrowed what this guard stands
        // between: it used to cover the keyboard too, where the unguarded outcome would have been a
        // whole typed word stored in reverse in another mod's config file. Only
        // `item.pugText.localize = false` in ListDetailScreen.AddItem keeps that path shut today
        // (it makes TextManager.ShouldUseDynamicFont return before it even looks at the language,
        // Pug.Other:272035-272038), and that line is there for a localisation-term reason that says
        // nothing about fonts.
        //
        // Two things ride on that one line; only the first was ever written down. The other is
        // dimensions itself: RenderDynamicText builds it from TMP mesh bounds (Pug.Other:352042)
        // rather than from the alignment branches, so ApplyOffset's scroll clamp loses its xMin
        // basis in the same move that empties this list. A second door into that path exists, and
        // the prefab is what holds it shut — SetFont tests isWrittenToByUser before it consults any
        // language at all (Pug.Other:351520), and every PugText here carries 0 there, which is
        // worth knowing on a field whose whole purpose is being written to by the user.
        private bool IndexSpaceIsSound()
        {
            // An unbound viewport has no text to measure against. No warning from here: Bind() logs
            // that specific wiring fault by name, and this would only add a vaguer second line.
            if (_text == null)
                return false;
            int length = _text.GetTextLength();
            if (length == 0)
                return true;
            var ends = _text.localCharacterEndPositions;
            int count = ends != null ? ends.Count : 0;
            if (count == length)
                return true;
            WarnUnsoundOnce(count, length);
            return false;
        }

        // Latched for the session, not per call and not per row. The check above runs on every click
        // into a row, and a row stays unsound for as long as it holds the text that made it so — an
        // unlatched warning would bury the one line that matters under copies of itself, precisely
        // when the log is being read to find it. Spanning rows is intentional too: a drill-in's rows
        // share one prefab and one font, so fifty rows would report one fault fifty times.
        private static bool _warnedUnsound;

        private static void WarnUnsoundOnce(int glyphCount, int textLength)
        {
            if (_warnedUnsound)
                return;
            _warnedUnsound = true;
            Debug.LogWarning(
                "[ModSettingsMenu] Click-to-place-caret is unavailable: PugText reports "
                    + glyphCount
                    + " glyph end positions for "
                    + textLength
                    + " characters, so a click position cannot be turned into a string index. Clicking a row still selects and "
                    + "activates it, but leaves the caret where it was. Typing and word jumps read a separate source and are "
                    + "unaffected by THIS fault — but see any caret-counter warning beside it. No text is lost or reordered. "
                    + "Logged once per session."
            );
        }

        // Same latch, different fault, and kept apart on purpose: this one says the mod lost its
        // grip on a game field, which is a broken mod rather than a row rendering oddly, and the two
        // want different answers from whoever reads the log. The reason is passed in because the
        // three ways to lose that grip — gone, reshaped, refused — want different next steps, and by
        // the time this is read the difference is not recoverable from anything else.
        private static bool _warnedCaretUnreadable;

        private static void WarnCaretUnreadableOnce(string reason)
        {
            if (_warnedCaretUnreadable)
                return;
            _warnedCaretUnreadable = true;
            Debug.LogWarning(
                "[ModSettingsMenu] The caret's counter cannot be read — changed by a Core Keeper update? ("
                    + reason
                    + "). Typing appends at the end of the row instead of at the caret, a word jump falls back to vanilla's own "
                    + "single-character move, and click-to-place-caret does nothing. No text is lost or reordered. Logged once "
                    + "per session."
            );
        }

        /// <summary>Word boundaries either side of an index, for Ctrl+Arrow. The clamp below is now
        /// the only thing bounding <paramref name="fromIndex"/>, and it must stay: TryCaretIndex
        /// returns the row's raw counter, which vanilla itself treats as possibly past the text
        /// (Pug.Other:343431-343434 logs and repairs exactly that), so without the clamp `s[i - 1]`
        /// is an IndexOutOfRangeException. That is the opposite of what this comment used to say,
        /// and the reason changed with it: while the index came from a reconstruction, clamping was
        /// the move that silently turned "no answer" into "the front of the string".</summary>
        public int WordBoundary(int fromIndex, int direction)
        {
            string s = _text != null ? _text.GetText() : "";
            if (string.IsNullOrEmpty(s))
                return 0;
            int i = Mathf.Clamp(fromIndex, 0, s.Length);
            if (direction < 0)
            {
                while (i > 0 && s[i - 1] == ' ')
                    i--;
                while (i > 0 && s[i - 1] != ' ')
                    i--;
            }
            else
            {
                while (i < s.Length && s[i] != ' ')
                    i++;
                while (i < s.Length && s[i] == ' ')
                    i++;
            }
            return i;
        }

        // How far short of the right clip edge the caret is kept while typing — just enough that it
        // is not flush against the mask. A PROPORTIONAL margin (an earlier version used
        // _fieldWidth/5f, taken uncritically from ChatWindow.AdjustInputFieldPosition's own
        // maskWidth/16f ratio) is wrong here: at this field's 15.625-unit width a fifth is ~3.1 units,
        // several characters — the caret then sits far short of the edge with a large blank gap
        // behind it. A small fixed margin keeps that gap proportional to a character, not to the
        // field, which is also what stops it from changing the next time the field is resized.
        //
        // NOT a round number, and that is deliberate, not sloppy: glyphs are point-filtered pixel art
        // at 16 px/unit, _fieldWidth (15.625, i.e. 250 px) is a whole number of pixels, and every caret position
        // (from localCharacterEndPositions) is too — so with a margin of exactly 1, ApplyOffset's
        // offset (fieldWidth - margin - caret) would land EXACTLY on a texel boundary for every caret
        // position once scrolling engages. A point-filtered sprite sitting exactly on that boundary
        // rasterises ambiguously per axis, and individual pixel rows tip into the neighbouring cell —
        // letters visibly come apart (see the project's sprite-on-grid-distortion note). The old
        // proportional margin (4.2) avoided this by accident, only because it happened not to be a
        // whole number; 1.005 makes that avoidance deliberate and keeps it small.
        private const float CaretMarginUnits = 1.005f;

        // Same off-grid reasoning as CaretMarginUnits, applied to the OTHER place ApplyOffset can
        // land the offset: the end-of-text scroll clamp below. _text.dimensions is derived from the
        // same whole-pixel glyph metrics as localCharacterEndPositions, so an UNNUDGED
        // "scroll until the text's own right edge meets the field's" (xMin + width - _fieldWidth,
        // all whole numbers of pixels) would land exactly on a texel boundary — the fragmentation
        // bug again, concentrated at the one caret position instead of spread across the field.
        // Same fix, same already-verified-imperceptible magnitude, applied to the other clamp.
        //
        // "All whole numbers" is itself one of the xMin == 0 assumptions ApplyOffset lists: the
        // style's rightToLeftXOffset is a free float, so a non-zero xMin could put this clamp back
        // on the pixel grid the nudge exists to leave.
        private const float EndOfTextNudge = 0.005f;

        // Ported from ChatWindow.AdjustInputFieldPosition (Pug.Other:317599), which is CK's own
        // horizontal scroll. Vanilla follows the text END because chat only appends — its
        // MoveCharMarker has an empty body. A row's caret can sit anywhere, so this follows the
        // caret instead — but ONLY for the row actually being edited.
        //
        // The blinker, not currentCharIndex, and not because the counter is out of reach — an offset
        // needs a POSITION, and the counter is an index. Update writes the caret's world x into the
        // public blinker every frame (Pug.Other:343386-343388), for EVERY row, not just the one
        // being edited: SetInputText (called by SeedText for every row on open)
        // always leaves currentCharIndex at the text's end (Pug.Other:343539), and nothing moves it
        // again until that row is actually typed into. Following CaretLocalX unconditionally would
        // therefore scroll every untouched row to the END of its text — a list of long tokens shown
        // mid-word instead of from the start. isActive gates that: an inactive row is pinned at
        // offset 0 (hard cut at the mask edge, text from the beginning), which is what a resting row
        // is supposed to show.
        private void ApplyOffset(bool isActive)
        {
            if (_text == null)
                return;

            var t = _text.transform;

            float offset = 0f;
            if (isActive && _blinker != null)
            {
                float caret = CaretLocalX;

                // A viewport is STATEFUL, and that is the whole of this method's design. Deriving the
                // offset as a pure function of the caret — what this did until now — pins the caret to
                // exactly _fieldWidth - CaretMarginUnits, roughly one character from the right edge,
                // for every caret position once scrolling engages at all: correct while typing, wrong
                // for every other way the caret moves. A word jump backwards into the middle of a long
                // token (Alt+Left, the reason word navigation exists here) then showed the caret hard
                // against the right edge with none of the text that FOLLOWS it visible, which is the
                // opposite of what "jump back to look at that" is for.
                //
                // The state is read back off the transform this method writes, deliberately, rather
                // than mirrored into a field: the transform is the only thing on screen, so a second
                // copy could only ever disagree with it — after a rebind, a rebuild, or the isActive
                // reset above, all of which change the offset without going through this branch.
                float scroll = -t.localPosition.x;
                float caretInField = caret - scroll;

                // Hold while the caret is inside the window, move only far enough to bring it back in
                // when it leaves. Both branches place the caret CaretMarginUnits inside the edge it
                // crossed, so a caret that keeps travelling in one direction keeps the window sliding
                // by exactly its own steps, and a reversal costs no motion at all until it reaches the
                // far margin. The left branch additionally keeps the odd margin for the same reason
                // the right one does, not for symmetry's sake: caret positions are whole pixels, so a
                // `caret - 1` there would land this offset EXACTLY on a 1/16 texel boundary and
                // fragment the point-filtered glyphs (see CaretMarginUnits). The .005 is what carries
                // this second code path off the grid too — verified by enumerating both branches and
                // both clamps across the field's whole realistic range.
                if (caretInField > _fieldWidth - CaretMarginUnits)
                    scroll = caret - _fieldWidth + CaretMarginUnits;
                else if (caretInField < CaretMarginUnits)
                    scroll = caret - CaretMarginUnits;

                // Clamped every frame, not just when a branch fired: the held scroll can fall out of
                // range without the caret moving at all, when the text behind it shrinks under a
                // backspace. maxScroll is how far there actually IS to scroll — once the text's own
                // right edge has met the field's, scrolling further only exposes blank space behind
                // it — and the lower bound stops the left branch from scrolling before the text start.
                //
                // That right edge is dimensions.xMin + width, not width alone: scroll comes from
                // CaretLocalX, and the caret's x carries xMin as its basis. The derivation — and what
                // else sets xMin — is docs/ck/ui-framework.md § "The caret's x has a basis, and the
                // render path sets it"; a second copy of it here would age on its own.
                //
                // Numerically this changes nothing today: xMin is 0 for this row, so the expression is
                // bit-identical to the old `width - _fieldWidth`. The line is about the basis, not the
                // result — simplifying it back would silently reinstate the divergence. What holds
                // xMin at 0 is the prefab, and only in part: EditField/Label is authored left-aligned
                // with rightToLeftXOffset 0, but also with invertHorizontalAlignment set — the flag a
                // shipped RTL language would use to turn it right, and xMin with it to exactly -width.
                //
                // Still reading the text as starting at 0, deliberately: the Clamp's lower bound, an
                // inactive row's resting offset, and the [0, _fieldWidth] window caretInField is
                // measured against. The two ends of this Clamp therefore agree only while xMin >= 0;
                // below that the lower bound would be wrong too, which is the larger change this is
                // not. _fieldOriginX is a fifth term of the same kind, and a likelier one to move: the
                // window's left edge is 0 only because FieldMask happens to be centred on half its own
                // width, which an Editor nudge undoes without touching anything here.
                float maxScroll = Mathf.Max(0f, _text.dimensions.xMin + _text.dimensions.width - _fieldWidth + EndOfTextNudge);
                offset = -1f * Mathf.Clamp(scroll, 0f, maxScroll);
            }

            float delta = offset - t.localPosition.x;
            if (Mathf.Approximately(delta, 0f))
                return;
            t.localPosition = new Vector3(offset, t.localPosition.y, t.localPosition.z);

            // Keep the blinker in step WITHIN this frame. base.Update() (Pug.Other:343386-343388)
            // placed it from the text's position as it stood at the START of the frame — i.e. before
            // the move above — so without this the drawn caret sits `delta` away from the character
            // it marks until the next base.Update().
            //
            // That is now the whole of the reason, and it is a rendering one: the blinker is the
            // caret the PLAYER sees. This used to guard an index as well, back when the insertion
            // point was recovered from the blinker's position — a held arrow key could land a
            // HandleTypingInput read inside the un-nudged window and insert at the wrong index.
            // Nothing reads an index off the blinker any more (TryCaretIndex reads the counter), and
            // CaretLocalX has exactly one consumer left: the offset computed above, which is why the
            // nudge stays despite its original hazard being gone.
            //
            // This is a WITHIN-FRAME correction, not an accumulating one: base.Update() overwrites
            // the blinker's position ABSOLUTELY from currentCharIndex every frame, before this method
            // ever runs, so next frame discards this nudge and recomputes fresh — it cannot drift.
            if (_blinker != null)
            {
                var b = _blinker.transform;
                b.position = new Vector3(b.position.x + delta, b.position.y, b.position.z);
            }
        }

        // The field mask is a child of the row, so it scrolls out of the list with it and would
        // keep clipping outside — text standing past the list edge with no frame around it, since
        // the frame is still governed by the viewport mask and the glyphs deliberately are not.
        // Re-fitting to the intersection every frame is what bounds them vertically.
        private void FitMaskToViewport()
        {
            if (_fieldMask == null || _viewportMask == null)
                return;

            var view = _viewportMask.bounds;
            var origin = _fieldMask.transform.parent.position;

            float minX = Mathf.Max(origin.x + _fieldOriginX, view.min.x);
            float maxX = Mathf.Min(origin.x + _fieldOriginX + _fieldWidth, view.max.x);
            float minY = Mathf.Max(origin.y - _fieldHeight / 2f, view.min.y);
            float maxY = Mathf.Min(origin.y + _fieldHeight / 2f, view.max.y);

            // An empty intersection disables the mask rather than shrinking it to nothing: a
            // VisibleInsideMask renderer with no mask covering it renders nothing, which is exactly
            // the disappearance a fully scrolled-out row should have.
            if (maxX <= minX || maxY <= minY)
            {
                _fieldMask.enabled = false;
                return;
            }
            _fieldMask.enabled = true;

            var t = _fieldMask.transform;
            // World position with a local scale is equivalent only because the row hierarchy carries
            // no scaling. If a parent ever scales, this needs lossyScale.
            t.position = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, t.position.z);
            t.localScale = new Vector3(maxX - minX, maxY - minY, 1f);
        }
    }
}
