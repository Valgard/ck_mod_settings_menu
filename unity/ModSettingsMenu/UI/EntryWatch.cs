using System;
using CoreLib.Data.Configuration;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>One row's subscription to its entry's file, so an outside write reaches the row.
    /// <c>ConfigFile.SettingChanged</c> is the only event reachable over the <c>ConfigEntryBase</c>
    /// MSM holds (the per-entry one lives on <c>ConfigEntry&lt;T&gt;</c>), and it fires for every entry
    /// in the file, so the handler filters on the one entry watched.
    ///
    /// Lifetime: the file outlives every row (rows are rebuilt on each open, the file is cached by
    /// ConfigStore), so a handler left behind keeps a destroyed row alive and fires on it. The
    /// subscribe and the unsubscribe therefore sit in one place and cannot drift apart: <see cref="Watch"/>
    /// first <see cref="Stop"/>s whatever was watched, which makes a second Bind of the same row replace
    /// its subscription rather than add one, and <see cref="Stop"/> removes the one delegate instance
    /// from the file it was added to — not from wherever the entry points now.
    ///
    /// What a row does once its entry was written is the same for both row classes, so it lives here
    /// too, and the rows supply only what differs: how to redraw, and how tall the result is.</summary>
    internal sealed class EntryWatch
    {
        // Typed MonoBehaviour rather than object on purpose: the destroyed-row check below needs
        // UnityEngine.Object's == overload, which a reference typed object would bypass.
        private readonly MonoBehaviour _row;
        private readonly Action _refresh;
        private readonly Func<int> _heightPx;
        private readonly EventHandler<SettingChangedEventArgs> _handler;
        private ConfigFile _file;
        private ConfigEntryBase _entry;
        private SettingDef _def;
        private ModSection _section;

        public EntryWatch(MonoBehaviour row, Action refresh, Func<int> heightPx)
        {
            _row = row;
            _refresh = refresh;
            _heightPx = heightPx;
            _handler = (sender, args) =>
            {
                if (args.ChangedSetting == _entry)
                    OnEntryChanged();
            };
        }

        public void Watch(SettingDef def, ModSection section)
        {
            Stop();
            _def = def;
            _section = section;
            var entry = def?.Entry;
            if (entry == null || entry.ConfigFile == null)
                return;
            _entry = entry;
            _file = entry.ConfigFile;
            _file.SettingChanged += _handler;
        }

        public void Stop()
        {
            if (_file != null)
                _file.SettingChanged -= _handler;
            _file = null;
            _entry = null;
        }

        // The row's entry was written — and most often by the row itself, which is the part that was
        // worth getting right here. SettingWidget.Adjust writes BoxedValue and CoreLib raises the file
        // event synchronously (ConfigEntryBase.cs:42 → :134 → ConfigFile.cs:504, invoked at :517), so
        // this runs INSIDE SettingWidget.Apply, before Adjust's own finally { Refresh(); }. A section reset arrives the
        // same way (SectionReset.cs:56 writes BoxedValue per entry, and RefreshSection then redraws the
        // same rows a second time). The other writers are a second row over the same entry
        // (testDupKeyAccess) and a mod's own gameplay code. All of them land here, which is why this
        // body has to be idempotent rather than transition-shaped. The exception is a write that
        // changes nothing: CoreLib's setter returns before the event when the new value equals the old
        // (ConfigEntryBase.cs:38), so a step that clamps back to where it already was is silent.
        //
        // The row's Refresh already runs the lock appearance in the order its contract needs, so no
        // tint or lock pass here.
        //
        // A row whose screen is not the active one is skipped, not rendered: the list drill-in keeps the
        // settings screen's rows alive but inactive. Two reasons. Rendering them would be wasted work,
        // since the screen's Activate rebuilds every row from the live values on return. And the
        // re-measure below could not work: LinearLayout counts inactive children as zero height, so a
        // layout pass over an inactive hierarchy would size the rows against zero-height siblings.
        //
        // The row's height is measured once, in Populate, so a value that now wraps differently (a
        // Kind == Info row showing a foreign string, a list preview) is re-measured here;
        // ModSettingsScreen.RemeasureRow relays out only when the height moved.
        //
        // Guarded like FollowPermissionChange's stages, and worth naming what can actually throw,
        // because the case this clause used to name was looked for and not reproduced: a Kind whose
        // unboxing cast in SettingWidget's value rendering disagrees with its entry's runtime type would need a producer that does not
        // pin the two together, and both pin them per Kind (ForeignConfigDiscovery.BuildDef's type
        // tests, SectionBuilder's typed overloads). What is reachable is the foreign material this
        // path handles — Label()/ValueLabel() → Loc.TFirstOf → API.Localization, called with term
        // strings composed from a foreign config's file path and keys; PugText.Render on arbitrary
        // foreign text; and RemeasureRow's layout pass below, which runs inside this same try. The
        // casts stay guarded all the same: this costs one row, and ConfigFile would log any of these
        // without saying whose row it was. Unlike a permission transition nothing is consumed here, so
        // the next write to the entry simply tries again.
        private void OnEntryChanged()
        {
            // A destroyed row whose OnDestroy never ran (Unity skips it for an object that was never
            // active) is the one way a handler outlives its row. This ends it at the first write to this
            // row's entry; on an entry nothing writes again it never fires, and the handler stays.
            if (_row == null)
            {
                Stop();
                return;
            }
            try
            {
                if (_row.gameObject.activeInHierarchy)
                {
                    _refresh();
                    _row.GetComponentInParent<ModSettingsScreen>()?.RemeasureRow(_row.gameObject, _heightPx());
                }
            }
            catch (Exception e)
            {
                // The owner, not the key alone: on the discovery path a key is a foreign mod's bare
                // field name, and the fixture set alone has several that repeat across files.
                Debug.LogError($"[ModSettingsMenu] refreshing '{_section?.ModId}/{_def?.Key}' after an outside change failed: {e}");
            }
        }
    }
}
