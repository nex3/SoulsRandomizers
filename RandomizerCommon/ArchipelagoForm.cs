using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;
using Microsoft.VisualBasic.ApplicationServices;
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
using System.Threading.Tasks;
using System.Windows.Forms;
using static RandomizerCommon.LocationData;
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
                showFailure("Missing Archipelago URL");
                return;
            }

            if (name.Text.Length == 0)
            {
                showFailure("Missing player name");
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
                showFailure(errorMessage);
                return;
            }

            status.Text = "Downloading item data...";
            var locations = session.Locations.ScoutLocationsAsync(session.Locations.AllLocations.ToArray()).Result;
            var archiNames = GetArchipelagoNames(session).Result;

            status.Text = "Loading game data...";

            var opt = new RandomizerOptions(FromGame.DS3);
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
            GameData game = new GameData(distDir, FromGame.DS3);
            game.Load();
            LocationDataScraper scraper = new LocationDataScraper(logUnused: false);
            LocationData data = scraper.FindItems(game);
            AnnotationData ann = new AnnotationData(game, data);
            ann.Load(opt);

            var items = new Dictionary<SlotKey, SlotKey>();
            foreach (var info in locations.Locations)
            {
                var item = archiNames.Items[info.Item];
                if (item.Game != "Dark Souls III")
                {
                    // TODO: generate synthetic items to represent items from other games
                    continue;
                }

                var location = archiNames.Locations[info.Location];
                var targetSlotKey = ann.GetArchipelagoLocation(location.Name);
                if (!ann.ArchipelagoItems.TryGetValue(item.Name, out ItemKey sourceKey))
                {
                    // Don't use game.ItemForName() because there are a number of items that have
                    // multiple possible IDs but it really doesn't matter which we choose.
                    sourceKey = game.RevItemNames[item.Name].First(data.Data.ContainsKey);
                }

                var sourceLocations = data.Data[sourceKey].Locations;
                var sourceScope = sourceLocations[
                    sourceLocations.Keys.First(scope => scope.Type == ItemScope.ScopeType.EVENT || scope.Type == ItemScope.ScopeType.ENTITY)
                ].Scope;
                items[targetSlotKey] = new SlotKey(sourceKey, sourceScope);
            }

            // The permutation writer will complain if we don't explicitly assign Path of the
            // Dragon to a location.
            //
            // TODO: This code didn't work so I had to comment out the check in PermutationWriter
            // instead. Figure out why this isn't working and fix it—we want to be able to
            // randomize this from Archipelago eventually.
            // var dragonSlotKey = new SlotKey(game.ItemForName("Path of the Dragon"), new ItemScope(ItemScope.ScopeType.SPECIAL, -1));
            // items[dragonSlotKey] = dragonSlotKey;

            status.Text = "Randomizing locations...";
            var permutation = new Permutation(game, data, ann, new Messages(null));
            permutation.Forced(items);

            var events = new Events($@"{game.Dir}\Base\ds3-common.emedf.json", darkScriptMode: true);
            var writer = new PermutationWriter(game, data, ann, events, null);
            // TODO: Seed the random number generator based on the Archipelago seed
            writer.Write(new Random(), permutation, opt);

            MiscSetup.DS3CommonPass(game, events, opt);

            status.Text = "Writing game files...";
            game.SaveDS3(Directory.GetCurrentDirectory(), true);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void showFailure(String message)
        {
            Enabled = true;
            status.ForeColor = Color.DarkRed;
            status.Text = message;
        }

        private Task<ArchipelagoNames> GetArchipelagoNames(ArchipelagoSession session)
        {
            var games = session.Players.AllPlayers.Select((player) => player.Game).Distinct().ToArray();
            var source = new TaskCompletionSource<ArchipelagoNames>();
            session.Socket.SendPacket(new GetDataPackagePacket() { Games = games });

            void onData(ArchipelagoPacketBase packet)
            {
                session.Socket.PacketReceived -= onData;
                session.Socket.ErrorReceived -= onError;
                var data = ((DataPackagePacket)packet).DataPackage;
                var items = new Dictionary<long, ArchipelagoEntity>();
                var locations = new Dictionary<long, ArchipelagoEntity>();
                foreach (var (game, gameData) in data.Games)
                {
                    foreach (var (name, id) in gameData.ItemLookup)
                    {
                        items[id] = new ArchipelagoEntity(id, name, game);
                    }
                    foreach (var (name, id) in gameData.LocationLookup)
                    {
                        locations[id] = new ArchipelagoEntity(id, name, game);
                    }
                }
                source.SetResult(new ArchipelagoNames(items, locations));
            }

            void onError(Exception e, string message)
            {
                session.Socket.PacketReceived -= onData;
                session.Socket.ErrorReceived -= onError;
                source.SetException(e);
            }

            session.Socket.PacketReceived += onData;
            session.Socket.ErrorReceived += onError;

            return source.Task;
        }

        /// <summary>
        /// A mapping from this Archipelago session's numeric ID for each item and location to full
        /// information about each of those entities.
        /// </summary>
        private readonly struct ArchipelagoNames
        {
            public Dictionary<long, ArchipelagoEntity> Items { get; init; }
            public Dictionary<long, ArchipelagoEntity> Locations { get; init; }

            public ArchipelagoNames(
                Dictionary<long, ArchipelagoEntity> items,
                Dictionary<long, ArchipelagoEntity> locations)
            {
                this.Items = items;
                this.Locations = locations;
            }
        }

        /// <summary>
        /// Information about a location or item as understood by the Archipelago server.
        /// </summary>
        private readonly struct ArchipelagoEntity
        {
            /// <summary>
            ///  The numeric ID for this entity. Different for each Archipelago session.
            /// </summary>
            public long ID { get; init; }

            /// <summary>
            /// This entity's name according to the Archipelago world.
            /// </summary>
            public string Name { get; init; }

            /// <summary>
            /// The name of the game in which this entity appears.
            /// </summary>
            public string Game { get; init; }

            public ArchipelagoEntity(long id, string name, string game)
            {
                ID = id;
                Name = name;
                Game = game;
            }

            public override string ToString()
            {
                return $"{Name} ({ID})";
            }
        }
    }
}
