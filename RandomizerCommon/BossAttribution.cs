using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SoulsFormats;
using YamlDotNet.Serialization;

namespace RandomizerCommon
{
    /// <summary>
    /// Boss attribution (SPEC-boss-attribution.md). Builds the runtime "sweep_flags" map
    ///   { eventFlag : [apLocationId, ...] }
    /// where eventFlag is a boss DefeatFlag OR a Site-of-Grace lit flag. A location may appear
    /// under several flags; the client (flagSentLocations dedupe) sends it the first time ANY of
    /// its flags sets. Tiers, in order:
    ///   1. dungeon containment (minidungeon by Area tag / legacy by Area name) -> that dungeon's boss
    ///   2. nearest field-boss instance (Miniboss/Dragon/Evergaol[/Night]) in the same AP region,
    ///      within FieldCap
    ///   4. else the region capstone = nearest great Boss-class enemy to the region centroid
    /// Grace layer (off|complement|full): each check also gets its nearest grace within GraceCap;
    /// in "complement" only weak-boss checks (capstone-bound or field dist > FieldGood) get one.
    ///
    /// This class owns no GUI state. ArchipelagoForm collects per-check (apLocId, area, worldPos)
    /// and per-grace (litFlag, worldPos) during its existing coord dump, then calls Compute().
    ///
    /// NOTE: positions are approximate where a boss has no MSB part (skipped); region membership
    /// uses on-the-fly AP-region centroids computed from the check positions (same model the
    /// poptracker dry-run validated). Build + verify on Windows; this never ran in the sandbox.
    /// </summary>
    public static class BossAttribution
    {
        public class Options
        {
            public bool IncludeNight = true;     // NightMiniboss in the field pool
            public string GraceMode = "complement"; // off | complement | full
            public double FieldCap = 800;        // max in-region check->field-boss distance (else capstone)
            public double FieldGood = 300;       // boss within this = "well covered" (complement gate)
            public double GraceCap = 180;        // max check->grace distance for a grace trigger
            public bool EnemyRando = false;      // when true, skip the live-MSB position fallback
                                                 // (post-rando positions misattribute; drop-check
                                                 // positions are rando-stable because item lots don't move)
        }

        public class CheckPt { public long ApLocId; public string Area; public Vector3 Pos; }
        public class GracePt { public int Flag; public Vector3 Pos; }

        private class Boss
        {
            public int Id, Flag;
            public string Map, Name;
            public EnemyAnnotations.EnemyClass Cls;
            public Vector3 Pos;
            public bool HasPos;
            public bool Dlc;       // DLC boss (map m20-29 / m40-59 / m61); kept apart from base
                                   // bosses so the m61/m60 global-XZ overlap can't cross-attribute.
            public string Region;
        }

        // Areas with no in-map Boss/MinorBoss get a manual dungeon-boss assignment.
        private static readonly Dictionary<string, int> AreaBossOverride = new()
        {
            ["limgrave_murkwatercave"] = 10000850, // Murkwater Cave (Patches) -> Margit, the Fell Omen
        };

        private static readonly string[] LegacyPrefix =
            { "stormveil", "academy", "leyndell", "volcano", "farumazula", "haligtree", "caria", "mohgwyn",
              // DLC (SOTE) legacy dungeons -> their dungeon boss (verified Boss-class in each map):
              //   belurat m20_00 -> Divine Beast Dancing Lion; enirilim m20_01 -> Radahn Consort;
              //   shadowkeep m21_00 -> Golden Hippopotamus; storehouse m21_01 -> Messmer;
              //   fissure m22_00 -> Putrescent Knight. NOT westrampart (m21_02 has no in-map boss ->
              //   DungeonBossFlag=0=unattributed; left "open" so field/capstone+grace catch it).
              // DLC minidungeons (catacombs/caves/gaols/forges) are already 'minidungeon'-tagged so
              // they reach tier 1 via the "mini" path, not here. Harmless on base-game/DLC-off seeds.
              "belurat", "enirilim", "shadowkeep", "storehouse", "fissure" };

        private static readonly HashSet<EnemyAnnotations.EnemyClass> AnchorClasses = new()
        {
            EnemyAnnotations.EnemyClass.Boss, EnemyAnnotations.EnemyClass.MinorBoss,
        };

