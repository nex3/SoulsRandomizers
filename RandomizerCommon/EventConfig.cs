using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoulsFormats;
using SoulsIds;
using YamlDotNet.Serialization;
using static SoulsIds.Events;
using NCalc;

namespace RandomizerCommon
{
    public class EventConfig
    {
        /// <summary>A map from map names to events events to add to those maps.</summary>
        public Dictionary<string, List<NewEvent>> NewEvents { get; set; } = new();

        /// <summary>
        /// A map from map names and event IDs to Existing events to modify, or potentially just to
        /// give names so they can be more easily referenced.
        /// </summary>
        public Dictionary<string, Dictionary<long, ExistingEvent>> ExistingEvents
        {
            get;
            set;
        } = new();

        /// <summary>
        /// A map from map names to new initializers to add to event 0 for those maps.
        /// </summary>
        public Dictionary<string, List<AddInitializer>> Initialize { get; set; } = new();

        public List<CommandSegment> DefaultSegments { get; set; }
        // Maybe "item" config should be split up in a different file
        public List<EventSpec> ItemTalks { get; set; }
        public List<EventSpec> ItemEvents { get; set; }
        public List<EventSpec> EnemyEvents { get; set; }
        public List<InstructionValueSpec> ValueTypes { get; set; }

        /// <summary>
        /// The base class for classes that create or edit events.
        /// </summary>
        public abstract class BaseEvent
        {
            /// <summary>Our name for the event, which we can use to refer to it easily.</summary>
            /// <remarks>
            /// This is typically used for events which are initialized multiple times using
            /// <c>GameData.AddInitializer</c>.
            /// </remarks>
            public string Name { get; set; }

            /// <summary>Documentation for this event.</summary>
            public string Comment { get; set; }

            /// <summary>
            /// A boolean expression. This event is only applied if it returns true.
            /// </summary>
            /// <remarks>
            /// <para>
            /// This can access any boolean in <c>RandomizerOptions</c> as an identifier.
            /// </para>
            /// 
            /// <para>This can be checked using <c>IncludeFor</c>.</para>
            /// </remarks>
            public string If { get; set; }

            /// <returns>
            /// Whether this event should be included given the selected randomizer options.
            /// </returns>
            public bool IncludeFor(RandomizerOptions opt)
            {
                if (If == null) return true;
                var expression = new Expression(If);
                expression.EvaluateParameter += delegate (string name, ParameterArgs args)
                {
                    args.Result = opt[name];
                };
                return (bool)expression.Evaluate();
            }
        }

        /// <summary>A new event to add to the game.</summary>
        public class NewEvent : BaseEvent
        {
            /// <summary>This event's beahvior when the player rests at a bonfire.</summary>
            public EMEVD.Event.RestBehaviorType Rest { get; set; } = EMEVD.Event.RestBehaviorType.Default;

            /// <summary>The names of arguments that can be passed to this event.</summary>
            /// <remarks>
            /// Setting these is optional. However, once set, arguments can be referenced by name
            /// in <c cref="Commands">Commands</c>.
            /// </remarks>
            public List<EventArgument> Arguments { get; set; } = new();

            /// <summary>The list of EMEVD commands to run for this event.</summary>
            /// <remarks>This can refer to <c cref="Arguments">Arguments</c> by name.</remarks>
            public List<string> Commands {
                get
                {
                    if (Arguments.Count == 0) return commands;

                    var argReplacements = new List<(Regex, string)>();
                    var totalBytes = 0;
                    foreach (var arg in Arguments)
                    {
                        if (!arg.Name.All(char.IsLetterOrDigit))
                        {
                            throw new Exception(
                                $"Argument \"{arg.Name}\" of event {Name} must be alphanumeric.");
                        }

                        var standardName = "X";

                        // A single argument never crosses the 4-aligned boundary. X0_1 can be
                        // followed by X1_1, but not X1_4; it has to be X4_4 instead.
                        if (totalBytes % 4 + arg.Width <= 4)
                        {
                            standardName += totalBytes;
                        }
                        else
                        {
                            // Move totalBytes to the next 4-aligned boundary.
                            totalBytes += 4 - totalBytes % 4;
                            standardName += totalBytes;
                        }
                        totalBytes += arg.Width;
                        standardName += "_" + arg.Width;
                        argReplacements.Add(
                            (new Regex($"\\b{Regex.Escape(arg.Name)}\\b"), standardName));
                    }

                    var result = commands.Select(command =>
                    {
                        foreach (var (regex, standardName) in argReplacements)
                        {
                            command = regex.Replace(command, standardName);
                        }
                        return command;
                    }).ToList();
                    return result;
                }

                set { commands = value; }
            }
            private List<string> commands;
        }

