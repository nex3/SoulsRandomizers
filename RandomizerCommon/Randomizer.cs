﻿using System;
using System.IO;
using System.Linq;
using SoulsIds;
using static SoulsIds.GameSpec;
using static RandomizerCommon.Messages;
using static RandomizerCommon.Util;

namespace RandomizerCommon
{
    public class Randomizer
    {
        [Localize]
        private static readonly Text loadPhase = new Text("Loading game data", "Randomizer_loadPhase");
        [Localize]
        private static readonly Text enemyPhase = new Text("Randomizing enemies", "Randomizer_enemyPhase");
        [Localize]
        private static readonly Text itemPhase = new Text("Randomizing items", "Randomizer_itemPhase");
        [Localize]
        private static readonly Text editPhase = new Text("Editing game files", "Randomizer_editPhase");
        [Localize]
        private static readonly Text savePhase = new Text("Writing game files", "Randomizer_savePhase");
        [Localize]
        private static readonly Text saveMapPhase = new Text("Writing map data: {0}%", "Randomizer_saveMapPhase");
        [Localize]
        private static readonly Text restartMsg = new Text(
            "Error: Mismatch between regulation.bin and other randomizer files.\nMake sure all randomizer files are present, the game has been restarted\nafter randomization, and the game and regulation.bin versions are compatible.",
            "GameMenu_restart");

        [Localize]
        private static readonly Text mergeMissingError =
            new Text("Error merging mods: directory {0} not found", "Randomizer_mergeMissingError");
        [Localize]
        private static readonly Text mergeWrongDirError =
            new Text("Error merging mods: already running from {0} directory", "Randomizer_mergeWrongDirError");

        public static readonly string EldenVersion = "v0.5.5";

        // TODO: There are way too many arguments here. Dependency injection or something?
        public void Randomize(
            RandomizerOptions opt,
            FromGame type,
            Action<string> notify = null,
            string outPath = null,
            Preset preset = null,
            Messages messages = null,
            bool encrypted = true,
            string gameExe = null,
            string modDir = null)
        {
            messages = messages ?? new Messages(null);
            string distDir = type == FromGame.ER ? "diste" : (type == FromGame.SDT ? "dists" : "dist");
            if (!Directory.Exists(distDir))
            {
                // From Release/Debug dirs
                distDir = $@"..\..\..\{distDir}";
                opt["dryrun"] = true;
            }
            if (!Directory.Exists(distDir))
            {
                throw new Exception("Missing data directory");
            }
            if (outPath == null)
            {
                if (type == FromGame.ER && opt["uxm"])
                {
                    outPath = Path.GetDirectoryName(gameExe);
                }
                else
                {
                    outPath = Directory.GetCurrentDirectory();
                }
            }
            bool header = !opt.GetOptions().Any(o => o.StartsWith("dump")) && !opt["configgen"];
            if (!header)
            {
                notify = null;
            }

            if (header)
            {
                Console.WriteLine($"Options and seed: {opt}");
                Console.WriteLine();
            }
            int seed = (int)opt.Seed;

            notify?.Invoke(messages.Get(loadPhase));

            DirectoryInfo modDirInfo = null;
            if (opt["mergemods"])
            {
                // Previous Elden Ring UXM merge behavior
                // modDir = Path.GetDirectoryName(gameExe);
                string modPath = type == FromGame.SDT ? "mods" : "mod";
                modDirInfo = new DirectoryInfo($@"{outPath}\..\{modPath}");
            }
            else if (!string.IsNullOrWhiteSpace(modDir))
            {
                modDirInfo = new DirectoryInfo(modDir);
            }
            string outModDir = null;
            if (modDirInfo != null)
            {
                if (!modDirInfo.Exists)
                {
                    throw new Exception(messages.Get(mergeMissingError, modDirInfo.FullName));
                }
                outModDir = modDirInfo.FullName;
                if (outModDir != null && new DirectoryInfo(outPath).FullName == outModDir)
                {
                    throw new Exception(messages.Get(mergeWrongDirError, modDirInfo.Name));
                }
            }

            GameData game = new GameData(distDir, type);
            game.Load(outModDir);

#if DEBUG
            if (opt["update"])
            {
                MiscSetup.UpdateEldenRing(game, opt);
                return;
            }
            // game.UnDcx(ForGame(FromGame.ER).GameDir + @"\map\mapstudio"); return;
            // game.SearchParamInt(14000800); return;
            // foreach (string lang in MiscSetup.Langs) game.DumpMessages(GameSpec.ForGame(GameSpec.FromGame.ER).GameDir + $@"\msg\{lang}"); return;
            // game.DumpMessages(GameSpec.ForGame(GameSpec.FromGame.ER).GameDir + @"\msg\engus"); return;
            // game.DumpMessages(GameSpec.ForGame(GameSpec.FromGame.DS1R).GameDir + @"\msg\ENGLISH"); return;
            // MiscSetup.CombineSFX(game.Maps.Keys.Concat(new[] { "dlc1", "dlc2" }).ToList(), GameSpec.ForGame(GameSpec.FromGame.DS3).GameDir + @"\combinedai", true); return;
            // MiscSetup.CombineAI(game.Maps.Keys.ToList(), ForGame(FromGame.DS3).GameDir + @"\combinedai", true); return;
#endif

            // Prologue
            if (header)
            {
                if (modDir != null) Console.WriteLine();
                if (opt["enemy"])
                {
                    Console.WriteLine("Ctrl+F 'Boss placements' or 'Miniboss placements' or 'Basic placements' to see enemy placements.");
                }
                if (opt["item"])
                {
                    Console.WriteLine("Ctrl+F 'Hints' to see item placement hints, or Ctrl+F for a specific item name.");
                }
                if (type == FromGame.ER)
                {
                    Console.WriteLine($"Version: {EldenVersion}");
                }
                if (preset != null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"-- Preset");
                    Console.WriteLine(preset.ToYamlString());
                }
                Console.WriteLine();
#if !DEBUG
                for (int i = 0; i < 50; i++) Console.WriteLine();
#endif
            }