        public class Stats
        {
            public int Roster, Positioned, FromDrop, FromMsb, Dungeon, Field, Capstone, Unattributed, GraceTriggers, Regions;
            public override string ToString() =>
                $"roster={Roster} positioned={Positioned} (drop={FromDrop} msb={FromMsb}) regions={Regions} | "
                + $"dungeon={Dungeon} field={Field} capstone={Capstone} unattributed={Unattributed} graceTriggers={GraceTriggers}";
        }

        public static Dictionary<int, List<long>> Compute(
            GameData game, AnnotationData ann, EldenCoordinator coord,
            List<CheckPt> checks, List<GracePt> graces, Options opt,
            Dictionary<int, Vector3> entityPos, out Stats stats, out Dictionary<int, string> flagNames)
        {
            stats = new Stats();
            // ---- field-boss pool ----
            var field = new HashSet<EnemyAnnotations.EnemyClass>
            {
                EnemyAnnotations.EnemyClass.Miniboss,
                EnemyAnnotations.EnemyClass.DragonMiniboss,
                EnemyAnnotations.EnemyClass.Evergaol,
            };
            if (opt.IncludeNight) field.Add(EnemyAnnotations.EnemyClass.NightMiniboss);

            // ---- load enemy.txt (independently; needed even with enemy rando off) ----
            EnemyAnnotations enemyAnn;
            var des = new DeserializerBuilder().Build();
            using (var r = File.OpenText($"{game.Dir}/Base/enemy.txt"))
                enemyAnn = des.Deserialize<EnemyAnnotations>(r);

            // ---- boss roster with world positions ----
            var maps = game.EldenMaps; // computed property: snapshot ONCE (it rebuilds per access)
            var roster = new List<Boss>();
            foreach (var e in enemyAnn.Enemies)
            {
                if (!(AnchorClasses.Contains(e.Class) || field.Contains(e.Class))) continue;
                var b = new Boss { Id = e.ID, Flag = e.DefeatFlag, Map = e.Map, Cls = e.Class, Name = BossName(e), Dlc = IsDlcMap(e.Map) };
                // Primary position source = the boss's own drop-check coords (rando-proof; matches the
                // validated dry-run). Live-MSB lookup is only a fallback (it misses under enemy rando,
                // which relocates/replaces the entity so its original id isn't found).
                if (entityPos != null && entityPos.TryGetValue(e.ID, out var dp)) { b.Pos = dp; b.HasPos = true; stats.FromDrop++; }
                else if (!opt.EnemyRando && TryBossPos(maps, coord, e, out var p)) { b.Pos = p; b.HasPos = true; stats.FromMsb++; }
                roster.Add(b);
            }
            stats.Roster = roster.Count;
            stats.Positioned = roster.Count(b => b.HasPos);

            // ---- region centroids from check positions. region = the check's own OPEN area;
            // mini/legacy areas are handled by tier 1 (DungeonBossFlag) so they're excluded here.
            // This matches the validated boss_attribution_dryrun.py open-AREA centroid model. The
            // old code keyed on RegionOf()=areaAnn.Archipelago, which is the AP item-tracking abbrev
            // and is null for every area in annotations.txt -> regions=0 -> field+capstone empty. ----
            var regionPts = new Dictionary<string, List<Vector3>>();
            var regionDlc = new Dictionary<string, bool>();
            foreach (var c in checks)
            {
                if (c.Area == null || AreaKind(ann, c.Area) != "open") continue;
                if (!regionPts.TryGetValue(c.Area, out var l)) regionPts[c.Area] = l = new();
                l.Add(c.Pos);
                regionDlc[c.Area] = IsDlcArea(ann, c.Area);
            }
            var centroid = regionPts.ToDictionary(
                kv => kv.Key,
                kv => new Vector3(kv.Value.Average(p => p.X), kv.Value.Average(p => p.Y), kv.Value.Average(p => p.Z)));
            stats.Regions = centroid.Count;

            // The DLC overworld (m61) shares global XZ with base m60, so match a check only within its
            // OWN realm: DLC checks -> DLC regions, base checks -> base regions. Without this, a DLC
            // open-region's nearest boss resolves to a BASE boss (e.g. Loretta) sitting at the same XZ.
            string NearestRegion(Vector3 p, bool dlc)
            {
                string best = null; double bd = double.MaxValue;
                foreach (var kv in centroid)
                {
                    if (regionDlc.TryGetValue(kv.Key, out var rd) && rd != dlc) continue;
                    double d = Flat(kv.Value, p);
                    if (d < bd) { bd = d; best = kv.Key; }
                }
                return best;
            }

            // assign each positioned boss a region; index field bosses by region
            var fieldByRegion = new Dictionary<string, List<Boss>>();
            foreach (var b in roster)
            {
                if (!b.HasPos) continue;
                b.Region = NearestRegion(b.Pos, b.Dlc);
                if (field.Contains(b.Cls) && b.Region != null)
                {
                    if (!fieldByRegion.TryGetValue(b.Region, out var l)) fieldByRegion[b.Region] = l = new();
                    l.Add(b);
                }
            }

            // capstone per region = nearest great Boss-class enemy to that region's centroid
            var capstoneFlag = new Dictionary<string, int>();
            foreach (var kv in centroid)
            {
                bool regDlc = regionDlc.TryGetValue(kv.Key, out var rd0) && rd0;
                Boss best = null; double bd = double.MaxValue;
                foreach (var b in roster)
                {
                    if (b.Cls != EnemyAnnotations.EnemyClass.Boss || !b.HasPos || b.Dlc != regDlc) continue;
                    double d = Flat(b.Pos, kv.Value);
                    if (d < bd) { bd = d; best = b; }
                }
                if (best != null) capstoneFlag[kv.Key] = best.Flag;
            }

            // ---- assign each check ----
            var sweep = new Dictionary<int, List<long>>();
            void Add(int flag, long ap)
            {
                if (flag == 0) return;
                if (!sweep.TryGetValue(flag, out var l)) sweep[flag] = l = new();
                l.Add(ap);
            }

            foreach (var c in checks)
            {
                string kind = AreaKind(ann, c.Area);
                int bossFlag = 0; double fieldDist = double.MaxValue; bool capstone = false;

                if (kind == "mini" || kind == "legacy")
                {
                    bossFlag = DungeonBossFlag(c.Area, ann, roster);
                    if (bossFlag != 0) stats.Dungeon++;
                    // else: a dungeon with no in-map Boss/MinorBoss (e.g. DLC ruined forges, or a
                    // Miniboss-only catacomb like Darklight) -> don't strand it; fall through to the
                    // field/capstone path below (the catacomb's own Miniboss is in the field pool).
                }
                if (bossFlag == 0)
                {
                    string reg = NearestRegion(c.Pos, IsDlcArea(ann, c.Area));
                    Boss best = null; double bd = double.MaxValue;
                    if (reg != null && fieldByRegion.TryGetValue(reg, out var cands))
                        foreach (var b in cands) { double d = Flat(b.Pos, c.Pos); if (d < bd) { bd = d; best = b; } }
                    if (best != null && bd <= opt.FieldCap * opt.FieldCap) { bossFlag = best.Flag; fieldDist = Math.Sqrt(bd); stats.Field++; }
                    else
                    {
                        bossFlag = reg != null && capstoneFlag.TryGetValue(reg, out var cf) ? cf : 0;
                        capstone = true;
                        if (bossFlag != 0) stats.Capstone++; else stats.Unattributed++;
                    }
                }
                Add(bossFlag, c.ApLocId);

                // grace complement layer
                if (opt.GraceMode != "off" && graces.Count > 0)
                {
                    int gflag = 0; double gd = double.MaxValue;
                    foreach (var g in graces) { double d = Dist3(g.Pos, c.Pos); if (d < gd) { gd = d; gflag = g.Flag; } }
                    bool weak = capstone || fieldDist > opt.FieldGood;
                    bool want = opt.GraceMode == "full" || (opt.GraceMode == "complement" && weak);
                    if (want && gd <= opt.GraceCap) { Add(gflag, c.ApLocId); stats.GraceTriggers++; }
                }
            }
            // Succinct name per sweep flag (boss name [region] / grace) for the bake log (ap_sweep_diag).
            flagNames = new Dictionary<int, string>();
            foreach (var rb in roster)
                if (rb.Flag != 0 && !flagNames.ContainsKey(rb.Flag))
                    flagNames[rb.Flag] = rb.Name + (string.IsNullOrEmpty(rb.Region) ? "" : " [" + rb.Region + "]");
            foreach (var gp in graces)
                if (!flagNames.ContainsKey(gp.Flag))
                    flagNames[gp.Flag] = "Grace " + gp.Flag;
            return sweep;
        }