        /// <summary>An argument passed to an EMEVD event.</summary>
        public class EventArgument : BaseEvent
        {
            /// <summary>The width (in bytes) of the argument.</summary>
            /// <remarks>
            /// EMEVD arguments are typically written `XA_B`, where `X` is a literal character "X",
            /// `A` is the byte position of the argument, and `B` is the byte width of the argument.
            /// Most arguments are 4 bytes wide, but in some cases an argument will be smaller (for
            /// example, booleans are often 1 byte wide).
            /// </remarks>
            public int Width { get; set; } = 4;

            public static explicit operator EventArgument(string s) => new() { Name = s };
        }

        /// <summary>An event that already exists in the game.</summary>
        /// <remarks>
        /// This can just provide a name to a function we might want to call, or it can make more
        /// detailed <c>Edits</c> to change the behavior of an existing event.
        /// </remarks>
        public class ExistingEvent : BaseEvent
        {
            /// <summary>A list of edits to make to this event.</summary>
            public List<EventEdit> Edits { get; set; } = new();
        }

        /// <summary>A single edit to apply to an existing event.</summary>
        /// <remarks>
        /// An edit has two critical components: the <c>Matcher</c> which determines which
        /// instruction(s) in the event to change, and the other properties which indicate which
        /// change(s) to make. Only one change may be made per edit.
        /// </remarks>
        public class EventEdit
        {
            /// <summary>Options for which matching instructions to remove.</summary>
            public enum RemoveType
            {
                /// <summary>The default: do not remove matching instructions.</summary>
                None,
                /// <summary>Remove the first matching region.</summary>
                First,
                /// <summary>Remove all matching instructions.</summary>
                All,
            }

            /// <summary>The matcher which indicates which instruction to choose.</summary>
            /// <remarks>
            /// <para>
            /// If this matches multiple events, only the first will be modified. If it doesn't
            /// match any, it will throw an error.
            /// </para>
            /// 
            /// <para>
            /// An empty matcher is considered to match the whole event as a region.
            /// </para>
            /// </remarks>
            /// <seealso cref="MatchLength"/>
            public InstructionMatcher Match { get; set; }

            /// <summary>The length of the match. Defaults to 1.</summary>
            /// <remarks>
            /// When multiple lines are matched, this is referred to as the "region". Only certain
            /// actions care about the region; others will throw an error if MatchLength isn't 1.
            /// </remarks>
            public int MatchLength { get; set; } = 1;

            /// <summary>Sets a parameter of a matched instruction.</summary>
            public SetEdit Set { get; set; }

            /// <summary>If true, removes matching instructions entirely.</summary>
            public RemoveType Remove { get; set; } = RemoveType.None;

            /// <summary>A list of commands to insert before the matching region.</summary>
            public List<string> AddBefore { get; set; } = new();

            /// <summary>A list of commands to insert after the matching region.</summary>
            public List<string> AddAfter { get; set; } = new();

