namespace ModSettingsMenu.UI
{
    /// <summary>
    /// A text row's field rectangle as the prefab AUTHORED it: a width, a height, and the CENTRE in
    /// the row's local x — not the edge, which is what makes it usable as a collider centre
    /// directly. Produced only by <see cref="TextFieldViewport.TryFieldRect"/>.
    ///
    /// It exists to stop a transposition. The three values were three <c>out float</c>s, and the one
    /// consumer lands them in <c>size.x</c>, <c>size.y</c> and <c>center.x</c> — two x-axis
    /// quantities and one y, in an order nothing enforced. Swapping two compiles, produces a
    /// wrong-shaped click box, and says nothing. Earlier in that same method the frame branch does
    /// the identical job through <c>ModSettingsScreen.FitColliderToFrame</c>, where no
    /// scalar crosses the boundary and the mistake is not expressible; this makes the two paths
    /// equally safe rather than leaving one of them the sharp one.
    ///
    /// Named fields rather than a tuple, for the same reason: <c>(float, float, float)</c> is
    /// positional again the moment it is destructured.
    ///
    /// <para>Returned behind a <c>bool</c> verdict, matching its two siblings on the same class —
    /// <c>TryCaretIndex</c> and <c>TryCaretIndexFromLocalX</c>, the latter of which argues the Try
    /// shape at length. House consistency is the reason; a nullable would in fact make the verdict
    /// harder to ignore, since skipping it would cost a <c>.Value</c>. What the verdict must
    /// therefore carry on its own is this: <c>default(FieldRect)</c> is a zero-width, zero-height
    /// box centred at 0 — not obviously absent, and precisely the "silently unhittable" row the
    /// collider code exists to prevent. A caller that uses the value without reading the verdict
    /// builds exactly that failure.</para>
    ///
    /// <para><see cref="RowSelection"/> beside this file is where the shape comes from: a readonly
    /// struct with named fields. Its answer to the same hazard is the opposite one — absence there
    /// is a null <c>RowSelection?</c>, not a value of the type — so it is a precedent for the shape
    /// and not for this choice.</para>
    /// </summary>
    internal readonly struct FieldRect
    {
        public readonly float Width;
        public readonly float Height;

        /// <summary>The rectangle's centre in the row's local x, not its left edge.</summary>
        public readonly float CenterX;

        public FieldRect(float width, float height, float centerX)
        {
            Width = width;
            Height = height;
            CenterX = centerX;
        }
    }
}
