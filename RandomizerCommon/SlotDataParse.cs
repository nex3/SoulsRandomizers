using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RandomizerCommon
{
    // Pure helpers for reading Archipelago slot_data, extracted from ArchipelagoForm so the
    // (historically bug-prone) numeric/option parsing is unit-testable without a live AP session.
    // See SPEC-test-coverage.md (P2) and RandomizerCommon.Tests/SlotDataParseTests.
    public static class SlotDataParse
    {
        // slot_data packs category-tagged FullIDs; the GEM category nibble (0x80000000) sets bit 31,
        // which overflows a signed-int32 read. Read the JSON number as long, then reinterpret the low
        // 32 bits unchecked so e.g. 0x80000000 maps to int.MinValue instead of throwing OverflowException.
        public static int ToItemId(long value) => unchecked((int)(uint)value);

        // apIdsToItemIds: { "<apId>": <packedFullId>, ... }  ->  { apId(long) : itemId(int) }
        public static Dictionary<long, int> ApIdsToItemIds(JObject obj) =>
            obj.ToObject<Dictionary<string, long>>()
               .ToDictionary(entry => long.Parse(entry.Key), entry => ToItemId(entry.Value));

        // The bool-typed subset of the options object. ER slot_data also carries non-bool options
        // (e.g. arrays) that would break a strict Dictionary<string,bool> deserialization, so those
        // are read directly from slot_data elsewhere.
        public static Dictionary<string, bool> BoolOptions(JObject options) =>
            options.Properties()
                   .Where(p => p.Value.Type == JTokenType.Boolean)
                   .ToDictionary(p => p.Name, p => p.Value.Value<bool>());

        // locationIdsToKeys: { "<apLocId>": "<scopeKey>", ... }  ->  { apLocId(long) : key(string) }
        public static Dictionary<long, string> LocationIdsToKeys(JObject obj) =>
            obj.ToObject<Dictionary<string, string>>()
               .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
    }
}