            /// <summary>Performs the chosen edit on <paramref name="ev"/>.</summary>
            /// <param name="ev">The event being edited.</param>
            /// <param name="events">
            /// The event metadata used to decode information about <paramref name="ev"/>.
            /// </param>
            /// <remarks>Throws an exception if no instruction matches <c>Match</c>.</remarks>
            public void Edit(EMEVD.Event ev, Events events)
            {
                var matchLength = Match == null ? ev.Instructions.Count : MatchLength;

                var editTypes = 0;
                if (Set!= null) editTypes++;
                if (Remove != RemoveType.None) editTypes++;
                if (AddBefore.Count > 0) editTypes++;
                if (AddAfter.Count > 0) editTypes++;
                if (editTypes > 1)
                {
                    throw new Exception("Each EventEdit may only contain one edit");
                }

                // EMEVD handles parameters unusually. Rather than using any kind of variable or
                // register, it begins events with special instructions that rewrite all the
                // parameter bytes in the rest of the event with their values. This means that in
                // order to safely make edits that add or remove instructions, we have to update the
                // parameter references as well. Fortunately, OldParams handles that for us as long
                // as we tell it which new instructions we're adding.
                var pre = OldParams.Preprocess(ev);
                if (Remove == RemoveType.All)
                {
                    AssertNoRegion("Remove: All");
                    var removed = ev.Instructions
                        .RemoveAll(inst => Match.Match(events.Parse(inst, pre), events));
                    pre.Postprocess();
                    if (removed > 0) return;
                    throw new Exception("Expected Remove: All EditEvent to match an instruction");
                }

                for (var i = 0; i < ev.Instructions.Count; i++)
                {
                    var instr = events.Parse(ev.Instructions[i]);
                    if (Match != null && !Match.Match(instr, events)) continue;

                    if (Set != null)
                    {
                        AssertNoRegion("Set");
                        Set.Edit(instr);
                        instr.Save(pre);
                    }
                    else if (Remove == RemoveType.First)
                    {
                        ev.Instructions.RemoveRange(i, matchLength);
                    }
                    else if (AddBefore.Count > 0)
                    {
                        ev.Instructions.InsertRange(i, ParseInstructions(AddBefore, events, pre));
                    }
                    else if (AddAfter.Count > 0)
                    {
                        ev.Instructions.InsertRange(
                            i + matchLength, ParseInstructions(AddAfter, events, pre));
                    }

                    pre.Postprocess();
                    return;
                }

                throw new Exception("Expected Remove: All EditEvent to match an instruction");
            }

            /// <summary>
            /// Throws an error if this edit applies to a region, as opposed to a single
            /// instruction.
            /// </summary>
            private void AssertNoRegion(string action)
            {
                if (Match == null || MatchLength != 1)
                {
                    throw new Exception($"Edit action {action} doesn't apply to a region.");
                }
            }

            /// <summary>Parses a list of text instructions to add to the event.</summary>
            private static List<EMEVD.Instruction> ParseInstructions(
                IEnumerable<string> instructions,
                Events events,
                OldParams pre
            )
            {
                return instructions.Select(text =>
                {
                    var (inst, ps) = events.ParseAddArg(text);
                    pre.AddParameters(inst, ps);
                    return inst;
                }).ToList();
            }
        }

        /// <summary>
        /// The interface for matchers that select particular event instructions.
        /// </summary>
        public interface IInstructionMatcher
        {
            /// <returns>Whether this matches a given instruction.</returns>
            /// <param name="events">Additional EMEDF metadata for parsing events.</param>
            public bool Match(Instr instr, Events events);
        }

        /// <summary>
        /// A matcher which indicates which instruction to choose for an <c>EventEdit</c>.
        /// </summary>
        /// <remarks>
        /// This can include multiple matcher conditions, in which case all of them must match in
        /// order for the matcher to match.
        /// </remarks>
        public class InstructionMatcher : IInstructionMatcher
        {
            /// <summary>A matcher for initializer instructions found in event 0s.</summary>
            public InitMatcher Init { get; set; }

            /// <summary>A literal instruction to match.</summary>
            public string Instruction { get; set; }

            /// <summary>
            /// Parses a literal string as an instruction, which is matched exactly.
            /// </summary>
            public static explicit operator InstructionMatcher(string instruction) =>
                // Parameters are replaced with 0s when parsed. We can't distinguish between actual
                // params and literal 0s because parameters are actually substituted in-place just
                // before an event is run.
                new() { Instruction = Regex.Replace(instruction, "\\bX[0-9]+_[0-9]+\\b", "0") };

            public bool Match(Instr instr, Events events)
            {
                return Init?.Match(instr, events) ?? true &&
                    MatchInstruction(instr, events);
            }

