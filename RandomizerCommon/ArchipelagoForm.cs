using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using YamlDotNet.Core.Tokens;
using YamlDotNet.Serialization;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.Util;
using static SoulsIds.GameSpec;

namespace RandomizerCommon
{
    public partial class ArchipelagoForm : Form
    {
        public ArchipelagoForm()
        {
            InitializeComponent();
        }

        private void submit_Click(object sender, EventArgs e)
        {
            Enabled = false;
            status.ForeColor = System.Drawing.SystemColors.GrayText;
            status.Text = "Connecting...";
            status.Visible = true;

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
                    "Dark Souls III",
                    name.Text,
                    Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.NoItems,
                    password: password.Text.Length == 0 ? null : password.Text,
                    version: new Version(0, 4, 3),
                    requestSlotData: false
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

            try
            {
                RandomizeForArchipelago(session);
            }
            catch (Exception ex)
            {
                ShowFailure(ex.Message);
                return;
            }

            MessageBox.Show("Archipelago config loaded successfully!");

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void RandomizeForArchipelago(ArchipelagoSession session)
        {

            status.Text = "Downloading item data...";
            var locations = session.Locations
                .ScoutLocationsAsync(session.Locations.AllLocations.ToArray())
                .Result
                .Values
                .ToList();
            var slotData = session.DataStorage.GetSlotData();
            var apIdsToItemIds = ((JObject)slotData["apIdsToItemIds"]).ToObject<Dictionary<string, int>>()
                .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
            var options = ((JObject)slotData["options"]).ToObject<Dictionary<string, bool>>();
            var opt = ConvertRandomizerOptions(options);
            var itemCounts = ((JObject)slotData["itemCounts"]).ToObject<Dictionary<string, uint>>()
                .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);

            status.Text = "Loading game data...";

            var distDir = "dist";
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
            var game = new GameData(distDir, FromGame.DS3);
            game.Load();
            var scraper = new LocationDataScraper(logUnused: false);
            var data = scraper.FindItems(game);
            var ann = new AnnotationData(game, data);
            ann.Load(opt);
            var events = new Events($@"{game.Dir}\Base\ds3-common.emedf.json", darkScriptMode: true);
            var writer = new PermutationWriter(game, data, ann, events, null);
            var permutation = new Permutation(game, data, ann, new Messages(null));
            var apLocationsToScopes = ArchipelagoLocations(session, ann, locations);

            // The Archipelago API doesn't guarantee that the seed is a number, so we hash it so
            // that we can use it as a seed for C#'s RNG. Add the current player's slot number so
            // that multiple DS3 instances in the same multiworld have different local seeds.
            var seed = HashStringToInt(session.RoomState.Seed) + session.ConnectionInfo.Slot;
            opt.Seed = (uint)seed;
            var random = new Random(seed);

            // Randomize starting loadout *before* adding a bunch of synthetic weapons and armor to
            // the pool that we don't want shoved into shops.
            if (options["random_starting_loadout"])
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
            var itemsToRemove = new Dictionary<SlotKey, SlotKey>();

            foreach (var info in locations)
            {
                var targetScope = apLocationsToScopes[info.LocationId];
                var candidates = data.Location(targetScope);
                SlotKey targetSlotKey;
                if (candidates.Count == 1)
                {
                    targetSlotKey = candidates.First();
                }
                else
                {
                    var apLocation = session.Locations.GetLocationNameFromId(info.LocationId);
                    var defaultItemName = ItemNameForLocation(apLocation);
                    var match = candidates.FirstOrDefault(candidate => game.ItemNames[candidate.Item] == defaultItemName);
                    if (match != null)
                    {
                        targetSlotKey = match;
                    }
                    else
                    {
                        throw new Exception($"Multiple possible locations for {apLocation}: {string.Join(", ", candidates)}");
                    }
                }

                // Tentatively mark all items in this location as not being in the game, unless
                // we've already seen them or we see them later.
                foreach (var itemInLocation in data.Locations[targetScope])
                {
                    itemsToRemove.TryAdd(itemInLocation, targetSlotKey);
                }

                var targetSlot = ann.Slots[targetScope];
                var player = session.Players.Players[session.ConnectionInfo.Team]
                    .First(player => player.Slot == info.Player);

                if (info.Player != session.ConnectionInfo.Slot)
                {
                    // Create a fake key item for each item from another world.
                    var item = writer.AddSyntheticItem(
                        $"{player.Alias}'s {info.ItemName}",
                        $"An object from a mysterious world known only as \"{player.Game}\".",
                        // The highest in-game sortId is 133,100, so for foreign items we start
                        // from 200,000 to sort them after in-game key items. From there we add
                        // the player ID as the primary sort, followed by the item ID (mod 10k
                        // because Archipelago puts all item IDs in a single 54-bit numberspace).
                        // This means that in shops, foreign items will be grouped first by player
                        // and then by item.
                        sortId: 200000 + (uint)info.Player.Slot * 10000 +
                            (uint)(info.ItemId % 10000),
                        archipelagoLocationId: info.LocationId);
                    AddMulti(items, targetSlotKey, item);
                }
                else if (info.ItemName == "Path of the Dragon")
                {
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"Path of the Dragon",
                        "A gesture of meditation channeling the eternal essence of the ancient dragons",
                        "The path to ascendence can be achieved only by the most resolute of seekers. Proper utilization of this technique can grant deep inner focus.",
                        iconId: 7039,
                        archipelagoLocationId: info.LocationId));
                }
                else if (targetScope.ShopIds.Count == 0 && !(targetSlot.Tags?.Contains("crow") ?? false))
                {
                    // The Archipelago mod can't replace items that appear in shops or are dropped
                    // by the crow, so we put more realistic items there. Everywhere else, we
                    // replace with placeholders, so we can notify the Archipelago server when
                    // they're checked. We can't do this with items in shops because we don't have
                    // a good way to replace them on pickup.
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"[Placeholder] {info.ItemName}",
                        "If you can see this your Archipelago mod isn't working.",
                        archipelagoLocationId: info.LocationId,
                        replaceWithInArchipelago: new ItemKey(apIdsToItemIds[info.ItemId]),
                        replaceWithQuantity: itemCounts.GetValueOrDefault(info.ItemId, 1U)));
                }
                else
                {
                    var original = new ItemKey(apIdsToItemIds[info.ItemId]);
                    var (copy, _) = writer.AddSyntheticCopy(original, info.LocationId);
                    AddMulti(items, targetSlotKey, copy);

                    // Because we can't replace items on purchase in the mod the same way we do on
                    // pickup, we rely on custom events to make the swap for us.
                    writer.AddNewEvent(new[]
                    {
                        $"IfPlayerHasdoesntHaveItem(MAIN, {(int)copy.Item.Type}, {copy.Item.ID}, OwnershipState.Owns)",
                        $"RemoveItemFromPlayer({(int)copy.Item.Type}, {copy.Item.ID}, 1)",
                        // The third argument here just needs to be a flag that's always on. 6001
                        // fits the bill.
                        $"DirectlyGivePlayerItem({(int)original.Type}, {original.ID}, 6001, 1)"
                    });
                }
            }

            status.Text = "Randomizing locations...";

            permutation.Forced(items,
                remove: itemsToRemove
                    .Where(entry => entry.Value != null)
                    .GroupBy(entry => entry.Value)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(entry => entry.Key).ToList()));

            permutation.Logic(random, opt, null, new List<Permutation.RandomSilo> {
                Permutation.RandomSilo.INFINITE,
                Permutation.RandomSilo.INFINITE_SHOP,
                Permutation.RandomSilo.INFINITE_GEAR,
                Permutation.RandomSilo.INFINITE_CERTAIN,
                Permutation.RandomSilo.MIXED
            });

            writer.Write(random, permutation, opt, alwaysReplacePathOfTheDragon: true);

            if (options["no_weapon_requirements"])
            {
                RemoveWeaponRequirements(game);
            }

            if (options["no_spell_requirements"])
            {
                RemoveSpellRequirements(game);
            }

            if (options["no_equip_load"])
            {
                RemoveEquipLoad(game);
            }

            if (options["randomize_enemies"])
            {
                EventConfig eventConfig;
                using (var reader = File.OpenText($@"{game.Dir}\Base\events.txt"))
                {
                    var deserializer = new DeserializerBuilder().Build();
                    eventConfig = deserializer.Deserialize<EventConfig>(reader);
                }

                // Serializing this only to parse it again is silly, but YamlDotNet doesn't have
                // any way to deserialize from an object graph
                var preset = Preset.ParsePreset("archipelago", (string)slotData["random_enemy_preset"]);
                preset.RemoveSource = preset.RemoveSource == null
                    ? "Yhorm the Giant"
                    : preset.RemoveSource + ";Yhorm the Giant";
                preset.Enemies ??= new Dictionary<string, string>();
                preset.Enemies[(string)slotData["yhorm"]] = "Yhorm the Giant";
                new EnemyRandomizer(game, events, eventConfig).Run(opt, preset);
            }

            MiscSetup.DS3CommonPass(game, events, opt);

            status.Text = "Writing game files...";
            game.SaveDS3(Directory.GetCurrentDirectory(), true);
        }

        /// <summary>
        /// Converts Archipelago options into options for this randomizer.
        /// </summary>
        private static RandomizerOptions ConvertRandomizerOptions(Dictionary<string, bool> archiOptions)
        {
            var opt = new RandomizerOptions(FromGame.DS3);
            opt["onehand"] = archiOptions["require_one_handed_starting_weapons"];
            opt["ngplusrings"] = archiOptions["enable_ngp"];
            opt["nongplusrings"] = !archiOptions["enable_ngp"];
            opt["nooutfits"] = true; // Don't randomize NPC equipment. We should add this option
                                     // when we add enemizer support.
            // Used for infinite items from shops and enemy drops
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

            // These options aren't actually used, but they're necessary to run the offlien item
            // randomizer for infinite items.
            opt.Difficulty = 50;

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
            ArchipelagoSession session, AnnotationData ann, List<ScoutedItemInfo> locations)
        {
            var slotData = session.DataStorage.GetSlotData();
            var apIdsToKeys = ((JObject)slotData["locationIdsToKeys"])
                .ToObject<Dictionary<string, string>>()
                .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);

            // A map from item names to all the slots that correspond to those names.
            var itemNameToSlots = new Dictionary<string, List<AnnotationData.SlotAnnotation>>();

            // A map from (Archipelago region abbreviation, item name) pairsto all the slots that
            // could correspond to those pairs.
            var locationToSlots = new Dictionary<(string, string), Queue<AnnotationData.SlotAnnotation>>();
            foreach (var slot in ann.SlotsByAnnotationsKey.Values)
            {
                var area = ann.Areas[slot.Area].Archipelago;
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
            foreach (var location in locations)
            {
                if (apIdsToKeys.TryGetValue(location.LocationId, out var key))
                {
                    result[location.LocationId] = ann.SlotsByAnnotationsKey[key].LocationScope;
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
            foreach (var type in new ItemType[] {
                ItemType.WEAPON, ItemType.ARMOR, ItemType.RING, ItemType.GOOD
            })
            {
                foreach (var row in game.Param(type).Rows)
                {
                    foreach (var stat in new[] { "Strength", "Agility", "Magic", "Faith" })
                    {
                        row[$"proper{stat}"].Value = 0;
                    }
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

        private void ShowFailure(String message)
        {
            Enabled = true;
            status.ForeColor = System.Drawing.Color.DarkRed;
            status.Text = message;
        }
    }
}