        // ---- helpers ----
        private static double Flat(Vector3 a, Vector3 b) { double dx = a.X - b.X, dz = a.Z - b.Z; return dx * dx + dz * dz; }
        private static double Dist3(Vector3 a, Vector3 b)
        { double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        private static string BossName(EnemyAnnotations.EnemyInfo e)
            => !string.IsNullOrEmpty(e.Name) ? e.Name : e.ID.ToString();

        // region = the AP-region (areaAnn.Archipelago) the check's area belongs to
        private static string RegionOf(AnnotationData ann, string area)
        {
            if (area == null) return null;
            if (ann.Areas.TryGetValue(area, out var a) && a.Archipelago != null) return a.Archipelago;
            return null;
        }

        // ER DLC maps: m20-29 (Belurat/Shadow Keep/Stone Coffin/Enir-Ilim), m40-59 (DLC
        // catacombs/gaols/forges), m61 (Land of Shadow overworld). Base: m10-19 / m30-39 / m60.
        private static bool IsDlcMap(string map)
        {
            if (string.IsNullOrEmpty(map) || map.Length < 3 || map[0] != 'm') return false;
            if (!int.TryParse(map.Substring(1, 2), out int n)) return false;
            return (n >= 20 && n <= 29) || (n >= 40 && n <= 59) || n == 61;
        }

        private static bool IsDlcArea(AnnotationData ann, string area)
        {
            return area != null && ann.Areas.TryGetValue(area, out var a)
                && a.Tags != null && a.Tags.Split(' ').Contains("dlc");
        }

        private static string AreaKind(AnnotationData ann, string area)
        {
            if (area == null) return "open";
            if (ann.Areas.TryGetValue(area, out var a) && a.Tags != null &&
                a.Tags.Split(' ').Contains("minidungeon")) return "mini";
            if (LegacyPrefix.Any(p => area == p || area.StartsWith(p + "_"))) return "legacy";
            return "open";
        }

        // dungeon/legacy boss = override, else lowest-id Boss in the area's maps, else lowest-id MinorBoss
        private static int DungeonBossFlag(string area, AnnotationData ann, List<Boss> roster)
        {
            if (AreaBossOverride.TryGetValue(area, out var ov))
                return roster.FirstOrDefault(b => b.Id == ov)?.Flag ?? 0;
            if (!ann.Areas.TryGetValue(area, out var a) || a.Maps == null) return 0;
            var maps = new HashSet<string>(a.Maps.Split(' '));
            Boss pick =
                roster.Where(b => b.Cls == EnemyAnnotations.EnemyClass.Boss && maps.Contains(b.Map)).OrderBy(b => b.Id).FirstOrDefault()
                ?? roster.Where(b => b.Cls == EnemyAnnotations.EnemyClass.MinorBoss && maps.Contains(b.Map)).OrderBy(b => b.Id).FirstOrDefault();
            return pick?.Flag ?? 0;
        }

        // boss world position from its MSB enemy part -> global coords. False if not found.
        private static bool TryBossPos(Dictionary<string, MSBE> maps, EldenCoordinator coord, EnemyAnnotations.EnemyInfo e, out Vector3 g)
        {
            g = default;
            try
            {
                if (e.Map == null || !maps.TryGetValue(e.Map, out var msb)) return false;
                var part = msb.Parts.Enemies.Find(p => p.EntityID == e.ID);
                if (part == null) return false;
                var (gp, _, _) = coord.ToGlobalCoords(e.Map, part.Position);
                g = gp;
                return true;
            }
            catch { return false; }
        }
    }
}
