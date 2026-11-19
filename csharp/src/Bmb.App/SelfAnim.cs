// The robot test of the 3D tab's Animation sub-tab (BMB_ONLY=anim): makes three moving objects, takes pictures, checks
// the preview follows AnimEval, records keys like G / R would, then undoes everything. Scratch copy only.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    async Task AnimTests(Action<string, bool> Ok, Func<int, Task> Pump, Action<string> Shot, List<string> rep)
    {
        SetMode("3d"); await Pump(500);
        V3.SetSub("anim"); await Pump(300);
        Shot("anim_0_empty");
        Ok("Animation sub-tab: shows", V3.AnimTab);
        // layout: the map panel hides in this sub-tab (wide window), the right column holds the object list + settings
        var size0 = Size; var state0 = WindowState;
        WindowState = FormWindowState.Normal; Size = new Size(1800, 1000); await Pump(400);
        Ok($"layout (wide): the map panel is hidden in Animation ({ClientSize.Width} px)", !_rightP.Visible);
        Shot("anim_0b_wide");
        V3.SetSub("objects"); await Pump(300);
        Ok("layout (wide): the map panel is back in Objects", _rightP.Visible);
        V3.SetSub("anim"); await Pump(300);
        Size = new Size(1024, 700); await Pump(400);
        var col = V3.AnimColBounds();
        Ok($"layout (small): the right column fits ({col.Left}..{col.Right} of {V3.CentreWidth}), settings {col.Width} px, timeline {V3.Timeline.Width} px",
            col.Right <= V3.CentreWidth && col.Width >= 200 && V3.Timeline.Width >= AnimTimeline.LabelW + 150 && V3.OutlinerInColumn);
        Shot("anim_0c_small");
        Size = size0; WindowState = state0; await Pump(400);
        int undo = 0;
        var objs = D.Map.Arr("objects") ?? new();
        var keys = Enumerable.Range(0, objs.Count).Select(i => "obj:" + i).Where(k => V3.AnimNo(k) == null && V3.PickOf(k) != null).Take(3).ToList();
        Ok($"three objects that can move ({keys.Count})", keys.Count == 3);
        if (keys.Count < 3) return;
        double[] P(string k) => (double[])V3.PickOf(k)!.Pos.Clone();
        double Dist(double[] a, double[] b) => Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[2] - b[2], 2));
        var r0 = P(keys[0]); var r1 = P(keys[1]); var r2 = P(keys[2]);

        // 1. keyframes, made the way a player does: select, stopwatch on (first key), move the time marker, G
        V3.Select(keys[0]); await Pump(100);
        V3.Target = (double[])r0.Clone(); V3.Dist = 420; V3.Pitch = 0.7;
        Shot("anim_1_selected_still");
        V3.AnimStartTest(keys[0], "keys"); undo++; await Pump(100);
        Ok("keys: first key at the time marker", AnimEval.Keys(V3.AnimOf(keys[0])!).Count == 1);
        V3.PlayT = 3; V3.AnimTimeMoved();
        Ok("keys: G at 3 s adds a key", V3.AnimRecordTest(keys[0], new[] { 80.0, 0, 0 }, 0)); undo++;
        V3.PlayT = 6; V3.AnimTimeMoved();
        V3.AnimRecordTest(keys[0], new[] { 0.0, 0, 70 }, 0); undo++;
        Ok("keys: 3 keys", AnimEval.Keys(V3.AnimOf(keys[0])!).Count == 3);
        V3.AnimStopwatch(keys[0], "rot"); undo++;
        V3.PlayT = 3; V3.AnimTimeMoved();
        V3.AnimRecordTest(keys[0], new[] { 0.0, 0, 0 }, Math.PI / 2); undo++;
        Ok("keys: R at 3 s adds a turn key", AnimEval.RKeys(V3.AnimOf(keys[0])!).Count == 2);
        V3.AnimFrame(keys[0]);
        V3.PlayT = 1.5; V3.AnimTimeMoved(); await Pump(100);
        Ok("preview: half way to the 2nd key the object has moved", Dist(P(keys[0]), r0) > 20 && Dist(P(keys[0]), r0) < 70);
        Shot("anim_2_keys_1_5s");
        V3.PlayT = 4.5; V3.AnimTimeMoved(); await Pump(100);
        Shot("anim_3_keys_4_5s");

        // 2. a closed path, started by a trigger box
        var sq = new List<object?> { L3(r1[0], r1[1], r1[2]), L3(r1[0] + 60, r1[1], r1[2]), L3(r1[0] + 60, r1[1], r1[2] + 60), L3(r1[0], r1[1], r1[2] + 60) };
        V3.AnimSet(keys[1], new JObj { ["mode"] = "path", ["start"] = "trigger", ["loop"] = true, ["closed"] = true, ["speed"] = 15.0, ["path"] = sq, ["trigger"] = new JObj { ["x"] = r1[0] - 40, ["y"] = r1[1] + 1, ["z"] = r1[2], ["w"] = 40.0, ["h"] = 20.0, ["d"] = 40.0 } }, "test path"); undo++;
        // 3. spin
        V3.AnimSet(keys[2], new JObj { ["mode"] = "spin", ["start"] = "race", ["rpm"] = 30.0 }, "test spin"); undo++;
        Ok("three objects move", V3.AnimatedKeys().Count == 3);
        V3.Select(keys[1]); V3.AnimFrame(keys[1]);
        V3.PlayT = 5; V3.AnimTimeMoved(); await Pump(150);
        var pose = AnimEval.Pose(V3.AnimOf(keys[1])!, 5, r1, 0);
        Ok("path: the object is where AnimEval says (75 m along)", Dist(P(keys[1]), pose.P) < 0.5 && Dist(P(keys[1]), r1) > 10);
        Shot("anim_4_path_trigger");
        V3.Select(keys[2]); await Pump(100);
        Shot("anim_5_spin");

        // timeline: drag a key in time, delete one
        V3.Select(keys[0]); await Pump(100);
        var k0 = AnimEval.Keys(V3.AnimOf(keys[0])!);
        V3.AnimShiftKeys(new() { (keys[0], "keys", k0[1].Num("t")) }, 1.0); undo++;
        Ok("timeline: a dragged key moved to 4 s", AnimEval.Keys(V3.AnimOf(keys[0])!).Any(k => Math.Abs(k.Num("t") - 4) < 1e-6));
        V3.Timeline.SelKeys.Clear(); V3.Timeline.SelKeys.Add((keys[0], "keys", 6.0));
        V3.AnimDeleteKeys(); undo++;
        Ok("timeline: Delete removes the selected key", AnimEval.Keys(V3.AnimOf(keys[0])!).Count == 2);
        // the key editor: one key clicked, its time typed
        var k1 = AnimEval.Keys(V3.AnimOf(keys[0])!)[1];
        V3.Timeline.SelKeys.Clear(); V3.Timeline.SelKeys.Add((keys[0], "keys", k1.Num("t"))); V3.AnimSettings(); await Pump(100);
        Shot("anim_6b_key_editor");
        V3.KeyEditTest(keys[0], "keys", k1.Num("t"), "t", 5.0); undo++;
        Ok("key editor: a typed time moves the key to 5 s", AnimEval.Keys(V3.AnimOf(keys[0])!).Any(k => Math.Abs(k.Num("t") - 5) < 1e-6) && V3.Timeline.SelKeys.Contains((keys[0], "keys", 5.0)));
        V3.Timeline.SelKeys.Clear(); V3.AnimSettings();
        // box-select: a box drawn over both rows picks every key in it
        V3.Timeline.Fit(); V3.Timeline.Invalidate(); await Pump(150);
        var tl = V3.Timeline;
        int yPos = tl.RowY(keys[0], "pos"), yRot = tl.RowY(keys[0], "rot");
        int nIn = AnimEval.Keys(V3.AnimOf(keys[0])!).Count(k => k.Num("t") <= 5.5) + AnimEval.RKeys(V3.AnimOf(keys[0])!).Count(k => k.Num("t") <= 5.5);
        tl.BoxTest(new Point(AnimTimeline.LabelW + 2, yPos + 2), new Point((int)tl.XOf(5.5), yRot + AnimTimeline.RowH - 2)); await Pump(150);
        Shot("anim_6c_box_select");
        Ok($"timeline: a box selects many keys ({tl.SelKeys.Count} of {nIn})", tl.SelKeys.Count == nIn && nIn >= 2);
        tl.SelKeys.Clear(); V3.AnimSettings();
        // zoom keeps 0 at the left; Shift + wheel scrolls
        for (int i = 0; i < 4; i++) tl.WheelTest(tl.Width - 40, 120);
        Ok($"timeline: zooming in keeps 0 at the left ({tl.ViewStart:0.##} s, {tl.ViewLen:0.#} s shown)", tl.ViewStart == 0 && tl.ViewLen < V3.TimeLen());
        tl.WheelTest(tl.Width - 40, -120, true); await Pump(100);
        Ok($"timeline: Shift + wheel scrolls ({tl.ViewStart:0.##} s)", tl.ViewStart > 0);
        Shot("anim_6d_zoomed_scrolled");
        V3.Timeline.Fit();
        V3.PlayT = 2; V3.AnimTimeMoved(); await Pump(150);
        Shot("anim_6_timeline");

        // play
        V3.AnimPlayToggle(); await Pump(1200);
        Ok("Play moves the time marker", V3.PlayT > 0.5 && V3.Playing);
        Shot("anim_7_playing");
        V3.AnimStop();

        // back to Objects: everything stands where it was placed
        V3.SetSub("objects"); await Pump(200);
        Ok("Objects sub-tab: objects back at their places", Dist(P(keys[0]), r0) < 0.01 && Dist(P(keys[1]), r1) < 0.01);
        Shot("anim_8_objects_tab");
        V3.SetSub("anim"); await Pump(100);

        // the export makes game actors for them (the same code Build + install runs)
        try
        {
            var w = Share.Copy(D.World);
            var m = w.Objs("locations").ElementAt(D.Li).Objs("maps").ElementAt(D.Mi);
            var spots = Bmb.Core.Layout.PlacedSpots(m).Tiles.Where(t => t.Sp.Anim != null).ToList();
            Ok($"export: {spots.Count} animated objects reach the build", spots.Count >= 3);
        }
        catch (Exception e) { Ok("export: " + e.Message, false); }

        // drawing a path with the mouse: 3 clicks on the ground, Enter
        V3.Select(keys[2]); V3.Top(); await Pump(150);
        V3.AnimSet(keys[2], null, "test: still again"); undo++;
        V3.AnimStartTest(keys[2], "path"); undo++;
        Ok("path: drawing starts", V3.PathEditing == keys[2]);
        int cx = G.Width / 2, cy = G.Height / 2;
        foreach (var (dx, dy) in new[] { (60, 0), (60, 60), (0, 80) }) { V3.LeftPress(cx + dx, cy + dy); undo++; await Pump(60); }
        Ok($"path: 3 clicks add 3 points ({AnimEval.PathPoints(V3.AnimOf(keys[2])!).Count} in all)", AnimEval.PathPoints(V3.AnimOf(keys[2])!).Count == 4);
        V3.Key(Keys.Enter, false, false, false);
        Ok("path: Enter ends drawing", V3.PathEditing == null);
        Shot("anim_9_path_drawn");

        for (int i = 0; i < undo; i++) Undo();
        await Pump(200);
        Ok("undo: no moving objects left", V3.AnimatedKeys().Count == 0);
        V3.SetSub("objects");
        rep.Add($"undo steps: {undo}");
    }

    static List<object?> L3(double x, double y, double z) => new() { Math.Round(x, 2), Math.Round(y, 2), Math.Round(z, 2) };
}