            /// <returns>whether <paramref name="instr"/> matches <c>Instruction</c>.</returns>
            private bool MatchInstruction(Instr instr, Events events)
            {
                if (Instruction == null) return true;
                var expected = events.ParseAdd(Instruction);
                return expected.Bank == instr.Val.Bank &&
                    expected.ID == instr.Val.ID &&
                    Enumerable.SequenceEqual(expected.ArgData, instr.Val.ArgData);
            }
        }

        /// <summary>
        /// A matcher that matches an instruction (typically in event 0) that initializes a
        /// specific event.
        /// </summary>
        public class InitMatcher : IInstructionMatcher
        {
            /// <summary>
            /// The initializer index (that is, the first argument to the initializer).
            /// </summary>
            public int? Index {  get; set; }

            /// <summary>The ID of the event being initialized by this instruction.</summary>
            public int? Callee { get; set; }

            /// <summary>Specific arguments to match. Null arguments match any value.</summary>
            /// <remarks>
            /// Neither the initializer index nor the callee are considered arguemnts. An
            /// initializer fewer args than listed here will not match, but an initializer with more
            /// will.
            /// </remarks>
            public List<int?> Arguments { get; set; } = new();

            public static explicit operator InitMatcher(int? callee) => new() { Callee = callee };

            public bool Match(Instr instr, Events events)
            {
                if (!instr.Init) return false;
                if (Index != null && (int)instr[0] != Index) return false;
                if (Callee != null && instr.Callee != Callee) return false;
                for (var i = 0; i < Arguments.Count; i++)
                {
                    var expected = Arguments[i];
                    if (expected == null) continue;
                    if ((int)instr[instr.Offset + i] != expected) return false;
                }
                return true;
            }
        }

        /// <summary>Sets the value of a specific parameter of a matched instruction.</summary>
        public class SetEdit
        {
            /// <summary>The parameter to set.</summary>
            public InstructionParameter Param { get; set; }

            /// <summary>The new value for the parameter.</summary>
            public uint Value { get; set; }

            public void Edit(Instr instr)
            {
                instr[Param.Offset(instr)] = Value;
            }
        }

        /// <summary>A means of determining a specific parameter in an EMEVD instruction.</summary>
        /// <remarks>
        /// It's an error to set multiple different means of finding the parameter.
        /// </remarks>
        public class InstructionParameter
        {
            /// <summary>A simple 0-based index into the parameter list.</summary>
            public int? Index { get; set; }

            /// <summary>The camel-case parameter name from EMEDF.</summary>
            public string Name { get; set; }

            /// <summary>The argument passed to the event invoked by an initializer.</summary>
            /// <remarks>This requires that the instruction is an initialization.</remarks>
            public int? InitArg { get; set; }

            public static explicit operator InstructionParameter(int index) =>
                new() { Index = index };

            public static explicit operator InstructionParameter(string name) =>
                new() { Name = name };

            /// <returns>
            /// The offset into <c>instr</c>'s arguments that this parameter indicates.
            /// </returns>
            public int Offset(Instr instr)
            {
                var setTypes = 0;
                if (Index != null) setTypes++;
                if (Name != null) setTypes++;
                if (InitArg != null) setTypes++;
                if (setTypes > 1) throw new Exception("Can't set multiple parameter types at once");

                if (Index is int index) return index;
                if (Name is string name)
                {
                    for (var i = 0; i < instr.Doc.Arguments.Length; i++)
                    {
                        if (instr.Doc.Arguments[i].Name == name) return i;
                    }
                    throw new Exception(
                        $"Insruction {instr.Name} has no argument {name}. Arguments: " +
                        String.Join(", ", instr.Doc.Arguments.Select(doc => doc.Name)));
                }
                if (InitArg is int initArg) return instr.Offset + initArg;

                throw new Exception("No parameter provided");
            }
        }

        /// <summary>A representation of an initializer to add for a given map.</summary>
        public class AddInitializer
        {
            /// <summary>The numeric ID of the event to initialize.</summary>
            public int? ID { get; set; }

