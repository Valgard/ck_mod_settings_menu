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
        private PugText _text;
        private SpriteMask _fieldMask;
        private SpriteMask _viewportMask;
        private CharacterMarkBlinker _blinker;
        private float _fieldWidth;
        private float _fieldHeight;
        private float _fieldOriginX;

        // The field rectangle comes from the mask's OWN prefab transform, never from the frame.
        // The frame is 22 units centred at 10.5 and spans [-0.5, 21.5], while the text starts at 0 —
        // sizing the clip from it lets the text run half a unit PAST the frame, which is what a first
        // attempt did. The prefab mask is authored at 21 units from 0: the window the old maxWidth
        // used to define.
        //
        // Read once, here: re-fitting moves the mask every frame, so its live transform stops being
        // a witness to its authored geometry after the first Tick.
        public void Bind(PugText text, SpriteMask fieldMask, SpriteMask viewportMask, CharacterMarkBlinker blinker)
        {
            _text = text;
            _fieldMask = fieldMask;
            _viewportMask = viewportMask;
            _blinker = blinker;
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
        }

        /// <summary>The caret's x in text-local space — independent of the offset applied below,
        /// which is what keeps the calculation non-circular.</summary>
        public float CaretLocalX => _blinker != null && _text != null ? _blinker.transform.position.x - _text.transform.position.x : 0f;

        /// <summary>The caret's character index, recovered from the blinker's position because the
        /// base class keeps currentCharIndex private. localCharacterEndPositions is public and is
        /// the same list CK's own Update uses to place that blinker. False means the recovery is not
        /// trustworthy right now — see IndexSpaceIsSound.</summary>
        public bool TryCaretIndex(out int index) => TryCaretIndexFromLocalX(CaretLocalX, out index);

        /// <summary>Recovers a character index from a position in text-local space. TryCaretIndex is
        /// this asked about the caret itself; a mouse click asks about the pointer.
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

        // Whether an index recovered from a glyph position may be used as a STRING index — decided
        // here and nowhere else, so all three callers answer the question the same way.
        //
        // Vanilla never has to ask. RadicalMenuOptionTextInput holds currentCharIndex as a string
        // index (MoveCharMarker clamps it against pugText.GetTextLength(), Pug.Other:343457-343458)
        // and indexes localCharacterEndPositions with it directly (Pug.Other:343387). That field is
        // private and the Roslyn sandbox forbids reflection, so this class reconstructs the index
        // from the caret's POSITION instead — and a reconstruction rests on an assumption an
        // authoritative read does not make: that entry k of the glyph list ends character k. This
        // method is that assumption, written down and testable.
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
        // the row prefab leaves at 0. The list is then EMPTY while the text is not, every query
        // answers 0, and each keystroke inserts at the front: "abc" typed left to right is stored as
        // "cba", in another mod's config file, with no log line and no exception. Only
        // `item.pugText.localize = false` in ListDetailScreen.AddItem keeps that path shut today
        // (it makes TextManager.ShouldUseDynamicFont return before it even looks at the language,
        // Pug.Other:272035-272038), and that line is there for a localisation-term reason that says
        // nothing about fonts.
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

        // Latched for the session, not per call and not per row. The check above runs on every
        // keystroke, every word jump and every click, and a row stays unsound for as long as it holds
        // the text that made it so — an unlatched warning would bury the one line that matters under
        // thousands of copies of itself, precisely when the log is being read to find it. Spanning
        // rows is intentional too: a drill-in's rows share one prefab and one font, so fifty rows
        // would report one fault fifty times.
        private static bool _warnedUnsound;

        private static void WarnUnsoundOnce(int glyphCount, int textLength)
        {
            if (_warnedUnsound)
                return;
            _warnedUnsound = true;
            Debug.LogWarning(
                "[ModSettingsMenu] Caret index recovery is unavailable: PugText reports "
                    + glyphCount
                    + " glyph end positions for "
                    + textLength
                    + " characters, so a position cannot be turned into a string index. Typing appends at the end of the row "
                    + "instead of at the caret; word jumps and click-to-place-caret do nothing. No text is lost or reordered. "
                    + "Logged once per session."
            );
        }

        /// <summary>Word boundaries either side of an index, for Ctrl+Arrow. <paramref
        /// name="fromIndex"/> must be an index TryCaretIndex vouched for; the clamp below is a bound
        /// on a trusted value, not a way to make an untrusted one usable — clamping a recovered index
        /// is what silently turns "no answer" into "the front of the string".</summary>
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
        // maskWidth/16f ratio) is wrong here: at this field's 21-unit width a fifth is ~4.2 units,
        // roughly ten characters — the caret then sits far short of the edge with a large blank gap
        // behind it. A small fixed margin keeps that gap proportional to a character, not to the
        // field.
        //
        // NOT a round number, and that is deliberate, not sloppy: glyphs are point-filtered pixel art
        // at 16 px/unit, _fieldWidth (21) is itself a whole number of pixels, and every caret position
        // (from localCharacterEndPositions) is too — so with a margin of exactly 1, ApplyOffset's
        // offset (fieldWidth - margin - caret) would land EXACTLY on a texel boundary for every caret
        // position once scrolling engages. A point-filtered sprite sitting exactly on that boundary
        // rasterises ambiguously per axis, and individual pixel rows tip into the neighbouring cell —
        // letters visibly come apart (see the project's sprite-on-grid-distortion note). The old
        // proportional margin (4.2) avoided this by accident, only because it happened not to be a
        // whole number; 1.005 makes that avoidance deliberate and keeps it small.
        private const float CaretMarginUnits = 1.005f;

        // Same off-grid reasoning as CaretMarginUnits, applied to the OTHER place ApplyOffset can
        // land the offset: the end-of-text scroll clamp below. _text.dimensions.width is derived
        // from the same whole-pixel glyph metrics as localCharacterEndPositions, so an UNNUDGED
        // "scroll until the text's own right edge meets the field's" (textWidth - _fieldWidth, both
        // whole numbers of pixels) would land exactly on a texel boundary — the fragmentation bug
        // again, just concentrated at the one caret position instead of spread across the field.
        // Same fix, same already-verified-imperceptible magnitude, applied to the other clamp.
        private const float EndOfTextNudge = 0.005f;

        // Ported from ChatWindow.AdjustInputFieldPosition (Pug.Other:317599), which is CK's own
        // horizontal scroll. Vanilla follows the text END because chat only appends — its
        // MoveCharMarker has an empty body. A row's caret can sit anywhere, so this follows the
        // caret instead — but ONLY for the row actually being edited.
        //
        // currentCharIndex is private on the base class (Pug.Other:343320), but Update writes the
        // caret's world x into the public blinker every frame (Pug.Other:343386-343388), for EVERY
        // row, not just the one being edited: SetInputText (called by SeedText for every row on open)
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
                float maxScroll = Mathf.Max(0f, _text.dimensions.width - _fieldWidth + EndOfTextNudge);
                offset = -1f * Mathf.Clamp(scroll, 0f, maxScroll);
            }

            float delta = offset - t.localPosition.x;
            if (Mathf.Approximately(delta, 0f))
                return;
            t.localPosition = new Vector3(offset, t.localPosition.y, t.localPosition.z);

            // Keep the blinker in step WITHIN this frame. base.Update() (Pug.Other:343386-343388)
            // placed it from the text's position as it stood at the START of the frame — i.e. before
            // the move above — and CaretLocalX is exactly the difference between the blinker and the
            // text transform, so without this it reads wrong by `delta` until the next base.Update().
            // Hand-typing leaves several frames between keystrokes for that next Update to catch up;
            // a held arrow key at the OS repeat rate, in a field already scrolled past its width, can
            // land a MenuManager.HandleTypingInput read (Pug.Other:269535-269555) inside that window
            // and insert at the wrong index — the same class of bug the caret-insert rewrite closed.
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