            // Slightly different high-level algorithm for each game.
            if (game.Sekiro)
            {
                Events events = new Events($@"{game.Dir}\Base\sekiro-common.emedf.json");
                var eventConfig = game.ParseYaml<EventConfig>("events.yaml");

                EnemyLocations locations = null;
                if (opt["enemy"])
                {
                    notify?.Invoke("Randomizing enemies");
                    locations = new EnemyRandomizer(game, events, eventConfig).Run(opt, preset);
                    if (!opt["enemytoitem"])
                    {
                        locations = null;
                    }
                }
                if (opt["item"])
                {
                    notify?.Invoke("Randomizing items");
                    SekiroLocationDataScraper scraper = new SekiroLocationDataScraper();
                    LocationData data = scraper.FindItems(game);
                    AnnotationData anns = new AnnotationData(game, data);
                    anns.Load(opt);
                    anns.ProcessRestrictions(locations);

                    SkillSplitter.Assignment split = null;
                    if (!opt["norandom_skills"] && opt["splitskills"])
                    {
                        split = new SkillSplitter(game, data, anns, events).SplitAll();
                    }

                    Permutation perm = new Permutation(game, data, anns, messages, explain: false);
                    perm.Logic(new Random(seed), opt, preset);

                    notify?.Invoke("Editing game files");
                    PermutationWriter write = new PermutationWriter(game, data, anns, events, eventConfig);
                    write.Write(new Random(seed + 1), perm, opt);
                    if (!opt["norandom_skills"])
                    {
                        SkillWriter skills = new SkillWriter(game, data, anns);
                        skills.RandomizeTrees(new Random(seed + 2), perm, split);
                    }
                    if (opt["edittext"])
                    {
                        HintWriter hints = new HintWriter(game, data, anns);
                        hints.Write(opt, perm);
                    }
                }
                MiscSetup.SekiroCommonPass(game, events, opt);

                notify?.Invoke("Writing game files");
                if (!opt["dryrun"])
                {
                    game.SaveSekiro(outPath);
                }
                return;
            }
            else if (game.DS3)
            {
                Events events = new Events($@"{game.Dir}\Base\ds3-common.emedf.json", darkScriptMode: true);
                var eventConfig = game.ParseYaml<EventConfig>("events.yaml");

                LocationDataScraper scraper = new LocationDataScraper(logUnused: false);
                LocationData data = scraper.FindItems(game);
                AnnotationData ann = new AnnotationData(game, data);
                ann.Load(opt);

                if (opt["enemy"])
                {
                    notify?.Invoke("Randomizing enemies");
                    new EnemyRandomizer(game, events, eventConfig).Run(opt, preset);
                }

                if (opt["item"])
                {
                    ann.AddSpecialItems();
                    notify?.Invoke("Randomizing items");
                    Random random = new Random(seed);
                    Permutation permutation = new Permutation(game, data, ann, messages, explain: false);
                    permutation.Logic(random, opt, null);

                    notify?.Invoke("Editing game files");
                    random = new Random(seed + 1);
                    PermutationWriter writer = new PermutationWriter(game, data, ann, events, null);
                    writer.Write(random, permutation, opt);
                    random = new Random(seed + 2);
                    // TODO maybe randomize other characters no matter what, only do self for item rando
                    CharacterWriter characters = new CharacterWriter(game, data);
                    characters.Write(random, opt);
                }
                else if (opt["enemychr"])
                {
                    // temp
                    Random random = new Random(seed);
                    CharacterWriter characters = new CharacterWriter(game, data);
                    characters.Write(random, opt);
                }
                MiscSetup.DS3CommonPass(game, events, opt);

                notify?.Invoke("Writing game files");
                if (!opt["dryrun"])
                {
                    game.SaveDS3(outPath, encrypted);
                }
            }
            else if (game.EldenRing)
            {
#if DEBUG
                if (opt["noitem"])
                {
                    new EldenDataPrinter().PrintData(game, opt);
                    return;
                }
#endif
                LocationData data = null;
                PermutationWriter.Result permResult = null;
                if (opt["item"])
                {
                    notify?.Invoke(messages.Get(itemPhase));
                    var itemEventConfig = game.ParseYaml<EventConfig>("itemevents.yaml");

                    EldenCoordinator coord = new EldenCoordinator(game, opt["debugcoords"]);
                    if (opt["dumpcoords"])
                    {
                        coord.DumpJS(game);
                        return;
                    }
                    EldenLocationDataScraper scraper = new EldenLocationDataScraper();
                    data = scraper.FindItems(game, coord, opt);
                    if (data == null || opt["dumplot"] || opt["dumpitemflag"])
                    {
                        return;
                    }
                    AnnotationData ann = new AnnotationData(game, data);
                    ann.Load(opt);
                    if (opt["dumpann"])
                    {
                        ann.Save(initial: false, filter: opt["annfilter"], coord: coord);
                        return;
                    }
                    ann.ProcessRestrictions(null);
                    ann.AddSpecialItems();
                    ann.AddMaterialItems(opt["mats"]);

                    Random random = new Random(seed);
                    Permutation perm = new Permutation(game, data, ann, messages, explain: opt["explain"]);
                    perm.Logic(random, opt, preset);

                    notify?.Invoke(messages.Get(editPhase));
                    random = new Random(seed + 1);
                    PermutationWriter writer = new PermutationWriter(game, data, ann, null, itemEventConfig, messages, coord);
                    permResult = writer.Write(random, perm, opt);

                    if (opt["markareas"])
                    {
                        new HintMarker(game, data, ann, messages, coord).Write(opt, perm, permResult);
                    }

                    if (opt["mats"])
                    {
                        random = new Random(seed + 1);
                        new EldenMaterialRandomizer(game, data, ann).Randomize(random, perm);
                    }

                    random = new Random(seed + 2);
                    CharacterWriter characters = new CharacterWriter(game, data);
                    characters.Write(random, opt);
                }
                else if (!(opt["nooutfits"] && opt["nostarting"]))
                {
                    // Partially load item data just for identifying eligible starting weapons
                    EldenCoordinator coord = new EldenCoordinator(game, false);
                    EldenLocationDataScraper scraper = new EldenLocationDataScraper();
                    data = scraper.FindItems(game, coord, opt);
                    Random random = new Random(seed + 2);
                    CharacterWriter characters = new CharacterWriter(game, data);
                    characters.Write(random, opt);
                }
                if (opt["enemy"])
                {
                    notify?.Invoke(messages.Get(enemyPhase));

                    string emedfPath = null;
                    EventConfig enemyConfig;
                    string path = $@"{game.Dir}\Base\events.yaml";
#if DEV
                    if (opt["full"] || opt["configgen"])
                    {
                        emedfPath = @"configs\diste\er-common.emedf.json";
                        enemyConfig = ParseYaml<EventConfig>(@"configs\diste\events.yaml");
                    }
                    else
                    {
#endif
                    enemyConfig = game.ParseYaml<EventConfig>("events.yaml");
#if DEV
                    }
#endif
                    Events events = new Events(
                        emedfPath,
                        darkScriptMode: true,
                        paramAwareMode: true,
                        valueSpecs: enemyConfig.ValueTypes);
                    new EnemyRandomizer(game, events, enemyConfig).Run(opt, preset);
                }

                MiscSetup.EldenCommonPass(game, opt, permResult);

                if (!opt["dryrun"])
                {
                    notify?.Invoke(messages.Get(savePhase));
                    string options = $"Produced by Elden Ring Randomizer {EldenVersion} by thefifthmatt. Do not distribute. Options and seed: {opt}";
                    int mapPercent = -1;
                    void notifyMap(double val)
                    {
                        int percent = (int)Math.Floor(val * 100);
                        if (percent > mapPercent && percent <= 100)
                        {
#if !DEBUG
                            notify?.Invoke(messages.Get(saveMapPhase, percent));
#endif
                            mapPercent = percent;
                        }
                    }
                    game.WriteFMGs = true;
                    messages.SetFMGEntry(
                        game.MenuFMGs, game.OtherMenuFMGs, "EventTextForMap",
                        RuntimeParamChecker.RestartMessageId, restartMsg);

                    game.SaveEldenRing(outPath, opt["uxm"], options, notifyMap);
                }
            }
        }
    }
}