            /// <summary>The name we've provided to the event to initialize.</summary>
            /// <remarks>
            /// This is set via <c>ExistingEvent.Name</c> or <c>AddEvent.Name</c>.
            /// </remarks>
            public string Name { get; set; }

            /// <summary>The arguments to pass to this initializer.</summary>
            public List<int> Arguments { get; set; } = new();
        }

        public class EventSpec : AbstractEventSpec
        {
            // If true, the template was automatically generated and will be overwritten in the future
            public bool Auto { get; set; }
            // Shorthand for simple dupe behaviors, "rewrite" or "copy"
            public string Dupe { get; set; }
            // All entities and entity args mentioned by name in this event, used for automatic dupe copy
            public string Entities { get; set; }
            // On duplicated events, the dupe index for entity-rewriting purposes.
            // The rewriting itself only happens after processing all templates.
            [YamlIgnore]
            public int DupeIndex { get; set; } = -1;
            public List<EnemyTemplate> Template { get; set; }
            public List<ItemTemplate> ItemTemplate { get; set; }

            public EventSpec DeepCopy()
            {
                // Despite the name, does not deep-copy debug strings from parent class, which would be expensive anyway
                EventSpec o = (EventSpec)MemberwiseClone();
                if (o.Template != null) o.Template = o.Template.Select(x => x.DeepCopy()).ToList();
                if (o.ItemTemplate != null) o.ItemTemplate = o.ItemTemplate.Select(x => x.DeepCopy()).ToList();
                return o;
            }
        }

