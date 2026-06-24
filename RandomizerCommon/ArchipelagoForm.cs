using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.Util;
using static SoulsIds.GameSpec;

namespace RandomizerCommon
{
    public partial class ArchipelagoForm : Form
    {

        /// <summary>
        /// The location of the file in which data about this AP session is saved.
        /// </summary>
        private static readonly string ConfigFileLocation = "..\\apconfig.json";

        /// <summary>
        /// The location of the file in which ME3's configuration is stored.
        /// </summary>
        private static readonly string ME3ConfigFileLocation = "..\\me3-config.me3";

        /// <summary>The game that's being randomized.</summary>
        private readonly FromGame type;

        /// <summary>
        /// The Archipelago configuration data that was already saved in this directory, or an
        /// empty object if there wasn't any data.
        /// </summary>
        private readonly JObject configData;

        /// <summary>
        /// The ModEngine3 configuration that was saved in this directory, or null if none was
        /// found.
        /// </summary>
        private readonly TomlTable me3ConfigData;

        private readonly Timer blinkTimer;

        /// <summary>When true, automatically clicks Connect once the form is shown (dev loop).</summary>
        public bool AutoConnect = false;
        // Set via the "enemies" launch arg: leave the enemy randomizer ENABLED under autoconnect.
        // (Default remains disabled — the original goods-MVP-safe dev loop.)
        public bool AutoConnectEnemies = false;
        // Slot name to autoconnect as (build.ps1 passes slot=<name> read from the Players
        // yaml). Falls back to "Player1" if unset, for manual/legacy bakes.
        public string AutoConnectSlot = null;
        // URL to autoconnect to (build.ps1 passes url=<host:port>). Defaults to localhost:38281.
        // Like the slot, this ALWAYS wins under autoconnect -- the dev loop / -LoopTest always
        // serve locally, so a stale url pre-filled from the last apconfig must NOT survive.
        public string AutoConnectUrl = null;
        // Headless batch bake (build.ps1 -LoopTest): suppress the success dialog and
        // auto-close on BOTH success and failure, setting the process exit code
        // (0 ok / 1 fail) so a script can bake many seeds unattended. Implies AutoConnect.
        public bool Headless = false;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (AutoConnect)
            {
                // The url ALWAYS wins under autoconnect -- url.Text may be pre-filled from the last
                // apconfig (a stale remote ref from a central sync), so an empty-check isn't enough.
                // The dev loop and -LoopTest always serve locally; default to localhost:38281.
                url.Text = string.IsNullOrEmpty(AutoConnectUrl) ? "localhost:38281" : AutoConnectUrl;
                // The slot from build.ps1 (yaml name) ALWAYS wins -- name.Text may be pre-filled
                // from the last apconfig (e.g. a stale "Player1"), so an empty-check isn't enough.
                if (!string.IsNullOrEmpty(AutoConnectSlot)) name.Text = AutoConnectSlot;
                else if (name.Text.Length == 0) name.Text = "Player1";
                // Goods-only plumbing test default: disable enemy randomization unless the
                // "enemies" launch arg opted in (DLC-enemy testing).
                disableEnemyRandomizerCheckbox.Checked = !AutoConnectEnemies;
                submit_Click(this, EventArgs.Empty);
            }
        }

