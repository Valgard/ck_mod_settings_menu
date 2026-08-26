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

        /// <summary>Call once per frame from the owning row's Update.</summary>
        public void Tick()
        {
            FitMaskToViewport();
            ApplyOffset();
        }

        /// <summary>The caret's x in text-local space — independent of the offset applied below,
        /// which is what keeps the calculation non-circular.</summary>
        public float CaretLocalX => _blinker != null && _text != null ? _blinker.transform.position.x - _text.transform.position.x : 0f;

        /// <summary>The caret's character index, recovered from the blinker's position because the
        /// base class keeps currentCharIndex private. localCharacterEndPositions is public and is
        /// the same list CK's own Update uses to place that blinker.</summary>
        public int CaretIndex
        {
            get
            {
                var ends = _text != null ? _text.localCharacterEndPositions : null;
                if (ends == null || ends.Count == 0)
                    return 0;
                float target = CaretLocalX - _text.dimensions.xMin - 1f / 32f;
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
        }

        // Ported from ChatWindow.AdjustInputFieldPosition (Pug.Other:317599), which is CK's own
        // horizontal scroll. Vanilla follows the text END because chat only appends — its
        // MoveCharMarker has an empty body. A row's caret can sit anywhere, so this follows the
        // caret instead.
        //
        // currentCharIndex is private on the base class (Pug.Other:343320), but Update writes the
        // caret's world x into the public blinker every frame (Pug.Other:343386-343388). The
        // viewport needs the position, not the index, so no access DLL is required.
        private void ApplyOffset()
        {
            if (_text == null || _blinker == null)
                return;

            float caret = CaretLocalX;
            // Keep a margin so the caret is never flush against the edge while typing.
            float margin = _fieldWidth / 5f;
            float offset = -1f * Mathf.Max(0f, caret - _fieldWidth + margin);

            var t = _text.transform;
            if (!Mathf.Approximately(t.localPosition.x, offset))
                t.localPosition = new Vector3(offset, t.localPosition.y, t.localPosition.z);
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