        public class EnemyTemplate
        {
            // chr, multichr, loc, start, end, startphase, endphase, remove, segment
            // chr and multichr create copies
            public string Type { get; set; }
            // Documentation on edits being made
            public string Comment { get; set; }
            // The affected entities, if a chr command or if conds/cmds are used below which need to be transplanted
            public int Entity { get; set; }
            // The other entity involved. The source for locs, or the target for chrs. (Currently only works for loc)
            // Currently only used for Divine Dragon as Tutorial Genichiro, and DS3 easter egg
            public int Transfer { get; set; }
            // All possible affected entities, generally for removing initializations/commands conditionally
            public string Entities { get; set; }
            // Arg positions of entities, used instead of inferring them by id
            public string ArgEntities { get; set; }
            // Arg positions of flags, for flag-replacement features, to keep things speedy
            public string ArgFlags { get; set; }
            // A flag which ends this event when on, if chr
            // TODO: Can this be string, for arg flags?
            public int DefeatFlag { get; set; }
            // Label to goto when defeat flag is off, or if there is no defeat flag
            public string DefeatFlagLabel { get; set; }
            // A flag which ends this event when off, if chr
            public int AppearFlag { get; set; }
            // A 5xxx flag which this event waits for (phase change or boss fight), or the flag itself if start event
            public int StartFlag { get; set; }
            // Flags which set fight state used between events. Used for double feature, mainly
            public string ProgressFlag { get; set; }
            // Editing events to replace a flag usage with an speffect usage, "<index> <entity> <flag> [<arg>]"
            public string EffectFlag { get; set; }
            // Phase change flag is set in this event. This is replaced/removed if a single flag, or else if
            // "<command> -> <flag>", added after matching command.
            public string MusicFlag { get; set; }
            // Place where phase change flag is used, so it can be rewritten if the actual music flag is rewritten.
            // In the case of phase transitions with cutscenes, each one should unset and set the music flag, but this
            // needs to use a custom music flag if the start flag is already taken (in particular, with copyphase, dupeIndex -1).
            public string MusicFlagArg { get; set; }
            // The condition groups used to end a boss fight, first for music flag and second for permanent flag. Either a group or a command name (with cond group 0)
            public string EndCond { get; set; }
            public string EndCond2 { get; set; }
            // Moving CameraSetParam ids between regions in Sekiro
            public string Camera { get; set; }
            // A finisher deathblow, to add conditions to stop it from proccing unnecessarily
            public int Deathblow { get; set; }
            // This character's invincibility is managed here, so after they lose it, their immortality may need to be reset if an immortal boss
            // In DS3, this also sets invincibility/immortality of other enemies in the fight
            public int Invincibility { get; set; }
            // Replace idle/wakeup character animations, like 700 1700, for whoever gets placed in this enemy.
            // The main format is like "<entity> <initial> <wakeup>", 0 if not provided in this event, and arg refs acceptable.
            // A different format "gravity <eventid>" is supported on common funcs when a gravity-less version is available.
            public string Animation { get; set; }
            // Replacing boss/miniboss health bar names. Either "entity" to refer to Entity, or "<entity> <name>" containing ints or arg refs
            public string Name { get; set; }
            // Rewriting multiplayer buff entity, if it no longer applies (because of RemoveGroup or if it's not a group).
            // For the moment, only an arg spec
            public string MultiplayerBuff { get; set; }
            // In Sekiro, commands used when starting a boss fight for this entity, like SetLockOnPoint, ForceAnimationPlayback, SetDispMask, SetAIId
            public string StartCmd { get; set; }
            // Data for modifying the contents of an event when the entity is duplicated into another one
            public Dupe Dupe { get; set; }
            // Commands to change when the tree dragon entity is disabled, with the argument spec as the first semicolon-separate value.
            public string TreeDragons { get; set; }
            // Directive to rewrite event flags depending on which tree dragons are enabled for lightning.
            public string TreeDragonFlags { get; set; }
            // What to do with regions if a chr command - chrpoint (exact), arenapoint (center/random), arenabox10 (random), arena (bgm), arenasfx (center), or dist10.
            public List<string> Regions { get; set; }
            // Commands to add
            public List<EventAddCommand> Add { get; set; }
            // Commands to unconditionally remove.
            public string Remove { get; set; }
            public List<string> Removes { get; set; }
            // Commands to unconditionally remove when enemy is not unique
            public string RemoveDupe { get; set; }
            // Args to replace (TODO: replacing entire commands, selecting EventValueTypes, and 'Replaces')
            public string Replace { get; set; }
            public List<EventReplaceCommand> Replaces { get; set; }
            // When provided, rewrites the entire event to the given commands, before applying other edits.
            // This is effectively like creating a brand new event, but per enemy randomization target.
            public List<string> NewEvent { get; set; }
            // Replacement for StartCmd/EndCond which is more general, as segments which must be transplanted to other segments
            public List<CommandSegment> Segments { get; set; }
            // Heuristic check for doing nothing. TODO fully migrate to "default" type
            public bool IsDefault() =>
                Entity == 0 && DefeatFlag == 0 && AppearFlag == 0 && StartFlag == 0 && MusicFlag == null
                    && EndCond == null && EndCond2 == null && StartCmd == null && Segments == null && Dupe == null
                    && Remove == null && RemoveDupe == null && Replace == null && Add == null && NewEvent == null
                    && Replaces == null && Removes == null && DefeatFlagLabel == null
                    && Regions == null && Camera == null && Invincibility == 0 && Deathblow == 0 && Name == null
                    && TreeDragons == null && TreeDragonFlags == null && Animation == null;

            public EnemyTemplate ShallowCopy() => (EnemyTemplate)MemberwiseClone();

            public EnemyTemplate DeepCopy()
            {
                // Not handled here: Dupe
                EnemyTemplate o = (EnemyTemplate)MemberwiseClone();
                if (o.Regions != null) o.Regions = o.Regions.ToList();
                if (o.Removes != null) o.Removes = o.Removes.ToList();
                if (o.NewEvent != null) o.NewEvent = o.NewEvent.ToList();
                if (o.Add != null) o.Add = o.Add.Select(x => x.DeepCopy()).ToList();
                if (o.Replaces != null) o.Replaces = o.Replaces.Select(x => x.DeepCopy()).ToList();
                if (o.Segments != null) o.Segments = o.Segments.Select(x => x.DeepCopy()).ToList();
                return o;
            }
        }