        public ArchipelagoForm(FromGame type)
        {
            InitializeComponent();
            var resources = new ComponentResourceManager(typeof(ArchipelagoForm));
            Icon = (System.Drawing.Icon)resources.GetObject(
                type switch
                {
                    FromGame.DS3 => "$this.DS3Icon",
                    FromGame.SDT => "$this.SDTIcon",
                    // TODO: ship a dedicated ER icon resource; reuse DS3's for now (cosmetic only).
                    FromGame.ER => "$this.DS3Icon",
                    var g => throw UnsupportedGame(g),
                }
            );

            MinimumSize = Size;

            this.type = type;
            blinkTimer = new()
            {
                Interval = 500 // 0.5 seconds
            };
            blinkTimer.Tick += BlinkTimer_Tick;

            try
            {
                configData = JsonConvert.DeserializeObject<JObject>(
                    File.ReadAllText(ConfigFileLocation)
                );
            }
            catch (FileNotFoundException)
            {
                configData = new JObject();
            }
            catch (JsonException)
            {
                MessageBox.Show(
                    $"Failed to load {Path.GetFileName(ConfigFileLocation)}. Running the " +
                    "randomizer may overwrite existing save data.",
                    "Archipelago Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            try
            {
                me3ConfigData = Toml.ToModel(File.ReadAllText(ME3ConfigFileLocation));
            }
            catch (Exception)
            {
                // Allow the config to be null, we just won't customize the save location.
            }

            if (configData.Value<string>("url") is string savedUrl) url.Text = savedUrl;
            if (configData.Value<string>("slot") is string savedSlot) name.Text = savedSlot;
            if (configData.Value<string>("password") is string savedPassword) password.Text = savedPassword;
        }

        private static SemanticVersioning.Version Version
        {
            get
            {
                return Assembly.GetCallingAssembly()
                    .GetCustomAttribute<VersionAttribute>()
                    .Version;
            }
        }

        private async void submit_Click(object sender, EventArgs e)
        {
            foreach (Control control in Controls)
            {
                if (control.Name != "status")
                {
                    control.Enabled = false;
                }
            }

            Cursor = Cursors.WaitCursor;

            SetStatusText("Connecting...", System.Drawing.Color.Blue);
            status.Visible = true;
            status.Refresh();

            if (url.Text.Length == 0)
            {
                ShowFailure("Missing Archipelago URL");
                return;
            }

            if (name.Text.Length == 0)
            {
                ShowFailure("Missing player name");
                return;
            }

            ArchipelagoSession session;
            try
            {
                session = ArchipelagoSessionFactory.CreateSession(url.Text);
            }
            catch (System.UriFormatException ex)
            {
                ShowFailure(ex.Message);
                return;
            }

            LoginResult result;
            try
            {
                result = session.TryConnectAndLogin(
                    type switch
                    {
                        FromGame.DS3 => "Dark Souls III",
                        FromGame.SDT => "Sekiro: Shadows Die Twice",
                        // Frozen contract Decision A: exact AP connection string, no space.
                        FromGame.ER => "EldenRing",
                        var g => throw UnsupportedGame(g)
                    },
                    name.Text,
                    Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.NoItems,
                    password: password.Text.Length == 0 ? null : password.Text,
                    version: new System.Version(0, 6, 6),
                    requestSlotData: true
                );
            }
            catch (Exception exception)
            {
                result = new LoginFailure(exception.GetBaseException().Message);
            }

            if (!result.Successful)
            {
                var failure = (LoginFailure)result;
                var errorMessage = "Failed to connect:";
                foreach (string error in failure.Errors)
                {
                    errorMessage += $"\n    {error}";
                }
                foreach (ConnectionRefusedError error in failure.ErrorCodes)
                {
                    errorMessage += $"\n    {error}";
                }
                ShowFailure(errorMessage);
                return;
            }

#if !DEBUG
            try
            {
#endif
            // Slot data now arrives in the Connected packet (we ask for it at login) and is
            // parsed before login returns. Read it from the login result rather than a live
            // DataStorage read of the _read_slot_data key: that synchronous read blocks on a
            // packet response and times out, as the AP client library explicitly warns.
            var slotData = ((LoginSuccessful)result).SlotData;
            await Task.Run(() => RandomizeForArchipelago(session, slotData));
#if !DEBUG

            }
            catch (Exception ex)
            {
                // Dump the full stack trace so we can find the actual throw site (the form only
                // shows ex.Message otherwise).
                Console.WriteLine("=== RandomizeForArchipelago FAILED ===");
                Console.WriteLine(ex.ToString());
                try { File.WriteAllText(Util.ApDiagPath("ap_error"), ex.ToString()); } catch { }
                ShowFailure(ex.Message);
                return;
            }
#endif

            if (Headless) { System.Environment.ExitCode = 0; }
            else { MessageBox.Show("Archipelago config loaded successfully!"); }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// Runs the randomizer and saves its results.
        /// </summary>
        /// <returns>True if randomization succeeded, false if it was canceled.</returns>
        // Full-bake log (tee): mirror Console.Out/Error to a timestamped ap_bake_<stamp>.log so the
        // ENTIRE bake is persisted (RegionFogGates, CompletionScaling diag, ap_* echoes, and the
        // FAILED stack from submit_Click's catch). Installed once at bake start; deliberately NOT
        // restored, so output that happens after RandomizeForArchipelago unwinds is still captured.
        private sealed class TeeTextWriter : System.IO.TextWriter
        {
            private readonly System.IO.TextWriter _a, _b;
            public TeeTextWriter(System.IO.TextWriter a, System.IO.TextWriter b) { _a = a; _b = b; }
            public override System.Text.Encoding Encoding => _a.Encoding;
            public override void Write(char c) { _a.Write(c); _b.Write(c); }
            public override void Write(string s) { _a.Write(s); _b.Write(s); }
            public override void Flush() { _a.Flush(); _b.Flush(); }
        }
        private static System.IO.TextWriter _bakeRealOut, _bakeRealErr;
        private static System.IO.StreamWriter _bakeLogWriter;
        private static string StartBakeLog()
        {
            try
            {
                if (_bakeRealOut == null) { _bakeRealOut = Console.Out; _bakeRealErr = Console.Error; }
                try { _bakeLogWriter?.Flush(); _bakeLogWriter?.Dispose(); } catch { }
                string path = Util.ApDiagPath("ap_bake").Replace(".txt", ".log");
                _bakeLogWriter = new System.IO.StreamWriter(path, false) { AutoFlush = true };
                _bakeLogWriter.WriteLine($"=== ER AP bake log {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                Console.SetOut(new TeeTextWriter(_bakeRealOut, _bakeLogWriter));
                Console.SetError(new TeeTextWriter(_bakeRealErr, _bakeLogWriter));
                return path;
            }
            catch (Exception e) { try { Console.WriteLine("StartBakeLog failed: " + e.Message); } catch { } return null; }
        }
        private void RandomizeForArchipelago(ArchipelagoSession session, Dictionary<string, object> slotData)
        {
            SetStatusText("Downloading item data...");
            string bakeLogPath = StartBakeLog(); Console.WriteLine($"Bake log -> {bakeLogPath}");
            var locations = session.Locations
                .ScoutLocationsAsync(session.Locations.AllLocations.ToArray())
                .Result
                .Values
                .OrderBy(location => location.LocationId)
                .ToList();
            // Values are category-packed FullIDs (top nibble = category); the GEM nibble
            // 0x80000000 overflows a signed-int32 read. Read as long, reinterpret the low 32 bits.
            var apIdsToItemIds = SlotDataParse.ApIdsToItemIds((JObject)slotData["apIdsToItemIds"]);
            CheckVersionRange(slotData);
            // Only the boolean options go into this dict. ER's slot_data also includes non-bool
            // options (e.g. exclude_local_item_only is an array), which would break a strict
            // Dictionary<string, bool> deserialization; those are read directly from slotData where
            // the ER-specific logic needs them.
            var options = SlotDataParse.BoolOptions((JObject)slotData["options"]);
            if (disableEnemyRandomizerCheckbox.Checked) options["randomize_enemies"] = false;
            // The ER apworld has no randomize_enemies option in slot_data (DS3's does), so for ER
            // the checkbox is the source of truth: unchecked = enemy randomizer ON. Without this,
            // GetValueOrDefault(false) made the ER enemy pass unreachable.
            // ER honors the apworld's enemy_rando slot_data key as the source of truth, so
            // headless / true-multiworld seeds drive the enemy pass from the yaml rather than the
            // GUI. enemy_rando ships as an int toggle (0/1), which BoolOptions() drops -- it keeps
            // only JSON booleans -- so read it straight from the raw options object and coerce.
            // The "Disable Enemy Randomizer" checkbox stays a hard manual override (handled above:
            // checked => false). If the key is absent (older apworld) fall back to ON-when-unchecked
            // to preserve prior behavior.
            if (type == FromGame.ER && !disableEnemyRandomizerCheckbox.Checked)
            {
                JToken enemyRandoTok = ((JObject)slotData["options"])["enemy_rando"];
                bool enemyRandoOn =
                    enemyRandoTok == null
                        ? true
                        : enemyRandoTok.Type == JTokenType.Boolean
                            ? enemyRandoTok.Value<bool>()
                            : enemyRandoTok.Value<long>() != 0;
                options["randomize_enemies"] = enemyRandoOn;
            }

            var opt = ConvertRandomizerOptions(options);
            var itemCounts = ((JObject)slotData["itemCounts"]).ToObject<Dictionary<string, uint>>()
                .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);

            SetStatusText("Loading game data...");

            var distBasename = type switch
            {
                FromGame.DS3 => "dist",
                FromGame.SDT => "dists",
                FromGame.ER => "diste",
                var g => throw UnsupportedGame(g),
            };
#if DEBUG
            // In debug mode, always use the data files from the local repository rather than those
            // in the directory we're randomizing to. This ensures we don't accidentally end up
            // testing against the data that shipped with whichever release copy of the Archipelago
            // client we downloaded.
            var distDir = Path.Join(Application.StartupPath, $@"..\..\..\..\..\{distBasename}");
#else
            var distDir = distBasename;
#endif
            if (!Directory.Exists(distDir))
            {
                throw new Exception("Missing data directory");
            }
            var game = new GameData(distDir, type);
            if (type == FromGame.ER)
            {
                // Full-DLC spec (docs/er/er-ap-3-full-dlc.md) WI-1: keep SOTE maps loaded when the
                // seed includes DLC locations (enable_dlc) or the enemy randomizer will run (the
                // v0.11.4-ported enemy config includes DLC enemies). With both off, the original
                // base-game-only strip applies and behavior is unchanged.
                // enable_dlc ships as an int toggle (0/1); BoolOptions() keeps only JSON bools, so it
                // gets dropped and GetValueOrDefault returned false -> DLC maps stripped -> ~871 DLC
                // locations never scraped -> DLC items stayed vanilla on an enemy-rando-OFF / dlc_only
                // seed. Read it raw + coerce, same as randomize_enemies below.
                bool enableDlcRaw = (((JObject)slotData["options"])?["enable_dlc"]?.Value<int>() ?? 0) != 0;
                game.KeepDlcMaps = enableDlcRaw
                    || options.GetValueOrDefault("randomize_enemies", false);
            }
            game.Load();

            EventConfig eventConfig;
            // ER stores its item-event config in itemevents.txt; DS3/Sekiro use events.txt.
            string eventConfigFile = type == FromGame.ER ? "itemevents.txt" : "events.txt";
            using (var reader = File.OpenText($@"{game.Dir}\Base\{eventConfigFile}"))
            {
                eventConfig = new DeserializerBuilder().Build().Deserialize<EventConfig>(reader);
            }

            LocationData data;
            Events events;
            EldenCoordinator coord = null;
            switch (type)
            {
                case FromGame.DS3:
                    data = new LocationDataScraper(logUnused: false).FindItems(game);
                    events = new Events(
                        $@"{game.Dir}\Base\ds3-common.emedf.json",
                        darkScriptMode: true
                    );
                    break;

                case FromGame.SDT:
                    data = new SekiroLocationDataScraper().FindItems(game);
                    events = new Events($@"{game.Dir}\Base\sekiro-common.emedf.json");
                    break;

                case FromGame.ER:
                    coord = new EldenCoordinator(game, false);
                    data = new EldenLocationDataScraper().FindItems(game, coord, opt);
                    events = null; // ER's PermutationWriter takes a coord instead of an Events.
                    break;

                case var g: throw UnsupportedGame(g);
            }

            var messages = type == FromGame.ER ? new Messages(distBasename) : new Messages(null);
            var ann = new AnnotationData(game, data);
            ann.Load(opt);
            PermutationWriter writer;
            if (type == FromGame.ER)
            {
                // Mirror Randomizer.cs's ER setup: extra annotation processing + the coord/messages
                // PermutationWriter overload (ER uses a coord rather than an Events).
                ann.ProcessRestrictions(opt, null);
                ann.AddSpecialItems();
                ann.AddMaterialItems(opt["mats"]);
                writer = new PermutationWriter(game, data, ann, null, eventConfig, messages, coord);
            }
            else
            {
                writer = new PermutationWriter(game, data, ann, events, eventConfig);
            }
            var permutation = new Permutation(game, data, ann, messages);
            var apLocationsToScopes = ArchipelagoLocations(ann, locations, slotData);

            // The Archipelago API doesn't guarantee that the seed is a number, so we hash it so
            // that we can use it as a seed for C#'s RNG. Add the current player's slot number so
            // that multiple DS3 instances in the same multiworld have different local seeds.
            var seed = HashStringToInt(session.RoomState.Seed) + session.ConnectionInfo.Slot;
            opt.Seed = (uint)seed;
            var random = new Random(seed);

            // Randomize starting loadout *before* adding a bunch of synthetic weapons and armor to
            // the pool that we don't want shoved into shops. CharacterWriter fully supports ER
            // (class stats incl. Arcane, two-handing, ER params); with no handedness opts set it
            // uses the GUI defaults (two-hand allowed, stat adjustments allowed).
            // NB: ER's slot_data encodes toggles as 0/1 INTS, so they're excluded from the
            // bool-only `options` dict above — read random_start straight from slotData.
            // KNOWN BROKEN (2026-06-11): ER CharacterWriter corrupts the regulation -> game
            // crashes on boot. Root cause: this fork's ER CharacterWriter predates the DLC-era
            // CharaInitParam def (public randomizer source is ~3 years stale), so its writes are
            // misaligned against regulation 1.16. Fix = audit CharacterWriter's ER writes against
            // the current Paramdex def, then restore:
            //   type == FromGame.ER && (((JObject)slotData["options"])?["random_start"]?.Value<int>() ?? 0) != 0
            bool erRandomStart = false;
            if ((type == FromGame.DS3 && options["random_starting_loadout"]) || erRandomStart)
            {
                var characters = new CharacterWriter(game, data);
                characters.Write(random, opt);
            }

            // A map from locations in the game where items can appear to the list of items that
            // should appear in those locations.
            var items = new Dictionary<SlotKey, List<SlotKey>>();

            // A map from items in the game that should be removed to locations where those items
            // would normally appear, or null if those items should remain in-game (likely because
            // they're assigned elsewhere).
            var itemsToRemove = new Dictionary<SlotKey, List<SlotKey>>();

            int skippedUnresolvedItems = 0;
            var droppedLocationNames = new List<string>();
            var droppedItemNames = new List<string>();
            // GOOD items whose er_code has no EquipParamGoods row (bad apworld id / wrong
            // category nibble). Logged + skipped (location keeps vanilla item) instead of NPEing.
            var badParamRowItems = new List<string>();
            foreach (var info in locations)
            {
                // Locations whose slot keys weren't in this (base-game-only) scrape were dropped
                // from apLocationsToScopes; skip them here too instead of throwing.
                if (!apLocationsToScopes.TryGetValue(info.LocationId, out var targetScope))
                {
                    skippedUnresolvedItems++;
                    droppedLocationNames.Add(info.LocationName ?? $"<id {info.LocationId}>");
                    continue;
                }
                var targetSlotKey = FindMatchingSlotKey(
                    session, game, data.Location(targetScope), info);
                AddMulti(itemsToRemove, targetSlotKey, FindMatchingSlotKey(
                    session, game, data.Locations[targetScope], info));

                var targetSlot = ann.Slots[targetScope];
                var player = session.Players.Players[session.ConnectionInfo.Team]
                    .First(player => player.Slot == info.Player);

                if (info.Player != session.ConnectionInfo.Slot)
                {
                    // Create a fake key item for each item from another world.
                    var item = writer.AddSyntheticItem(
                        SyntheticItemName(info),
                        $"An object from a mysterious world known only as \"{player.Game}\".",
                        // Custom Archipelago icon.
                        iconId: type switch {
                            FromGame.DS3 => 6020,
                            FromGame.SDT => 579,
                            // TODO: dedicated ER AP foreign-item icon. Cosmetic only — the runtime
                            // client detects synthetic items by id range, not icon.
                            FromGame.ER => 7039,
                            var g => throw UnsupportedGame(g),
                        },
                        // The highest in-game sortId across all supported games is 133,100, so for
                        // foreign items we start from 200,000 to sort them after in-game key
                        // items. From there we add the player ID as the primary sort, followed by
                        // the item ID (mod 10k because Archipelago puts all item IDs in a single
                        // 54-bit numberspace). This means that in shops, foreign items will be
                        // grouped first by player and then by item.
                        sortId: 200000 + (uint)info.Player.Slot * 10000 +
                            (uint)(info.ItemId % 10000),
                        archipelagoLocationId: info.LocationId);
                    AddMulti(items, targetSlotKey, item);
                }
                else if (type == FromGame.DS3 && info.ItemName == "Path of the Dragon")
                {
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"Path of the Dragon",
                        "A gesture of meditation channeling the eternal essence of the ancient dragons",
                        "The path to ascendence can be achieved only by the most resolute of seekers. Proper utilization of this technique can grant deep inner focus.",
                        iconId: 7039,
                        archipelagoLocationId: info.LocationId));
                }
                else if (!apIdsToItemIds.TryGetValue(info.ItemId, out var localItemId))
                {
                    // The server's slot_data didn't map this local AP item id to a game item id.
                    // (apworld slot_data completeness issue.) Skip placing it rather than throwing
                    // KeyNotFoundException; the location keeps its vanilla item.
                    skippedUnresolvedItems++;
                    droppedItemNames.Add($"{info.ItemName} (id {info.ItemId})");
                }
                else if (
                    (targetScope.ShopIds.Count == 0 && !(targetSlot.Tags?.Contains("crow") ?? false))
                    // ER: only EquipParamGoods/EquipParamAccessory have the Vagrant* fields we use
                    // to carry the AP location id (weapon/armor/gem params dropped them), and the
                    // runtime client only decodes synthetic GOODS tokens anyway. So for ER any
                    // non-GOOD local item must use the goods placeholder token even in shops —
                    // buying the token grants the real item via replaceWithInArchipelago. Without
                    // this, the first weapon placed in a shop NPE'd AddSyntheticCopy (Vagrant
                    // field lookup) the moment the expanded scope made shops resolvable.
                    || (type == FromGame.ER && new ItemKey(localItemId).Type != ItemType.GOOD)
                    // BRIEF #6: own-world GOODS sold in SHOPS were the last case still using the
                    // functional copy (AddSyntheticCopy) in the else-branch below: buying granted
                    // the real item AND the purchase flag tripped flag-polling, echoing it a
                    // second time (double-grant). Route shop GOODS through the placeholder token
                    // like every other shop item -> single grant (buy = placeholder, echo = real
                    // item; a lingering token clears once the client's removeFromInventory lands).
                    // Crow GOODS (ShopIds.Count == 0) keep the functional copy: not flag-polled
                    // the same way, out of scope for #6.
                    || (type == FromGame.ER && targetScope.ShopIds.Count > 0))
                {
                    // The Archipelago mod can't replace items that appear in shops or are dropped
                    // by the crow, so we put more realistic items there. Everywhere else, we
                    // replace with placeholders, so we can notify the Archipelago server when
                    // they're checked. We can't do this with items in shops because we don't have
                    // a good way to replace them on pickup.
                    //
                    // We can't make _all_ items realistic like we do for shops because that can't
                    // represent bundles of multiple items.
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"[Placeholder] {info.ItemName}",
                        $"A voucher for your own {info.ItemName}. Acquiring it reports the check; "
                            + "the real item is delivered by the Archipelago server moments later.",
                        archipelagoLocationId: info.LocationId,
                        replaceWithInArchipelago: new ItemKey(localItemId),
                        replaceWithQuantity: itemCounts.GetValueOrDefault(info.ItemId, 1U)));
                }
                else
                {
                    var original = new ItemKey(localItemId);
                    // A packed id whose low bits don't exist in the category's param would NPE
                    // deep in AddSyntheticCopy's row clone. These are the apworld's logic-only
                    // "lock" keys (sentinel er_code 99999) or genuinely bad ids; route them
                    // through the placeholder token instead so the CHECK still exists in-game.
                    // The client skips granting the 99999 sentinel when it echoes back.
                    // (ER-only: in ER this branch is GOODs-only so a direct row lookup is valid;
                    // DS3 weapons need the upgrade-digit math.)
                    if (type == FromGame.ER && game.Param(original.Type)[original.ID] == null)
                    {
                        badParamRowItems.Add(
                            $"{info.ItemName} (ap {info.ItemId}) -> {original.Type}:{original.ID} (no param row; placed as placeholder token)");
                        AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                            $"[Placeholder] {info.ItemName}",
                            $"A logic-only token ({info.ItemName}). Acquiring it reports the check; "
                                + "there is no physical item to deliver.",
                            archipelagoLocationId: info.LocationId,
                            replaceWithInArchipelago: original,
                            replaceWithQuantity: 1));
                        continue;
                    }
                    var (copy, _) = writer.AddSyntheticCopy(
                        original,
                        info.LocationId,
                        replaceWithInArchipelago: original,
                        replaceWithQuantity: 1
                    );
                    AddMulti(items, targetSlotKey, copy);
                }
            }

            // ===== DIAGNOSTIC DUMP: quantify how much of the server's seed we failed to resolve,
            // and sample the names so we can tell base-game vs DLC mismatch. =====
            try
            {
                var diag = new System.Text.StringBuilder();
                diag.AppendLine($"server scouted locations:            {locations.Count}");
                diag.AppendLine($"resolved to scopes:                  {apLocationsToScopes.Count}");
                diag.AppendLine($"dropped (location not in scrape):    {droppedLocationNames.Count}");
                diag.AppendLine($"dropped (item id not in apIdsToItemIds): {droppedItemNames.Count}");
                diag.AppendLine($"locations with placed items:         {items.Count}");
                diag.AppendLine($"apIdsToItemIds entries:              {apIdsToItemIds.Count}");
                diag.AppendLine($"ann.SlotsByAnnotationsKey:           {ann.SlotsByAnnotationsKey.Count}");
                diag.AppendLine($"ann.Slots:                           {ann.Slots.Count}");
                diag.AppendLine($"ann.Areas:                           {ann.Areas.Count}");
                diag.AppendLine($"game.Maps (after DLC strip):         {game.Maps.Count}");
                diag.AppendLine();
                diag.AppendLine("== sample dropped location names (first 40) ==");
                foreach (var n in droppedLocationNames.Take(40)) diag.AppendLine("  " + n);
                diag.AppendLine();
                diag.AppendLine("== sample dropped item names (first 40) ==");
                foreach (var n in droppedItemNames.Take(40)) diag.AppendLine("  " + n);
                diag.AppendLine();
                diag.AppendLine($"== items with NO PARAM ROW (bad apworld er_code/category): {badParamRowItems.Count} ==");
                foreach (var n in badParamRowItems.Take(100)) diag.AppendLine("  " + n);
                File.WriteAllText(Util.ApDiagPath("ap_diag"), diag.ToString());
                Console.WriteLine(diag.ToString());
            }
            catch (Exception diagEx) { Console.WriteLine("diag dump failed: " + diagEx); }

            SetStatusText("Randomizing locations...");

            permutation.Forced(items, remove: itemsToRemove);

            permutation.Logic(random, opt, null, new List<Permutation.RandomSilo> {
                Permutation.RandomSilo.INFINITE,
                Permutation.RandomSilo.INFINITE_SHOP,
                Permutation.RandomSilo.INFINITE_GEAR,
                Permutation.RandomSilo.INFINITE_CERTAIN,
                Permutation.RandomSilo.MIXED
            });

            writer.Write(random, permutation, opt, alwaysReplacePathOfTheDragon: true);

            if (type == FromGame.DS3)
            {
                if (options["no_weapon_requirements"]) RemoveWeaponRequirements(game);
                if (options["no_spell_requirements"]) RemoveSpellRequirements(game);
                if (options["no_equip_load"]) RemoveEquipLoad(game);
            }

            if (options.GetValueOrDefault("randomize_enemies", false))
            {
                // ER's apworld doesn't send random_enemy_preset; DS3's always does.
                Preset preset = null;
                if (slotData.TryGetValue("random_enemy_preset", out object presetObj)
                    && presetObj is string presetYaml
                    && !string.IsNullOrWhiteSpace(presetYaml))
                {
                    try
                    {
                        preset = Preset.ParsePreset("archipelago", presetYaml);
                    }
                    catch (YamlException)
                    {
                        DisplayYamlParseError(presetYaml);
                        throw new Exception("Failed to parse enemy preset");
                    }
                }

                switch (type)
                {
                    case FromGame.DS3:
                        if (preset == null) throw new Exception("Missing random_enemy_preset in slot data");
                        preset.RemoveSource = preset.RemoveSource == null
                        ? "Yhorm the Giant"
                        : preset.RemoveSource + ";Yhorm the Giant";
                        preset.Enemies ??= new Dictionary<string, string>();
                        preset.Enemies[(string)slotData["yhorm"]] = "Yhorm the Giant";
                        break;

                    case FromGame.SDT:
                        // Handle explicit headless locations once the apworld has logic for them.
                        break;
                }

                if (type == FromGame.ER)
                {
                    // Mirror Randomizer.cs's ER enemy setup: the enemy pass uses Base/events.txt
                    // (this form's `eventConfig` above is itemevents.txt, the ITEM-path config)
                    // and its own lite-emedf Events instance. The previous code passed
                    // events=null + the item eventConfig, which would NPE if this path ever ran.
                    EventConfig enemyEventConfig;
                    using (var reader = File.OpenText($@"{game.Dir}\Base\events.txt"))
                    {
                        enemyEventConfig = new DeserializerBuilder().Build().Deserialize<EventConfig>(reader);
                    }
                    var enemyEvents = new Events(
                        null,
                        darkScriptMode: true,
                        paramAwareMode: true,
                        valueSpecs: enemyEventConfig.ValueTypes);
                    var erRando = new EnemyRandomizer(game, enemyEvents, enemyEventConfig);
                    erRando.Run(opt, preset);
                }
                else
                {
                    new EnemyRandomizer(game, events, eventConfig).Run(opt, preset);
                }
            }

            switch (type)
            {
                case FromGame.DS3:
                    // Sort params that have debug rows above them, so we don't break code that expects the
                    // params to be sorted by ID.
                    MiscSetup.SortParams(game, new[] { "EquipParamProtector", "EquipParamWeapon" });
                    MiscSetup.DS3CommonPass(game, events, opt);
                    break;

                case FromGame.SDT:
                    MiscSetup.SortParams(game, new[] { "EquipParamGoods", "EquipParamWeapon" });
                    MiscSetup.SekiroCommonPass(game, events, opt);
                    break;

                case FromGame.ER:
                    MiscSetup.EldenCommonPass(game, opt, messages);
                    // Messmer's Kindling Shard = goods 2008021 (vanilla Messmer's Kindling,
                    // a maxNum=1 key item). messmer_kindle grants up to messmer_kindle_max
                    // copies as the dlc_only spine, but the vanilla cap of 1 rejects the 2nd
                    // ("exceeds maximum storage"). Raise carry + box caps to hold the count.
                    {
                        var apKindling = game.Params["EquipParamGoods"][2008021];
                        if (apKindling != null)
                        {
                            apKindling["maxNum"].Value = (short)99;
                            apKindling["maxRepositoryNum"].Value = (short)99;
                        }
                    }
                    break;
            }
            MiscSetup.InjectUncompressed(game);
            MiscSetup.InjectApItemIcon(game);

            SetStatusText("Writing game files...");
            switch (type)
            {
                case FromGame.DS3:
                    game.SaveDS3(Directory.GetCurrentDirectory(), true);
                    break;

                case FromGame.SDT:
                    game.SaveSekiro(Directory.GetCurrentDirectory());
                    break;

                case FromGame.ER:
                    game.WriteFMGs = true;
                    game.SaveEldenRing(Directory.GetCurrentDirectory(), false,
                        $"Produced by ER Archipelago randomizer. Options and seed: {opt}");
                    break;

                case var g: throw UnsupportedGame(g);
            }

            SetStatusText("Writing client save file...");
            // Emit the AP-location -> in-game event flag map for the runtime client's flag
            // polling, which detects checks that bypass the AddItemFunc detour (shop purchases,
            // NPC gifts, pickups made while disconnected).
            if (type == FromGame.ER)
            {
                var flagMap = new JObject();
                foreach (var kv in writer.ApLocationFlags) flagMap[kv.Key.ToString()] = kv.Value;
                configData["location_flags"] = flagMap;
                Console.WriteLine($"location_flags: {writer.ApLocationFlags.Count} AP locations mapped to event flags");

                // Groundwork for grace warp rando (SPEC-grace-warp-rando.md): dump every
                // grace's warp-unlock flag so the apworld's grace data table can be built
                // from real ids. Diag-only; harmless if unused.
                try
                {
                    var lines = new List<string> { "rowId\teventflagId\t(extra fields best-effort)" };
                    foreach (var row in game.Params["BonfireWarpParam"].Rows)
                    {
                        string extra = "";
                        foreach (var fieldName in new[] { "bonfireEntityId", "textId1", "textId", "areaNo", "gridXNo", "gridZNo" })
                        {
                            try { extra += $"\t{fieldName}={row[fieldName].Value}"; } catch { }
                        }
                        try
                        {
                            lines.Add($"{row.ID}\t{row["eventflagId"].Value}{extra}");
                        } catch { }
                    }
                    File.WriteAllText(Util.ApDiagPath("ap_grace_flags"), string.Join("\n", lines));
                    Console.WriteLine($"ap_grace_flags: dumped {lines.Count - 1} BonfireWarpParam rows");
                } catch (Exception graceEx) { Console.WriteLine("grace flag dump failed: " + graceEx); }

                // Boss attribution (SPEC-boss-attribution.md): ENTIRELY gated on dungeon_sweep == bosses
                // (option value 3). When off, nothing below collects or computes -- no behaviour change
                // and no extra work for other seeds. Collect per-check (apLocId, area, pos) and per-grace
                // (litFlag, pos) during the coord dump; scopeToApLoc inverts apLocId->scope for AP ids.
                int apDungeonSweep = (slotData["options"] as JObject)?["dungeon_sweep"]?.Value<int>() ?? 0;
                bool apWantSweep = apDungeonSweep >= 3;
                var apSweepChecks = new List<BossAttribution.CheckPt>();
                var apSweepGraces = new List<BossAttribution.GracePt>();
                // entity id -> world pos, parsed from each slot's DebugText. A boss's drop-check
                // names its entity id, so this gives rando-stable boss positions (item lots don't
                // move when enemies shuffle), unlike a live-MSB lookup by entity id.
                var apEntityPos = new Dictionary<int, System.Numerics.Vector3>();
                var scopeToApLoc = new Dictionary<LocationScope, long>();
                if (apWantSweep)
                    foreach (var kv in apLocationsToScopes) scopeToApLoc[kv.Value] = kv.Key;

                // Check-trim groundwork (SPEC-check-trim.md): dump every Site of Grace AND every AP
                // item-location in GLOBAL coords (tile + x/y/z) so the apworld can score how 'out of
                // the way' a check is by distance to the nearest grace. Diag-only; harmless if it fails.
                try
                {
                    var clines = new List<string> { "type\tkey\ttileX\ttileZ\tgx\tgy\tgz\tmapName" };
                    int graceN = 0, itemN = 0;
                    foreach (var row in game.Params["BonfireWarpParam"].Rows)
                    {
                        try
                        {
                            List<byte> mapParts = game.GetMapParts(row);
                            var local = new System.Numerics.Vector3(
                                (float)row["posX"].Value, (float)row["posY"].Value, (float)row["posZ"].Value);
                            var (g, tx, tz) = coord.ToGlobalCoords(mapParts, local);
                            clines.Add($"grace\t{row.ID}\t{tx}\t{tz}\t{g.X:0.##}\t{g.Y:0.##}\t{g.Z:0.##}\t{GameData.FormatMap(mapParts)}");
                            graceN++;
                            if (apWantSweep) try { apSweepGraces.Add(new BossAttribution.GracePt { Flag = Convert.ToInt32(row["eventflagId"].Value), Pos = g }); } catch { }
                        } catch { }
                    }
                    foreach (var entry in ann.Slots)
                    {
                        var slotAnn = entry.Value;
                        if (slotAnn == null || string.IsNullOrEmpty(slotAnn.Key)) continue;
                        string em = null;
                        System.Numerics.Vector3 ep = default;
                        bool found = false;
                        foreach (SlotKey sk in data.Location(entry.Key))
                        {
                            ItemLocation il = data.Location(sk);
                            if (il == null) continue;
                            foreach (LocationKey lk in il.Keys)
                            {
                                foreach (EntityId ent in lk.Entities)
                                {
                                    if (ent.Position is System.Numerics.Vector3 p && !string.IsNullOrEmpty(ent.MapName))
                                    { ep = p; em = ent.MapName; found = true; break; }
                                }
                                if (found) break;
                            }
                            if (found) break;
                        }
                        if (!found) continue;
                        try
                        {
                            var (g, tx, tz) = coord.ToGlobalCoords(em, ep);
                            clines.Add($"item\t{slotAnn.Key}\t{tx}\t{tz}\t{g.X:0.##}\t{g.Y:0.##}\t{g.Z:0.##}\t{em}");
                            itemN++;
                            // Shop / NPC-exchange checks (Enia remembrances, Ymir/Moore/Thiollier shops)
                            // resolve to the MERCHANT's world position -- not a spot you reach by
                            // exploring near a boss. The position sweep otherwise mis-attributes them to
                            // the nearest boss and dumps them on that kill (killing Margit cleared 9 DLC
                            // merchant checks). They are BOUGHT, so exclude them from the boss sweep
                            // (still real checks: location_flags polls them on purchase).
                            if (apWantSweep && entry.Key.ShopIds.Count == 0
                                && scopeToApLoc.TryGetValue(entry.Key, out long apSweepId))
                                apSweepChecks.Add(new BossAttribution.CheckPt { ApLocId = apSweepId, Area = slotAnn.GetArea(), Pos = g });
                            // record this slot's entity ids at its world position. A boss's drop-check
                            // names the boss entity, giving a rando-stable boss position (item lots do
                            // not move under enemy rando). Read EntityID off the EntityId objects -- NOT
                            // slotAnn.DebugText: annotations.txt carries no "id N" (those live only in
                            // itemslots.txt), so the old DebugText regex matched nothing and left every
                            // boss unpositioned, silently emptying the field/capstone/grace sweep tiers.
                            if (apWantSweep)
                                foreach (SlotKey esk in data.Location(entry.Key))
                                {
                                    ItemLocation eil = data.Location(esk);
                                    if (eil == null) continue;
                                    foreach (LocationKey elk in eil.Keys)
                                        foreach (EntityId eent in elk.Entities)
                                            if (eent.EntityID > 0) apEntityPos[eent.EntityID] = g;
                                }
                        } catch { }
                    }
                    File.WriteAllText(Util.ApDiagPath("ap_location_coords"), string.Join("\n", clines));
                    Console.WriteLine($"ap_location_coords: dumped {itemN} item locations, {graceN} graces");
                } catch (Exception coordEx) { Console.WriteLine("location coords dump failed: " + coordEx); }

                // Boss attribution -> sweep_flags { eventFlag : [apLocationId,...] } in apconfig.json
                // (SPEC-boss-attribution.md). Gated on dungeon_sweep == bosses (option value 3).
                // grace_sweep: 0 off / 1 complement / 2 full. Harmless if it fails (sweep just absent).
                try
                {
                    if (apWantSweep)
                    {
                        int gsweep = (slotData["options"] as JObject)?["grace_sweep"]?.Value<int>() ?? 0;
                        bool erando = ((slotData["options"] as JObject)?["enemy_rando"]?.Value<int>() ?? 0) != 0;
                        var bopt = new BossAttribution.Options
                        {
                            GraceMode = gsweep == 2 ? "full" : gsweep == 1 ? "complement" : "off",
                            EnemyRando = erando,
                        };
                        var sweep = BossAttribution.Compute(game, ann, coord, apSweepChecks, apSweepGraces, bopt,
                            apEntityPos, out var sweepStats, out var sweepFlagNames);
                        // Chokepoint re-attribution (extra_region_locks: chokepoint_locks): the apworld
                        // carves a legacy dungeon's BEFORE-half onto its mid-boss chokepoint, but the
                        // geometric tier-1 attribution lumps the whole legacy area onto its single
                        // lowest-id boss (all Farum Azula -> Maliketh, all Haligtree -> Malenia). Re-home
                        // the before-half ids from the end-boss lump onto the choke boss DefeatFlag so
                        // killing the CHOKE boss (not the end boss) sweeps them. Grace flags (< 1e6) are
                        // left intact so grace_sweep still covers them. Source: slot_data chokepointSweeps.
                        if (slotData.TryGetValue("chokepointSweeps", out var chokeObj) && chokeObj is JObject chokeMap)
                        {
                            foreach (var ck in chokeMap)
                            {
                                if (!int.TryParse(ck.Key, out int chokeFlag)) continue;
                                var ids = (ck.Value as JArray)?.Select(t => t.Value<long>()).ToHashSet();
                                if (ids == null || ids.Count == 0) continue;
                                // pull off every OTHER boss flag (>= 1e6); leave grace flags alone
                                foreach (var kv in sweep)
                                    if (kv.Key != chokeFlag && kv.Key >= 1000000)
                                        kv.Value.RemoveAll(id => ids.Contains(id));
                                if (!sweep.TryGetValue(chokeFlag, out var dst)) sweep[chokeFlag] = dst = new List<long>();
                                foreach (var id in ids) if (!dst.Contains(id)) dst.Add(id);
                            }
                            // drop any boss flag whose list emptied out after the move
                            foreach (var _ek in sweep.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
                                sweep.Remove(_ek);
                        }
                        var sweepJson = new JObject();
                        long sweepPairs = 0; int sweepGraceFlags = 0;
                        foreach (var kv in sweep)
                        {
                            sweepJson[kv.Key.ToString()] = new JArray(kv.Value);
                            sweepPairs += kv.Value.Count;
                            if (kv.Key < 1000000) sweepGraceFlags++;   // grace lit-flags are small; boss DefeatFlags are >=1e6
                        }
                        configData["sweep_flags"] = sweepJson;
                        string sweepLine = $"sweep_flags: {sweep.Count} flags ({sweepGraceFlags} grace, "
                            + $"{sweep.Count - sweepGraceFlags} boss) over {apSweepChecks.Count} checks, {sweepPairs} pairs; "
                            + $"grace mode {bopt.GraceMode}, enemyRando {erando}; {sweepStats}";
                        Console.WriteLine(sweepLine);
                        try
                        {
                            // Succinct readable mapping: each sweep flag -> boss/grace name + check count.
                            string sweepBreakdown = string.Join("\n", sweep
                                .OrderByDescending(kv => kv.Value.Count)
                                .Select(kv => "  " + (sweepFlagNames.TryGetValue(kv.Key, out var _nm) ? _nm : "?")
                                    + " (flag " + kv.Key + "): " + kv.Value.Count + " checks"));
                            File.WriteAllText(Util.ApDiagPath("ap_sweep_diag"),
                                sweepLine + "\ngraces collected: " + apSweepGraces.Count
                                + "\nentity positions (drop-check): " + apEntityPos.Count
                                + "\n\nsweep mappings (boss/grace -> checks):\n" + sweepBreakdown + "\n");
                        }
                        catch { }
                    }
                }
                catch (Exception sweepEx) { Console.WriteLine("boss attribution failed: " + sweepEx); }
            }
            WriteConfigFiles(slotData);

            SetStatusText("Finished!", System.Drawing.Color.Green);
        }

        /// <summary>
        /// Show a dialog visually indicating the location of a parse error in the given YAML
        /// preset. This reformats the preset first, since we're confident that it's syntactically
        /// valid YAML.
        /// </summary>
        private static void DisplayYamlParseError(string presetYaml)
        {
            var obj = new DeserializerBuilder()
                .WithAttemptingUnquotedStringTypeDeserialization()
                .Build()
                .Deserialize(new StringReader(presetYaml));
            var serializer = new SerializerBuilder().WithQuotingNecessaryStrings().Build();
            var formattedYaml = serializer.Serialize(obj);

            try
            {
                Preset.ParsePreset("archipelago", formattedYaml);
            }
            catch (YamlException ex)
            {
                new PresetErrorDialog(formattedYaml, ex).ShowDialog();
            }
        }

        /// <summary>
        /// Returns the SlotKey in candidates whose base item name matches the item name in info.
        /// </summary>
        private static SlotKey FindMatchingSlotKey(ArchipelagoSession session, GameData game, List<SlotKey> candidates, ScoutedItemInfo info)
        {
            if (candidates.Count == 1) return candidates.First();

            var apLocation = session.Locations.GetLocationNameFromId(info.LocationId);
            var defaultItemName = ItemNameForLocation(apLocation);
            // AP location names carry a stack-quantity suffix the param item name lacks (e.g.
            // "Rune Arc x3" vs BaseName "Rune Arc"), so a raw equality check fails for every
            // stacked shop row and this method falls through to candidates.First() -- every row of
            // the shop then binds to one slot/flag (the Moore & Enia collapse). Strip a trailing
            // " xN" on both sides before comparing.
            string normName(string s) => StackQtyRe.Replace(s ?? "", "").Trim();
            var want = normName(defaultItemName);
            var match = candidates.FirstOrDefault(candidate => normName(game.BaseName(candidate.Item)) == want);
            if (match != null) return match;
            // Couldn't disambiguate by item name (e.g. multiple smithing-stone tiers at one
            // breakable-statue scope). Fall back to the first candidate so the run proceeds; they
            // share the same physical location anyway.
            var candNames = string.Join(", ", candidates.Select(c => "'" + normName(game.BaseName(c.Item)) + "'"));
            Console.WriteLine($"WARNING: ambiguous location {apLocation}: want '{want}' among [{candNames}] (n={candidates.Count}) -- using first");
            return candidates.First();
        }

        /// <summary>
        /// Writes or edits the config file for the current Archipelago run.
        /// </summary>
        private void WriteConfigFiles(Dictionary<string, object> slotData)
        {
            var seed = (string)slotData["seed"];
            configData["url"] = url.Text;
            configData["slot"] = name.Text;
            configData["seed"] = seed;
            configData["client_version"] = Version?.ToString();
            if (savePasswordCheckbox.Checked && password.Text.Length > 0)
            {
                configData["password"] = password.Text;
            }
            else
            {
                configData.Remove("password");
            }
            File.WriteAllText(ConfigFileLocation, JsonConvert.SerializeObject(configData));
            // Timestamped snapshot beside the ap_*_<stamp> diags (Util.ApDiagPath -> bake cwd),
            // so every bake's apconfig is preserved + readable even when the in-place
            // apconfig.json is locked/stale. Diagnostic only; ignore failures.
            try {
                File.WriteAllText(Util.ApDiagPath("apconfig").Replace(".txt", ".json"),
                    JsonConvert.SerializeObject(configData, Formatting.Indented));
            } catch { }

            if (me3ConfigData != null)
            {
                me3ConfigData["savefile"] = $"ap-{seed}.sl2";
                if (me3ConfigData.PropertiesMetadata.TryGetProperty("profileVersion", out var metadata))
                {
                    me3ConfigData.PropertiesMetadata.SetProperty("savefile", metadata);
                    me3ConfigData.PropertiesMetadata.SetProperty("profileVersion", new());
                }

                File.WriteAllText(ME3ConfigFileLocation, Toml.FromModel(me3ConfigData).ReplaceLineEndings());
            }
        }

        /// <returns>A human-readable name for a foreign item.</returns>
        private static string SyntheticItemName(ScoutedItemInfo info)
        {
            // Use the player's entire name, if it fits.
            var name = $"{info.Player.Alias}'s {info.ItemName}";
            if (name.Length <= ItemNameLimit) return name;

            // If the player's name doesn't fit, trim it. Don't trim below four characters in case
            // it becomes unrecognizable. This may still result in a string longer than the maximum,
            // but in that case the item name will automatically get trimmed by the game as
            // necessary.
            var charactersToTrim = name.Length - ItemNameLimit;
            var trimmedPlayerName = info.Player.Alias[
                ..Math.Min(
                    info.Player.Alias.Length,
                    Math.Max(info.Player.Alias.Length - charactersToTrim, 4)
                )
            ];
            return $"{trimmedPlayerName} {info.ItemName}";
        }

        /// <summary>
        /// Converts Archipelago options into options for this randomizer.
        /// </summary>
        private RandomizerOptions ConvertRandomizerOptions(Dictionary<string, bool> archiOptions)
        {
            var opt = new RandomizerOptions(type);
            switch (type)
            {
                case FromGame.DS3:
                    opt["onehand"] = archiOptions["require_one_handed_starting_weapons"];
                    opt["ngplusrings"] = archiOptions["enable_ngp"];
                    opt["nongplusrings"] = !archiOptions["enable_ngp"];
                    // Don't randomize NPC equipment. We should add this option when we add
                    // enemizer support. Used for infinite items from shops and enemy drops.
                    opt["nooutfits"] = true;
                    opt["weaponprogression"] = archiOptions["smooth_upgrade_locations"];
                    opt["soulsprogression"] = archiOptions["smooth_soul_locations"];

                    if (archiOptions["randomize_enemies"])
                    {
                        opt["bosses"] = true;
                        opt["enemies"] = true;
                        opt["edittext"] = true;
                        opt["mimics"] = archiOptions["randomize_mimics_with_enemies"];
                        opt["lizards"] = archiOptions["randomize_small_crystal_lizards_with_enemies"];
                        opt["reducepassive"] = archiOptions["reduce_harmless_enemies"];
                        opt["earlyreq"] = archiOptions["simple_early_bosses"];
                        opt["scale"] = archiOptions["scale_enemies"];
                        opt["chests"] = archiOptions["all_chests_are_mimics"];
                        opt["supermimics"] = archiOptions["impatient_mimics"];
                    }

                    if (archiOptions["enable_dlc"])
                    {
                        opt["dlc1"] = true;
                        opt["dlc2"] = true;
                        opt["dlc2fromdlc1"] = true;
                    }
                    else
                    {
                        opt["omitdlc"] = true;
                    }
                    break;

                case FromGame.SDT:
                    if (archiOptions["randomize_enemies"])
                    {
                        opt["bosses"] = true;
                        opt["minibosses"] = true;
                        opt["enemies"] = true;
                        opt["edittext"] = true;
                        opt["phases"] = archiOptions["similar_boss_phases"];
                        opt["phasebuff"] = archiOptions["balanced_endgame_boss_phases"];
                        opt["earlyreq"] = archiOptions["simple_early_minibosses"];
                        opt["scale"] = archiOptions["scale_enemies"];
                    }
                    break;

                case FromGame.ER:
                    // Mirror EldenForm's ER enemy-rando defaults ("enemy" gates the pass, "scale"
                    // rescales moved enemies to the destination tier). Previously there was no ER
                    // case at all, so the AP enemy pass ran with an all-default option set.
                    if (archiOptions.GetValueOrDefault("randomize_enemies", false))
                    {
                        opt["enemy"] = true;
                        opt["scale"] = archiOptions.GetValueOrDefault("scale_enemies", true);
                        // Mirror the GUI's boss QoL defaults: rename boss text to the actual
                        // arena occupant, rebalance multi-phase boss HP in single-phase arenas,
                        // and let boss music follow the boss instead of the arena.
                        opt["editnames"] = true;
                        opt["phasehp"] = true;
                        opt["bossbgm"] = true;
                        // Standard enemy-rando companions: relocated+scaled Malenia keeps her
                        // heal-on-hit and the Gargoyles keep their poison tick otherwise, which
                        // are both degenerate outside their tuned arenas. Delete to restore.
                        opt["nerfmalenia"] = true;
                        opt["nerfgargoyles"] = true;
                        // apworld enemy-rando sub-toggles (shipped as real bools in
                        // slot_data so they survive the bool-only options filter).
                        opt["swapboss"] = archiOptions.GetValueOrDefault("swap_multiboss", false);
                        opt["swaprewards"] = archiOptions.GetValueOrDefault("boss_runes_match", false);
                        opt["impolite"] = archiOptions.GetValueOrDefault("impolite_enemies", false);
                    }
                    // Flatten the regular-weapon upgrade curve: 1 smithing stone per level, like
                    // somber weapons (the GUI's "Reduce upgrade cost for non-somber weapons").
                    // Sensible default for AP runs where stones arrive at the pool's mercy.
                    opt["sombermode"] = true;
                    // Starting-loadout rando only; don't touch NPC outfits (mirrors the DS3 AP
                    // path's reasoning re: shop/drop interplay).
                    opt["nooutfits"] = true;
                    // apworld option (shipped as a real bool in slot_data, so it survives the
                    // bool-only options filter): zero all weapon/ammo/spell stat requirements.
                    opt["weaponreqs"] = archiOptions.GetValueOrDefault("no_weapon_requirements", false);
                    // apworld option: disable upgrading the Serpent-Hunter (base-randomizer
                    // balance tweak); independent of the enemy pass.
                    opt["nerfsh"] = archiOptions.GetValueOrDefault("disable_serpent_hunter_upgrade", false);
                    break;
            }

            // These options aren't actually used, but they're necessary to run the offlien item
            // randomizer for infinite items.
            opt.Difficulty = 50;

            opt["soft_consumable_shop"] = archiOptions.GetValueOrDefault("soft_consumable_shop", false);
            return opt;
        }

        /// <summary>
        /// Computes a stable hash of the given string and reduces it to a single integer.
        /// </summary>
        private static int HashStringToInt(string str)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToInt32(hash.ComputeHash(Encoding.UTF8.GetBytes(str)), 0);
            }
        }

        /// <summary>
        /// Returns a map from Archipelago location IDs to the corresponding location scopes.
        /// </summary>
        private static Dictionary<long, LocationScope> ArchipelagoLocations(
            AnnotationData ann, List<ScoutedItemInfo> locations, Dictionary<string, object> slotData)
        {
            var apIdsToKeys = SlotDataParse.LocationIdsToKeys((JObject)slotData["locationIdsToKeys"]);

            // A map from item names to all the slots that correspond to those names.
            var itemNameToSlots = new Dictionary<string, List<AnnotationData.SlotAnnotation>>();

            // A map from (Archipelago region abbreviation, item name) pairsto all the slots that
            // could correspond to those pairs.
            var locationToSlots = new Dictionary<(string, string), Queue<AnnotationData.SlotAnnotation>>();
            foreach (var slot in ann.SlotsByAnnotationsKey.Values)
            {
                // Some slots have no real area (e.g. "unknown") or an area not present in the
                // annotations; skip them rather than throwing KeyNotFoundException.
                if (slot.Area == null || !ann.Areas.TryGetValue(slot.Area, out var areaAnn)) continue;
                var area = areaAnn.Archipelago;
                if (area == null) continue;

                foreach (var text in slot.DebugText)
                {
                    var itemKey = text.Split(" - ")[0];
                    itemNameToSlots.TryAdd(itemKey, new());
                    itemNameToSlots[itemKey].Add(slot);

                    var locationKey = (area, itemKey);
                    locationToSlots.TryAdd(locationKey, new());
                    locationToSlots[locationKey].Enqueue(slot);
                }
            }

            var locationToCounts =
                locationToSlots.ToDictionary(pair => pair.Key, pair => pair.Value.Count);

            var result = new Dictionary<long, LocationScope>();
            int skippedUnmatchedKeys = 0;
            foreach (var location in locations)
            {
                if (apIdsToKeys.TryGetValue(location.LocationId, out var key))
                {
                    // Base-game-only AP: the server's seed (from a DLC-aware apworld) may reference
                    // slot keys that don't exist in this pre-DLC scrape (DLC maps were dropped at
                    // load). Skip those locations instead of throwing; they just won't be baked.
                    // keyedSlot.LocationScope is null for config-only slots whose game location
                    // isn't in this scrape (DLC-stripped or item lot absent). Treat as unmatched.
                    if (ann.SlotsByAnnotationsKey.TryGetValue(key, out var keyedSlot) && keyedSlot.LocationScope != null)
                    {
                        result[location.LocationId] = keyedSlot.LocationScope;
                    }
                    else
                    {
                        skippedUnmatchedKeys++;
                    }
                    continue;
                }

                var apName = location.LocationName;
                if (apName == null)
                {
                    throw new Exception(
                        $"Can't find a name for location ID {location.LocationId}. This " +
                        "probably indicates a server bug. Try regenerating your Multiworld.");
                }

                // https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net/issues/83
                apName = apName.Replace("Siegbr��u", "Siegbräu");
                var apKey = ParseArchipelagoLocation(apName);
                var (apRegion, itemName) = apKey;
                if (locationToSlots.TryGetValue(apKey, out var locationSlots))
                {
                    if (locationSlots.TryDequeue(out var slot))
                    {
                        result[location.LocationId] = slot.LocationScope;
                        continue;
                    }
                    else
                    {
                        throw new Exception(
                            $"There are only {locationToCounts[apKey]} locations in the offline " +
                            $"randomizer matching \"{apRegion}: {itemName}\", but there are more " +
                            "in Archipelago.");
                    }
                }

                if (itemNameToSlots.TryGetValue(itemName, out var itemSlots) && itemSlots.Count == 1)
                {
                    result[location.LocationId] = itemSlots.First().LocationScope;
                    continue;
                }

                throw new Exception($"Couldn't find a slot that corresponds to Archipelago location \"{apName}\".");
            }
            try
            {
                var kd = new System.Text.StringBuilder();
                kd.AppendLine($"apIdsToKeys (server) count: {apIdsToKeys.Count}");
                kd.AppendLine($"ann.SlotsByAnnotationsKey (scrape) count: {ann.SlotsByAnnotationsKey.Count}");
                kd.AppendLine();
                kd.AppendLine("== first 15 SERVER keys (apIdsToKeys values) ==");
                foreach (var v in apIdsToKeys.Values.Take(15)) kd.AppendLine("  " + v);
                kd.AppendLine();
                kd.AppendLine("== first 15 SCRAPE keys (SlotsByAnnotationsKey keys) ==");
                foreach (var k in ann.SlotsByAnnotationsKey.Keys.Take(15)) kd.AppendLine("  " + k);
                kd.AppendLine();
                int overlap = apIdsToKeys.Values.Distinct().Count(v => ann.SlotsByAnnotationsKey.ContainsKey(v));
                kd.AppendLine($"server keys that exist in scrape: {overlap}");
                File.WriteAllText(Util.ApDiagPath("ap_keys"), kd.ToString());
            }
            catch (Exception kdEx) { Console.WriteLine("key dump failed: " + kdEx); }
            if (skippedUnmatchedKeys > 0)
            {
                Console.WriteLine($"WARNING: skipped {skippedUnmatchedKeys} Archipelago locations whose " +
                    "slot keys aren't in this (base-game-only) scrape. Those locations won't be baked.");
            }
            return result;
        }

        /// <summary>
        /// Parses a full Archipelago location name into its region code and item name.
        /// </summary>
        private static (string, string) ParseArchipelagoLocation(string locationName)
        {
            var rx = new Regex(@"^([A-Z0-9]+): (.*?)(?: - .*)?$", RegexOptions.Compiled);
            var match = rx.Match(locationName);
            if (!match.Success)
            {
                throw new Exception($"Unknown Archipelago location format \"{locationName}\".");
            }

            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        /// <summary>Sets all weapon stat requirements to 0.</summary>
        private static void RemoveWeaponRequirements(GameData game)
        {
            foreach (var row in game.Params["EquipParamWeapon"].Rows)
            {
                foreach (var stat in new[] { "Strength", "Agility", "Magic", "Faith" })
                {
                    row[$"proper{stat}"].Value = 0;
                }
            }
        }

        /// <summary>Sets all spell stat requirements to 0.</summary>
        private static void RemoveSpellRequirements(GameData game)
        {
            foreach (var row in game.Params["Magic"].Rows)
            {
                row["requirementIntellect"].Value = 0;
                row["requirementFaith"].Value = 0;
            }
        }

        /// <summary>Sets the equip burden of all items to 0.</summary>
        private static void RemoveEquipLoad(GameData game)
        {
            foreach (var type in new ItemType[] {
                ItemType.WEAPON, ItemType.ARMOR, ItemType.RING, ItemType.GOOD
            })
            {
                foreach (var row in game.Param(type).Rows)
                {
                    row["weight"].Value = 0;
                }
            }
        }

        private static readonly Regex ApLocationRe = new(@"^[^:]+: (.*?)( - .*)?$");
        // Trailing stack-quantity on an AP location item name (e.g. "Rune Arc x3"); BaseName
        // has none, so it is stripped before name-matching shop rows in FindMatchingSlotKey
        // (else every stacked row collapses onto one slot/event flag).
        private static readonly Regex StackQtyRe = new(@"\s*x\d+$", RegexOptions.IgnoreCase);

        /// <summary>The maximum number of characters in a DS3 item's name.</summary>
        private const int ItemNameLimit = 32;

        /// <summary>
        /// Gets the name of the default item from an Archipelago location name.
        /// </summary>
        private static String ItemNameForLocation(string location)
        {
            var match = ApLocationRe.Match(location);
            if (!match.Success)
            {
                throw new Exception($"Unexpect Archipelago location format \"{location}\"");
            }

            return match.Groups[1].Value;
        }

        /// <summary>
        /// Throws an error if the current DS3 Archipelago version doesn't match the server's
        /// version range.
        /// </summary>
        private static void CheckVersionRange(Dictionary<string, object> slotData)
        {
            if (!slotData.ContainsKey("versions"))
            {
                throw new Exception(
                    "The server's version of the DS3 apworld doesn't include any version " +
                    "information, which means it's not compatible with this static randomizer." +
                    (Version?.IsPreRelease ?? false
                        ? " Make sure you use the apworld that comes with this version to " +
                          "generate the multiworld."
                        : "")
                );
            }
            var range = new SemanticVersioning.Range((string)slotData["versions"]);

            // This should only be the case during development.
            if (Version == null) return;

            // Until we actually make server-side changes for v4, declare ourselves compatible with
            // the 3.x.x branch.
            var compatibleVersion = new SemanticVersioning.Version("3.0.13");
            if (range.IsSatisfied(Version, includePrerelease: true) ||
                range.IsSatisfied(compatibleVersion, includePrerelease: true))
            {
                return;
            }


            throw new Exception(
                $"The server's version of the DS3 apworld supports DS3 AP versions {range}, " +
                $"but this static randomizer is version {Version}."
            );
        }

        /// <summary>
        /// Sets the status text and color on the UI thread. If called from a background thread, it marshals.
        /// If the message ends with "...", it will start blinking.
        /// </summary>
        private void SetStatusText(string message, System.Drawing.Color color = default(System.Drawing.Color))
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatusText(message, color)));
            }
            else
            {
                blinkTimer.Stop();
                status.Text = message;
                if (color != default(System.Drawing.Color))
                {
                    status.ForeColor = color;
                }
                if (message.EndsWith("..."))
                {
                    blinkTimer.Start();
                }
                status.Refresh();
            }
        }

        private void ShowFailure(String message)
        {
            SetStatusText(message, System.Drawing.Color.DarkRed);
            Cursor = Cursors.Default;
            foreach (Control control in Controls)
            {
                control.Enabled = true;
            }
            if (Headless)
            {
                // Unattended batch bake: record the failure in the exit code and close so
                // the driver script moves on to the next seed instead of waiting on a window.
                System.Environment.ExitCode = 1;
                BeginInvoke((Action)(() => Close()));
            }
        }

        [System.AttributeUsage(System.AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
        public sealed class VersionAttribute : System.Attribute
        {
            public SemanticVersioning.Version Version { get; }
            public VersionAttribute(string version)
            {
                this.Version = version == "" ? null : new SemanticVersioning.Version(version);
            }
        }

        /// <summary>
        /// Timer tick event handler to blink the status text.
        /// </summary>
        private void BlinkTimer_Tick(object sender, EventArgs e)
        {
            status.Text = status.Text.EndsWith("...") ? status.Text.Substring(0, status.Text.Length - 2) : status.Text = status.Text + ".";
        }

        /// <summary>
        /// Whenever the password changes, ensure the "Save Password" checkbox is checked or not to
        /// match.
        /// </summary>
        private void password_TextChanged(object sender, EventArgs e)
        {
            if (password.Text.Length > 0)
            {
                savePasswordLabel.Enabled = true;
                savePasswordCheckbox.Enabled = true;
            }
            else
            {
                savePasswordLabel.Enabled = false;
                savePasswordCheckbox.Enabled = false;
            }
        }

        private void ArchipelagoForm_Load(object sender, EventArgs e)
        {
            if (type == FromGame.SDT && !MiscSetup.CheckRequiredSekiroFiles(out var error))
            {
                MessageBox.Show(
                    $"Error setting up static randomizer: {error}",
                    $"Archipelago Randomizer v{Version}",
                    MessageBoxButtons.OK, MessageBoxIcon.Error
                );
                this.Close();
            }
        }
    }
}
