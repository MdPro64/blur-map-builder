// CAR SKINS ON PLAIN-PAINT CARS: their body shader (veh_Paint_a_b_c / veh_Paint_a_b) has no picture slot, only a dent and a scratch
// picture. AddSlot switches the body to the livery shaders the game's own livery cars use (veh_Paint_b_c = dent, MESH, scratch;
// veh_Paint_b = dent, scratch, MESH) and points the new picture slot at the body's own scratch picture (the same UV layout as a
// livery). The skin is then written into that scratch picture. Tested in game 2026-11-04 (BMW X5: Blur REBORN + normal Blur).
// Notes: map_builder_csharp\scratch\plain\PLAIN_CAR_SKINS.md
//   - picture numbers in the material values are not picture indexes (that table is unknown), so the slot reuses the scratch number;
//   - every data block ("RA", length, 16, 4 bytes, zeros) starts on the same 16-byte step: after a length change it is padded again;
//   - the file header keeps 4 lengths (offset 12 and three near 12300) that grow with the file.
using System.Text;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public static class PlainSkin
{
    public static string Why = "";
    static readonly byte[] RA = { 0x52, 0x41, 0, 0 };

    sealed class Entry { public string Name = ""; public int NameAt, CountAt, SizeAt, Size, NoAt, OffsAt, No; public List<(string Name, int Type, int At)> Params = new(); }

    static bool IsRa(byte[] d, int at) => at + 4 <= d.Length && d[at] == 0x52 && d[at + 1] == 0x41 && d[at + 2] == 0 && d[at + 3] == 0;
    /// a plain paint shader (veh_Paint_a_b_c, _a_b_c_h, _a_b) whose livery twin the game has (veh_Paint_b_c, _b_c_h, _b)
    static bool IsPlain(string fx) => fx.StartsWith("veh_paint_a_", StringComparison.OrdinalIgnoreCase) && new[] { "veh_paint_b_c.fx", "veh_paint_b_c_h.fx", "veh_paint_b.fx" }.Contains(Livery(fx).ToLowerInvariant())
        && !(Environment.GetEnvironmentVariable("BMB_PLAIN_NOH") == "1" && fx.EndsWith("_h.fx", StringComparison.OrdinalIgnoreCase))      // (test switch)
        && !(Environment.GetEnvironmentVariable("BMB_PLAIN_ONLYH") == "1" && !fx.EndsWith("_h.fx", StringComparison.OrdinalIgnoreCase));  // (test switch)
    /// the livery shader's slots: b_c / b_c_h = dent, MESH, scratch; b = dent, scratch, MESH
    static bool MeshSecond(string fx) => Livery(fx).Contains("_c", StringComparison.OrdinalIgnoreCase);

    /// the body's scratch picture (the one a skin goes into), or null when the car has none big enough
    public static string? ScratchPicture(byte[] d)
    {
        string s = B.Latin1.GetString(d);
        string? best = null; int area = 0;
        foreach (var n in Regex.Matches(s, "([A-Za-z0-9_]{3,})RA\x01").Select(m => m.Groups[1].Value).Distinct())
        {
            string l = n.ToLowerInvariant();
            if (!l.Contains("scratch") || l.Contains("smashed") || l.Contains("glass")) continue;
            var lv = Skins.Levels(d, n);
            if (lv.Count == 0) continue;
            int a = lv.Max(q => q.W * q.H);
            if (a > area) { area = a; best = n; }
        }
        return area >= 512 * 512 ? best : null;
    }

    /// does the body wear a plain paint (and can get a picture slot)?
    public static bool CanAdd(byte[] d, bool decalLivery = false) { try { return AddSlot(d, decalLivery) != null; } catch (Exception) { return false; } }

    /// a copy of the model whose plain body paint has a picture slot (= the scratch picture). null when the model is not like that.
    public static byte[]? AddSlot(byte[] d0, bool decalLivery = false)
    {
        // the skin follows the scratch layout: where a body mesh keeps its UV in the scratch numbers (the last two), copy them
        // into the livery numbers (the first two), which the new picture slot reads
        var d = (byte[])d0.Clone();
        try
        {
            if (Environment.GetEnvironmentVariable("BMB_PLAIN_NOUV") != "1")          // (test switch: leave the UV numbers alone)
            foreach (var m in Skins.BodyMeshes(d0, CpModel.Read(d0), plain: true))
                if (m.Pair == 2 || m.S != 1 || m.Ou != 0 || m.Ov != 0)
                    foreach (int v in m.Verts)
                    {
                        int at = m.Vs.Start + (m.VBase + v) * m.Vs.Bytes + m.UvAt;
                        if (m.S == 1 && m.Ou == 0 && m.Ov == 0) { Array.Copy(d0, at + 4, d, at, 4); continue; }     // the scratch UV as it is
                        var (u, q) = m.Uv(d0, v);                                                                 // a moved piece
                        B.Put(d, at, BitConverter.HalfToUInt16Bits((Half)u)); B.Put(d, at + 2, BitConverter.HalfToUInt16Bits((Half)q));
                    }
        }
        catch (Exception) { Why = "uv"; return null; }
        int I(int o) => B.I32(d, o);
        int L0 = d.Length;
        // header lengths
        var sizeFields = new List<int> { 12 };
        // three lengths 16 and 52 bytes apart that all end at the same place (their place + their value)
        for (int f = 2000; f + 56 <= Math.Min(L0, 40000) && sizeFields.Count == 1; f++)
        {
            long end = (long)f + I(f);
            if (end > f + 100000 && end <= L0 && f + 16 + I(f + 16) == end && f + 52 + I(f + 52) == end) sizeFields.AddRange(new[] { f, f + 16, f + 52 });
        }
        if (sizeFields.Count != 4) { Why = "step 1"; return null; }

        // material entries
        int k = IndexOf(d, Encoding.ASCII.GetBytes("InstanceReset"), 0);
        if (k < 12) { Why = "step 2"; return null; }
        int nsh = I(k - 12), p = k - 8;
        var ents = new List<Entry>();
        for (int i = 0; i < nsh; i++)
        {
            p += 4; int nl = I(p); p += 4;
            if (nl == 0) { p += 32; ents.Add(new Entry()); continue; }
            if (nl < 0 || nl > 256) { Why = "step 3"; return null; }
            var e = new Entry { NameAt = p - 4, Name = B.Latin1.GetString(d, p, nl) };
            p += nl; p += 4; e.CountAt = p; int np = I(p); p += 4;
            for (int j = 0; j < np; j++) { int a = p; p += 4; int ln = I(p); e.Params.Add((B.Latin1.GetString(d, p + 4, ln), I(p + 4 + ln), a)); p += 4 + ln + 8; }
            e.SizeAt = p; e.Size = I(p); p += 8; p += 4; e.NoAt = p; int no = I(p); p += 4;
            e.OffsAt = p; e.No = no; p += 2 * (no + 1); p += 6;
            ents.Add(e);
        }
        // the shader list (in order)
        int head = Math.Min(L0, 30000);
        var fxList = new List<string>();
        foreach (Match fm in Regex.Matches(B.Latin1.GetString(d, 0, head), "(veh_[A-Za-z_]+\\.fx)RA")) fxList.Add(fm.Groups[1].Value);
        int FxOf(Entry e) => e.Name == "" ? -1 : B.U16(d, e.OffsAt + 2 * e.No);
        // a car that already has the livery twin of a plain shader (a few livery parts: the Mustang GTR's decal): its plain parts
        // JOIN that livery material (its meshes point at the livery shader and its material values). Adding a second livery
        // material crashed the game, with the same name or not. No bytes are added for these.
        var join = new Dictionary<int, (int Fx, int Tex, int Sh)>();      // plain fx -> livery fx, livery picture material, its SH material
        for (int i = 0; i < fxList.Count; i++)
        {
            if (!IsPlain(fxList[i])) continue;
            int lf = fxList.FindIndex(q => q.Equals(Livery(fxList[i]), StringComparison.OrdinalIgnoreCase));
            if (lf < 0) continue;
            int lt = ents.FindIndex(e => FxOf(e) == lf && e.Params.Any(q => q.Name == "g_MeshTexture") && e.Params.Any(q => q.Name == "g_ScratchTexture"));
            int ls = ents.FindIndex(e => FxOf(e) == lf && e.Params.Count > 0 && e.Params.All(q => q.Type != 7));
            if (lt >= 0 && ls >= 0) join[i] = (lf, lt, ls);
        }
        // when the car joins a livery material, its other plain shaders (the _h one) join the same one: renaming / growing on such
        // a car crashed the game (Mustang GTR); joining adds and renames nothing
        if (join.Count > 0)
        {
            var main = join.Values.OrderBy(v => fxList[v.Fx].Contains("_c", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First();
            for (int i = 0; i < fxList.Count; i++)
                if (IsPlain(fxList[i]) && !join.ContainsKey(i) && MeshSecond(fxList[i])) join[i] = main;
        }
        var targets = new HashSet<int>();
        for (int i = 0; i < ents.Count; i++)
            if (IsPlain(ents[i].Name) && !join.ContainsKey(FxOf(ents[i])) && ents[i].Params.Select(q => q.Name).SequenceEqual(new[] { "g_DentTexture", "g_ScratchTexture" })) targets.Add(i);
        // livery paint whose livery is only decals: its picture slot reads the scratch picture too (the skin covers the body)
        var livTargets = new HashSet<int>();
        for (int i = 0; i < ents.Count; i++)
            if (ents[i].Name.Contains("paint", StringComparison.OrdinalIgnoreCase) && ents[i].Params.Any(q => q.Name == "g_MeshTexture") && ents[i].Params.Any(q => q.Name == "g_ScratchTexture")) livTargets.Add(i);
        if (targets.Count == 0 && join.Count == 0 && (!decalLivery || livTargets.Count == 0)) { Why = "step 4"; return null; }
        if (!decalLivery) livTargets.Clear();

        // material values (after the mesh list)
        p += 12; int nmesh = I(p); p += 4;
        if (nmesh <= 0 || nmesh > 5000) { Why = "step 5"; return null; }
        var meshAt = new List<int>(); var meshRecs = new List<List<int>>(); var meshTris = new List<int>();
        for (int i = 0; i < nmesh; i++)
        {
            meshAt.Add(p); p += 24 + 24 + 32; int n1 = I(p); meshTris.Add(n1 > 0 ? I(p + 4 + 8) : 0); p += 4 + n1 * 20; p += 4; int n2 = I(p); p += 4;
            var rs = new List<int>(); for (int j = 0; j < n2; j++) rs.Add(p + 32 * j); meshRecs.Add(rs); p += 32 * n2;
        }
        p += 24;
        int cntAt = p, cnt = I(p), total = I(p + 4); p += 8;
        // which picture number the new slots get: the scratch number the painted parts use most. A car's numbers can point at
        // different pictures, and some cannot be a body picture (the Mustang GTR's number 1 crashed the game, its number 2 works)
        var useCount = new Dictionary<int, int>();
        var pairs = new List<(int Dent, int Scr, int Ent)>();             // each paint instance's dent + scratch numbers
        var instOf = new Dictionary<int, List<int>>();                   // material -> its instances
        var instPair = new Dictionary<int, (int Dent, int Scr)>();       // instance -> its dent + scratch numbers
        {
            int q = p;
            for (int n = 0; n < cnt; n++)
            {
                int ei = I(q); q += 4;
                if (ei < 0 || ei >= ents.Count) break;
                if (!instOf.TryGetValue(ei, out var li)) instOf[ei] = li = new(); li.Add(n);
                var e = ents[ei];
                int dentV = -1, scrV = -1;
                for (int j = 0; j < e.Params.Count; j++)
                {
                    if (IsRa(d, q)) { q += 12; continue; }
                    if (e.Name.Contains("paint", StringComparison.OrdinalIgnoreCase)) { if (e.Params[j].Name == "g_ScratchTexture") scrV = I(q); if (e.Params[j].Name == "g_DentTexture") dentV = I(q); }
                    int off = B.U16(d, e.OffsAt + 2 * j), nxt = j + 1 < e.Params.Count ? B.U16(d, e.OffsAt + 2 * (j + 1)) : e.Size;
                    q += Math.Max(0, nxt - off);
                }
                if (scrV >= 0) { pairs.Add((dentV, scrV, ei)); useCount[scrV] = useCount.GetValueOrDefault(scrV) + 1; instPair[n] = (dentV, scrV); }
            }
        }
        if (useCount.Count == 0) { Why = "no scratch"; return null; }
        // which number is the real scratch picture: the scratch number that the most plain-paint triangles use. Some cars also
        // pair two filler pictures (DefaultColour) for a few parts (the Camaro SS Drag's 2 + 3, the F-150 Off-Road's 0 + 1),
        // and a joined livery material can have its own scratch number (the Bronco Off-Road's 3, the Beetle Off-Road's 6):
        // those showed no skin. (The Mustang GTR's livery number 2 is also its body's.)
        int pick;
        var livPairs = pairs.Where(q => join.Values.Any(j => j.Tex == q.Ent)).ToList();
        {
            var tris = new Dictionary<int, long>();
            for (int i = 0; i < meshAt.Count; i++)
            {
                int f = I(meshAt[i] + 4);
                if (f < 0 || f >= fxList.Count || !fxList[f].StartsWith("veh_paint_a", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (int rec in meshRecs[i])
                    if (instPair.TryGetValue(I(rec + 24), out var pr)) { tris[pr.Scr] = tris.GetValueOrDefault(pr.Scr) + meshTris[i]; break; }
            }
            if (tris.Count > 0) pick = tris.OrderByDescending(t => t.Value).ThenBy(t => t.Key).First().Key;
            else if (livPairs.Count > 0) pick = livPairs[0].Scr;
            else
            {
                int dent0 = pairs.Where(q => q.Dent >= 0).Select(q => q.Dent).DefaultIfEmpty(-1).Min();
                pick = pairs.Where(q => q.Dent == dent0).GroupBy(q => q.Scr).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            }
        }
        // a material that grows must have ONE instance (the Mustang GTR crashed when a grown material had two). Its other
        // instances move to a spare plain material of the same size (one the join left unused) and their parts use the first
        // instance. With no spare, that material stays plain.
        var spare = Enumerable.Range(0, ents.Count).FirstOrDefault(i => !targets.Contains(i) && ents[i].Params.Select(q => q.Name).SequenceEqual(new[] { "g_DentTexture", "g_ScratchTexture" }) && ents[i].Size == 8, -1);
        foreach (int t in targets.ToList())
            if (instOf.TryGetValue(t, out var li) && li.Count > 1 && spare < 0) targets.Remove(t);
        var extraInst = new Dictionary<int, int>();                      // extra instance -> the first instance of its material
        foreach (int t in targets)
            if (instOf.TryGetValue(t, out var li))
            {
                int keep = li.FirstOrDefault(n => instPair.TryGetValue(n, out var pr) && pr.Scr == pick, li[0]);   // the instance with the skin's numbers stays
                foreach (int n in li.Where(n => n != keep)) extraInst[n] = keep;
            }
        if (targets.Count == 0 && join.Count == 0 && (!decalLivery || livTargets.Count == 0)) { Why = "step 4b"; return null; }
        var skinNum = BitConverter.GetBytes(pick);
        var edits = new List<(int At, int Remove, byte[] New)>();
        var seen = new HashSet<int>();
        int found = 0;
        var seenLiv = new HashSet<int>();
        var instEnt = new List<int>(); var instAt = new List<Dictionary<string, int>>();
        for (int n = 0; n < cnt; n++)          // every instance: a material can be used by several (each grows by 4)
        {
            int ei = I(p); p += 4;
            if (ei < 0 || ei >= ents.Count) { Why = "step 6"; return null; }
            var e = ents[ei];
            int start = p; var at = new Dictionary<string, int>();
            instEnt.Add(ei); instAt.Add(at);
            if (extraInst.ContainsKey(n))
            {
                edits.Add((p - 4, 4, BitConverter.GetBytes(spare)));             // now an instance of the spare plain material
                p += 8; continue;
            }
            if (targets.Contains(ei))
            {
                var num = skinNum;
                edits.Add(MeshSecond(e.Name) ? (p + 4, 0, num) : (p + 8, 0, num));   // b_c: dent MESH scratch; b: dent scratch MESH
                seen.Add(ei); found++; p += 8; continue;
            }
            for (int j = 0; j < e.Params.Count; j++)
            {
                if (IsRa(d, p)) { p += 12; continue; }
                at[e.Params[j].Name] = p;
                int off = B.U16(d, e.OffsAt + 2 * j), nxt = j + 1 < e.Params.Count ? B.U16(d, e.OffsAt + 2 * (j + 1)) : e.Size;
                if (nxt < off) { Why = "step 7"; return null; }
                p += nxt - off;
            }
            if (livTargets.Contains(ei) && at.TryGetValue("g_MeshTexture", out int mAt) && at.TryGetValue("g_ScratchTexture", out int sAt))
            {
                edits.Add((mAt, 4, skinNum));                                  // the livery slot reads the scratch picture
                seenLiv.Add(ei);
            }
        }
        if (seen.Count != targets.Count) { Why = "step 8"; return null; }
        foreach (var rs in meshRecs)
            foreach (int rec in rs)
                if (extraInst.TryGetValue(I(rec + 24), out int first)) edits.Add((rec + 24, 4, BitConverter.GetBytes(first)));
        var joinedSlot = new HashSet<int>();
        foreach (var (pf, (lf, lt, ls)) in join)
        {
            int texInst = instEnt.IndexOf(lt), shInst = instEnt.IndexOf(ls);
            if (texInst < 0 || shInst < 0) { Why = "join"; return null; }
            // the livery material's picture slot reads the scratch picture (the skin)
            if (!instAt[texInst].TryGetValue("g_MeshTexture", out int mAt) || !instAt[texInst].TryGetValue("g_ScratchTexture", out int sAt)) { Why = "join values"; return null; }
            if (joinedSlot.Add(mAt)) edits.Add((mAt, 4, skinNum));                     // (several plain shaders can join one livery material)
            for (int i = 0; i < meshAt.Count; i++)
            {
                if (I(meshAt[i] + 4) != pf) continue;
                edits.Add((meshAt[i] + 4, 4, BitConverter.GetBytes(lf)));                      // the livery shader
                foreach (int rec in meshRecs[i])
                {
                    int inst = I(rec + 24);
                    if (inst < 0 || inst >= instEnt.Count) continue;
                    var ie = ents[instEnt[inst]];
                    if (FxOf(ie) != pf) continue;
                    edits.Add((rec + 24, 4, BitConverter.GetBytes(ie.Params.Any(q => q.Type == 7) ? texInst : shInst)));   // its material values
                }
            }
        }
        edits.Add((cntAt + 4, 4, BitConverter.GetBytes(total + 4 * found)));

        // the new name must not be the same text as a shader already in the list: then it is written in other letters' case
        string NewName(string old)
        {
            string nw = Livery(old);
            if (!fxList.Contains(nw)) return nw;
            if (!fxList.Contains(nw.ToLowerInvariant())) return nw.ToLowerInvariant();
            return nw.ToUpperInvariant();
        }
        var mesh = Cat(RA, BitConverter.GetBytes(13), Encoding.ASCII.GetBytes("g_MeshTexture"), BitConverter.GetBytes(7), BitConverter.GetBytes(1));
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            if (!IsPlain(e.Name) || join.ContainsKey(FxOf(e))) continue;           // joined: the plain material stays as it is (unused)
            string nw = NewName(e.Name);
            edits.Add((e.NameAt, 4 + e.Name.Length, Cat(BitConverter.GetBytes(nw.Length), Encoding.ASCII.GetBytes(nw))));
            if (!targets.Contains(i)) continue;
            var fx = d.AsSpan(e.OffsAt + 4, 2).ToArray();
            if (MeshSecond(e.Name)) { edits.Add((e.Params[1].At, 0, mesh)); edits.Add((e.SizeAt, 4, BitConverter.GetBytes(e.Size + 4))); }
            else edits.Add((e.SizeAt, 4, Cat(mesh, BitConverter.GetBytes(e.Size + 4))));
            edits.Add((e.NoAt, 4, BitConverter.GetBytes(3)));
            edits.Add((e.CountAt, 4, BitConverter.GetBytes(3)));
            edits.Add((e.OffsAt, 6, Cat(BitConverter.GetBytes((ushort)0), BitConverter.GetBytes((ushort)4), BitConverter.GetBytes((ushort)8), fx)));
        }
        // the shader list near the top. When the car already has the livery shader (a few livery parts), the plain parts use that
        // one: a second copy of the same name in the list crashes the game
        foreach (var old in fxList.Where((f, i) => IsPlain(f) && !join.ContainsKey(i)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pat = Cat(BitConverter.GetBytes(old.Length), Encoding.ASCII.GetBytes(old), new byte[] { 0x52, 0x41 });
            for (int at = IndexOfCi(d, pat, 0, head); at >= 0; at = IndexOfCi(d, pat, at + 1, head))
            {
                string nw = NewName(B.Latin1.GetString(d, at + 4, old.Length));
                edits.Add((at, 4 + old.Length, Cat(BitConverter.GetBytes(nw.Length), Encoding.ASCII.GetBytes(nw))));
            }
        }

        // aligned data blocks: pad again so each starts on the same 16-byte step
        var blocks = new List<int>();
        for (int at = 0; at + 12 <= L0; at++)
            if (d[at] == 0x52 && d[at + 1] == 0x41 && d[at + 2] == 0 && d[at + 3] == 0 && d[at + 8] == 16 && d[at + 9] == 0 && d[at + 10] == 0 && d[at + 11] == 0) blocks.Add(at + 12);
        int firstData = CpModel.Read(d0).VStreams.Count > 0 ? CpModel.Read(d0).VStreams.Min(v => v.Start) - 64 : 0;   // (d = d0 here: only UV numbers changed)
        var phases = new int[16];
        foreach (int h in blocks) { if (h < firstData) continue; int q = h; while (q < L0 && d[q] == 0) q++; if (q - h >= 4 && q - h <= 21) phases[q % 16]++; }
        int phase = Array.IndexOf(phases, phases.Max());
        // the data starts on the NEXT 16-byte step after its 4-byte field: 1 to 16 bytes of zeros, never 0 (checked on 520 blocks of 47 cars)
        int Align(int x) => x + 16 - (((x - phase) % 16) + 16) % 16;
        var good = new List<(int H, int Z)>();
        foreach (int h in blocks)
        {
            // the pictures stored before the vertex data have fixed records (their zero bytes are no padding): only from the first
            // vertex block on is a block padded to the 16-byte step (the X5 / Mustang tests showed both)
            if (h < firstData) continue;
            int data = Align(h + 4); if (data <= L0 && d.AsSpan(h, data - h).IndexOfAnyExcept((byte)0) < 0) good.Add((h, data - h)); }
        if (good.Count == 0) { Why = "step 9"; return null; }
        edits.Sort((a, b) => a.At.CompareTo(b.At));
        int delta = 0, ix = 0;
        var pads = new List<(int, int, byte[])>();
        foreach (var (h, z) in good)
        {
            while (ix < edits.Count && edits[ix].At < h) { delta += edits[ix].New.Length - edits[ix].Remove; ix++; }
            int nz = Align(h + delta + 4) - (h + delta);
            if (nz != z) { pads.Add((h, z, new byte[nz])); delta += nz - z; }
        }
        edits.AddRange(pads);

        // apply (a stable sort by place; same place: in the order added)
        var order = edits.Select((e, i) => (e, i)).OrderBy(q => q.e.At).ThenBy(q => q.i).Select(q => q.e).ToList();
        var o = new List<byte>(L0 + 1024);
        int pos = 0, grow = 0;
        foreach (var (at, rem, nw) in order)
        {
            if (at < pos) { Why = "step 10"; return null; }                       // two edits over the same bytes: not a layout we know
            o.AddRange(new ArraySegment<byte>(d, pos, at - pos));
            o.AddRange(nw); pos = at + rem; grow += nw.Length - rem;
        }
        o.AddRange(new ArraySegment<byte>(d, pos, L0 - pos));
        var r = o.ToArray();
        foreach (int f in sizeFields) B.Put(r, f, (uint)(B.I32(r, f) + grow));
        CpModel.Read(r);                                     // must still read as a model
        return r;
    }

    /// veh_Paint_a_b_c.fx -> veh_Paint_b_c.fx, keeping the letters' case ("veh_paint_..." stays small)
    static string Livery(string fx) => fx.Remove("veh_Paint_".Length, 2);

    static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static int IndexOf(byte[] d, byte[] pat, int from) { int i = d.AsSpan(from).IndexOf(pat); return i < 0 ? -1 : from + i; }
    static int IndexOfCi(byte[] d, byte[] pat, int from, int end)
    {
        for (int i = from; i + pat.Length <= end; i++)
        {
            int j = 0;
            for (; j < pat.Length; j++) { byte a = d[i + j], b = pat[j]; if (a != b && char.ToLowerInvariant((char)a) != char.ToLowerInvariant((char)b)) break; }
            if (j == pat.Length) return i;
        }
        return -1;
    }
}