        public class CommandSegment
        {
            // Type summary:
            // disable: Initialization that leaves enemies disabled, when followed by "end if defeated" condition
            // dead: Initialization that leaves enemies disabled, preceded by "goto if alive" and followed by unconditional end
            // setup: Block that leaves boss enabled and inactive
            // altsetup: If setup does not exist, alternate commands to do so (won't be removed from event later)
            // firstsetup: After setup, sets up the boss to appear dynamically at some point
            // secondsetup: After setup, finishes enabling the boss
            // firststart: Makes the boss dynamically appear, before starting the fight as usual
            // secondstart: Start commands when the boss is already enabled, like playing animations
            // start: Includes activation and boss health bar
            // quickstart: No-op for minibosses, a target for setup/start for regular bosses (except healthbars)
            // healthbar: Repeatable healthbar-showing event
            // unhealthbar: Repeatable healthbar-hiding event
            // end: End condition, music cue, and HandleBossDefeat
            // endphase: Only end condition
            // remove: Selection to remove. This is somewhat redundant with Remove attribute in templates, but bounded like segments
            // All bosses must provide:
            // 1. dead or disable - disables boss
            // 2. setup/altsetup/secondsetup - following dead/disable, makes boss enabled and inactive
            // 4. start or quickstart - following setups, make boss enabled and active
            // 5. end or endphase - condition for boss death which disables them after
            // All bosses providing presetup must provide first/second setup/start
            // All minibosses providing quickstart must also provide healthbar and unhealthbar
            public string Type { get; set; }
            // Whether the replacement spot for the should be based on Start or the first match
            public bool IgnoreMatch { get; set; }
            // Whether to add invincibility commands to this location, to enable (in setup) and disable (in start).
            public bool Invincibility { get; set; }
            // Params in this segment to substitute when transplanted elsewhere
            public string Params { get; set; }
            // Prerequisite segment for Start matching if ambiguity is possible
            public string PreSegment { get; set; }
            // Starting command for Commands to match against (must be GotoIf for dead)
            // For other types, may be semi-colon separated if there are multiple
            public string Start { get; set; }
            // Ending command for Commands to match against (must be End for dead, EndIf for disable)
            public string End { get; set; }
            // For dupes only, although may be usable for limited amounts of singleton bosses
            public string ProgressFlag { get; set; }
            // Regions used in this segment (these behave differently from chr template Regions, as they apply to the segment commands only)
            public List<string> Regions { get; set; }
            // Commands from below which should be removed when going to a miniboss, and vice versa.
            // Does not support params currently.
            public List<string> EncounterOnly { get; set; }
            public List<string> NonEncounterOnly { get; set; }
            // Removed when swapping out helper boss types.
            public List<string> SpecificHelperOnly { get; set; }
            // Removed when the entity is not moved
            public List<string> MoveOnly { get; set; }
            public List<string> NonMoveOnly { get; set; }
            // Commands, which mostly match ones in the range. (This can include some custom commands? hide, show, fall damage)
            // If not altsetup and not NoMatch, this must include at least one matching instruction
            public List<string> Commands { get; set; }
            // Commands after parameter substitution, if applicable
            [YamlIgnore]
            public List<string> NewCommands { get; set; }

            public CommandSegment ShallowCopy() => (CommandSegment)MemberwiseClone();
            public CommandSegment DeepCopy()
            {
                CommandSegment o = ShallowCopy();
                if (o.Regions != null) o.Regions = o.Regions.ToList();
                if (o.EncounterOnly != null) o.EncounterOnly = o.EncounterOnly.ToList();
                if (o.NonEncounterOnly != null) o.NonEncounterOnly = o.NonEncounterOnly.ToList();
                if (o.SpecificHelperOnly != null) o.SpecificHelperOnly = o.SpecificHelperOnly.ToList();
                if (o.Commands != null) o.Commands = o.Commands.ToList();
                if (o.NewCommands != null) o.NewCommands = o.NewCommands.ToList();
                return o;
            }
        }

        // Parsed segment data
        public class SegmentData
        {
            public List<EMEVD.Instruction> Instructions { get; set; }
            public List<string> Regions { get; set; }
        }

