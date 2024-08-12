using System;
using System.IO;
using YamlDotNet.Serialization;
using static SoulsIds.GameSpec;
using static RandomizerCommon.Util;

namespace RandomizerCommon
{
    public class RandomizerOptionsFile
    {
        public string Game { get; set; }
        public string Version { get; set; }
        public string Options { get; set; }
        public string EnemyPreset { get; set; }
        public string ItemPreset { get; set; }
        [YamlIgnore]
        public RandomizerOptions OptionsValue { get; set; }
        [YamlIgnore]
        public Preset PresetValue { get; set; }

        public static RandomizerOptionsFile Create(string version, RandomizerOptions opt, Preset enemyPreset = null)
        {
            RandomizerOptionsFile val = new RandomizerOptionsFile
            {
                Game = opt.GameNameForFile,
                Version = version,
                Options = opt.FullString(),
            };
            if (enemyPreset != null)
            {
                val.EnemyPreset = enemyPreset.ToYamlString();
            }
            return val;
        }

        public void Save(TextWriter writer)
        {
            ISerializer serializer = new SerializerBuilder().DisableAliases().Build();
            serializer.Serialize(writer, this);
        }

        public static RandomizerOptionsFile Load(string path)
        {
            RandomizerOptionsFile ret = ParseYaml<RandomizerOptionsFile>(path);
            if (ret.Options == null || ret.Game == null || ret.Version == null)
            {
                throw new Exception($"Error: missing required field in options file");
            }
            if (ret.Game != "ER")
            {
                throw new Exception($"Error: unsupported or unknown game \"{ret.Game}\" in options file");
            }
            ret.OptionsValue = RandomizerOptions.Parse(ret.Options.Split(' '), FromGame.ER);
            if (ret.EnemyPreset != null && !string.IsNullOrWhiteSpace(ret.OptionsValue.Preset))
            {
                ret.PresetValue = Preset.ParsePreset(ret.OptionsValue.Preset, ret.EnemyPreset);
            }
            return ret;
        }
    }
}
