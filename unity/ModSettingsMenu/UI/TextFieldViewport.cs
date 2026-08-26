using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Horizontal viewport for one text row: keeps the row's field mask inside the list viewport
    /// and offsets the text so the caret stays visible. Standalone rather than part of
    /// ListDetailItem, because SettingKind.Text is a second consumer of the same behaviour.
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
        /// the same list CK's own Update uses to place that blinker.</summary>
        public int CaretIndex => CaretIndexFromLocalX(CaretLocalX);

        /// <summary>Recovers a character index from a position in text-local space. CaretIndex is
        /// this asked about the caret itself; a mouse click asks about the pointer.</summary>
        public int CaretIndexFromLocalX(float localX)
        {
            var ends = _text != null ? _text.localCharacterEndPositions : null;
            if (ends == null || ends.Count == 0)
                return 0;
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
            return best;
        }

        /// <summary>Word boundaries either side of an index, for Ctrl+Arrow.</summary>
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
        private const float CaretMarginUnits = 1f;

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

            float offset = 0f;
            if (isActive && _blinker != null)
            {
                float caret = CaretLocalX;
                offset = -1f * Mathf.Max(0f, caret - _fieldWidth + CaretMarginUnits);
            }

            var t = _text.transform;
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