        public class SourceSegmentData
        {
            // -1 if the main instance, 0 1 2 etc for the dupe otherwise
            public int DupeIndex { get; set; }
            // Entity id of source segment owner
            public int Source { get; set; }
            // The entity target (by default the template entity, but may be duplicate targets instead)
             public int Target { get; set; }
            // If moved from original location
            public bool IsRandom { get; set; }
            // Mapping from source segment entities to target entities, including ID->Target
            public Dictionary<EventValue, EventValue> Reloc { get; set; }
            // All defined source segments
            public Dictionary<string, CommandSegment> Segments { get; set; }
            // If the source has encounter segments (first/second setup/start segments), for filling in setup/start in target
            public bool IsEncounter { get; set; }
            // If the source has swapped bosses, indicating certain commands should be removed
            public bool IsSwapped { get; set; }
            // TODO dupe: For healthbar index calculations. Should be added to EnemyInfo. Figure out this logic
            public int MaxHealthbars { get; set; }
        }

        // Mainly a config for dupe-only rewriting. It has way too much stuff in it.
        public class Dupe
        {
            // Type of special edit to make, which is only applicable when dupes exist
            // "rewrite" duplicates all entity commands (default in Sekiro for non-chr non-locarg), for loc only
            // "replace" rewrites all entity ids based on the current DupeIndex, for loc only
            // "none" does nothing except apply when dupes exist (default in non-Sekiro), for loc or chr
            public string Type { get; set; }
            // Sets Type = "none" in Sekiro
            public bool NoRewrite { get; set; }
            // The existing enemy arg and the dupe arg position to add, when parameterized, or entity otherwise
            // Implies rewrite when not locarg, and the rewrite is limited to the entity
            public string Entity { get; set; }
            // Health bar name id whose dupe should have different indices and name
            public string HealthBar { get; set; }
            // Health bar name id parameter, to add at the end after Entity args
            public string HealthBarArg { get; set; }
            // Condition groups, when and/or logic mismatches. <source group> [<source 2 group> <combined group>].
            // By default uses 12, 13 as replacement groups (only used by butterfly end, miniboss start)
            public string Condition { get; set; }
            // Generators to add to dupe rewrite map, to duplicate for different enemies
            // This is meant for loc events, as chr events should use chrgen regions.
            // It does not edit the event itself, and its results must be used in rewrite/replace edits.
            public string Generator { get; set; }
            // When rewriting an event, add a quick delay between animations
            public int DelayAnimation { get; set; }
        }

        public class ItemTemplate
        {
            // item, any, loc, carp, default (ER)
            public string Type { get; set; }
            // Documentation on edits being made
            public string Comment { get; set; }
            // The event flag to potentially rewrite (space-separate list)
            public string EventFlag { get; set; }
            // The argument to edit, if an arg event. If a second is given, copies the second to the first.
            public string EventFlagArg { get; set; }
            // The item lot to use for the event flag replacement. TODO: implement
            public string ItemLot { get; set; }
            // A condition group to use for converting flag checks to item flags, when item template is used.
            // Right now extremely hacky, should use Replace instead. ItemCond should be positive and able to increment until 15.
            public int ItemCond { get; set; }
            // The shop slot qwc to use for the event flag replacement. May not be needed, since shop event flags are unambiguous
            // public string ShopQwc { get; set; }
            // An arg to blank out, in the case of alternate drops
            public string RemoveArg { get; set; }
            // An entity to use to identity item lots, mainly for making carp drops unique
            public string Entity { get; set; }
            // For ESD edits, the machine with the flag usage
            public string Machine { get; set; }
            // Commands to unconditionally remove.
            public string Remove { get; set; }
            // Args to replace
            public string Replace { get; set; }
            // Commands to add to an event, before randomizing it
            public List<EventAddCommand> Add { get; set; }
            // Check for doing nothing
            public bool IsDefault() => EventFlag == null && ItemLot == null && RemoveArg == null && Entity == null && Remove == null && Add == null;

            public ItemTemplate DeepCopy()
            {
                ItemTemplate o = (ItemTemplate)MemberwiseClone();
                if (o.Add != null) o.Add = o.Add.Select(x => x.DeepCopy()).ToList();
                return o;
            }
        }
    }
}
