using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;
using Microsoft.VisualBasic.ApplicationServices;
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.Util;
using static SoulsIds.GameSpec;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

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

            var session = ArchipelagoSessionFactory.CreateSession(url.Text);

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

            status.Text = "Downloading item data...";
            var locations = session.Locations.ScoutLocationsAsync(session.Locations.AllLocations.ToArray()).Result;
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

            var random = new Random(session.RoomState.Seed.GetHashCode());

            // A map from locations in the game where items can appear to the list of items that
            // should appear in those locations.
            var items = new Dictionary<SlotKey, List<SlotKey>>();

            // A map from items in the game that should be removed to locations where those items
            // would normally appear, or null if those items should remain in-game (likely because
            // they're assigned elsewhere).
            var itemsToRemove = new Dictionary<SlotKey, SlotKey>();

            foreach (var info in locations.Locations)
            {
                var targetScope = apLocationsToScopes[info.Location];
                var candidates = data.Location(targetScope);
                SlotKey targetSlotKey;
                if (candidates.Count == 1)
                {
                    targetSlotKey = candidates.First();
                }
                else
                {
                    var apLocation = session.Locations.GetLocationNameFromId(info.Location);
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

                var itemName = session.Items.GetItemName(info.Item);
                if (info.Player != session.ConnectionInfo.Slot)
                {
                    var player = session.Players.Players[session.ConnectionInfo.Team]
                        .First(player => player.Slot == info.Player);
                    // Create a fake key item for each item from another world.
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"{player.Alias}'s {itemName}",
                        $"{IndefiniteArticle(itemName)} from a mysterious world known only as \"{player.Game}\".",
                        archipelagoLocationId: info.Location));
                }
                else if (targetScope.ShopIds.Count == 0)
                {
                    // Replace items that can't appear in shops with placeholders, so we can notify
                    // the Archipelago server when they're checked. We can't do this with items in
                    // shops because we don't have a good way to replace them on pickup.
                    AddMulti(items, targetSlotKey, writer.AddSyntheticItem(
                        $"[Placeholder] {itemName}",
                        "If you can see this your Archipelago mod isn't working.",
                        archipelagoLocationId: info.Location,
                        replaceWithInArchipelago: new ItemKey(apIdsToItemIds[info.Item]),
                        replaceWithQuantity: itemCounts.GetValueOrDefault(info.Item, 1U)));
                }
                else
                {
                    var itemKey = new ItemKey(apIdsToItemIds[info.Item]);
                    data.AddLocationlessItem(itemKey);
                    AddMulti(
                        items,
                        targetSlotKey,
                        new SlotKey(itemKey, new ItemScope(ItemScope.ScopeType.SPECIAL, -1)));
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

            // TODO: Respect randomization exclusions here as well.
            permutation.NoLogic(random, new List<Permutation.RandomSilo> {
                Permutation.RandomSilo.INFINITE,
                Permutation.RandomSilo.INFINITE_SHOP,
                Permutation.RandomSilo.INFINITE_GEAR,
                Permutation.RandomSilo.INFINITE_CERTAIN,
                Permutation.RandomSilo.MIXED
            });

            writer.Write(random, permutation, opt);

            if (options["random_starting_loadout"])
            {
                var characters = new CharacterWriter(game, data);
                characters.Write(random, opt);
            }

            MiscSetup.DS3CommonPass(game, events, opt);

            status.Text = "Writing game files...";
            game.SaveDS3(Directory.GetCurrentDirectory(), true);

            this.DialogResult = DialogResult.OK;
            this.Close();
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
            return opt;
        }

        /// <summary>
        /// Returns a map from Archipelago location IDs to the corresponding location scopes.
        /// </summary>
        private static Dictionary<long, LocationScope> ArchipelagoLocations(
            ArchipelagoSession session, AnnotationData ann, LocationInfoPacket locations)
        {
            var slotData = session.DataStorage.GetSlotData();
            var apIdsToKeys = ((JObject)slotData["locationIdsToKeys"])
                .ToObject<Dictionary<string, string>>()
                .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);

            // A map from item names to all the slots that correspond to those names.
            var itemNameToSlots = new Dictionary<string, List<AnnotationData.SlotAnnotation>>();

            // A map from potential Archipelago location names to all the slots that correspond to
            // those names.
            var locationNameToSlots = new Dictionary<string, List<AnnotationData.SlotAnnotation>>();
            foreach (var slot in ann.SlotsByAnnotationsKey.Values)
            {
                var area = ann.Areas[slot.Area].Archipelago;
                if (area == null) continue;

                foreach (var text in slot.DebugText)
                {
                    var itemKey = text.Split(" - ")[0];
                    itemNameToSlots.TryAdd(itemKey, new());
                    itemNameToSlots[itemKey].Add(slot);

                    var locationKey = $"{area}: {itemKey}";
                    locationNameToSlots.TryAdd(locationKey, new());
                    locationNameToSlots[locationKey].Add(slot);
                }
            }

            // Unlike locationNameToSlot, this divides multiple copies of the same item in the same
            // location up by their index and adds that index to the name.
            var locationNameToSlot = new Dictionary<string, AnnotationData.SlotAnnotation>();
            foreach (var (apName, slots) in locationNameToSlots)
            {
                if (slots.Count == 1)
                {
                    locationNameToSlot[apName] = slots.First();
                }
                else
                {
                    for (var i = 0; i < slots.Count; i++)
                    {
                        locationNameToSlot[$"{apName} #{i + 1}"] = slots[i];
                    }
                }
            }

            var result = new Dictionary<long, LocationScope>();
            foreach (var location in locations.Locations)
            {
                if (apIdsToKeys.TryGetValue(location.Location, out var key))
                {
                    result[location.Location] = ann.SlotsByAnnotationsKey[key].LocationScope;
                    continue;
                }

                var apName = session.Locations.GetLocationNameFromId(location.Location);
                if (locationNameToSlot.TryGetValue(apName, out var slot))
                {
                    result[location.Location] = slot.LocationScope;
                    continue;
                }

                var itemName = apName.Split(": ")[1];
                if (itemNameToSlots.TryGetValue(itemName, out var slots) && slots.Count == 1)
                {
                    result[location.Location] = slots.First().LocationScope;
                    continue;
                }

                throw new Exception($"Couldn't find a slot that corresponds to Archipelago location \"{apName}\".");
            }
            return result;
        }

        private static Regex ApLocationRe = new(@"^[^:]+: (.*?)( #\d+| \(.*\))?$");

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
            status.ForeColor = Color.DarkRed;
            status.Text = message;
        }
    }
}
