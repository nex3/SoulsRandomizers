using NCalc;
using SoulsFormats;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static RandomizerCommon.EventConfig;
using static SoulsFormats.ESD;
using static SoulsIds.AST;

namespace RandomizerCommon
{
    /// <summary>
    /// This class represents the structure of the `ezstate.yaml` data file, which encodes edits to
    /// make for dialogue state machines.
    /// </summary>
    public class EzstateConfig
    {
        /// <summary>
        /// A map from ESD identifiers to lists of <see cref="EzstateStateConfig"/>s.
        /// </summary>
        public Dictionary<string, List<ExistingState>> ExistingStates { get; set; } = new();

        /// <summary>
        /// Edits to make for a single Ezstate state.
        /// </summary>
        public class ExistingState
        {
            // TODO: Group and state numbers are auto-assigned by the compiler and thus subject to
            // high churn between patches. We should ideally find a better way to identify which
            // states we're targeting.
            /// <summary>
            /// The index of the group that contains this state. This uses the same indexing scheme
            /// as soulstruct.
            /// </summary>
            public uint Group { get; set; }

            /// <summary>
            /// The index of this state within its group.
            /// </summary>
            public uint State { get; set; }

            /// <summary>
            /// A boolean expression. This state is only applied if it returns true.
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
            /// Whether this state should be included given the selected randomizer options.
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

            /// <summary>
            /// Edits to apply to this state's procedural commands.
            /// </summary>
            public List<CommandEdit> EditCommands { get; set; } = new();

            /// <summary>
            /// Edits to apply to this state's conditional state transitions.
            /// </summary>
            public List<ConditionEdit> EditConditions { get; set; } = new();

            /// <summary>Performs the chosen edits on <paramref name="esd"/>.</summary>
            /// <param name="esd">The state machine file being edited.</param>
            /// <param name="doc">
            /// The ESD metadata used to decode information about <paramref name="cmd"/>.
            /// </param>
            /// <remarks>Throws an exception if no command matches <c>Match</c>.</remarks>
            public void Edit(ESD esd, ESDDocumentation doc)
            {
                if (!esd.StateGroups.TryGetValue(0x7FFFFFFF - Group, out var group))
                {
                    throw new Exception($"ESD {esd.Name} doesn't have a state group {Group}");
                }

                if (!group.TryGetValue(State, out var state))
                {
                    throw new Exception($"ESD {esd.Name} doesn't have a state {Group}.{State}");
                }

                foreach (var edit in EditCommands)
                {
                    edit.Edit(state, doc);
                }
                foreach (var edit in EditConditions)
                {
                    edit.Edit(state, doc);
                }
            }
        }

        /// <summary>A single edit to apply to a comamnd in an existing state.</summary>
        /// <remarks>
        /// An edit has two critical components: the <c>Matcher</c> which determines which command
        /// in the state to change, and the other properties which indicate which change(s) to
        /// make.
        /// </remarks>
        public class CommandEdit
        {
            /// <summary>The matcher which indicates which command to choose.</summary>
            /// <remarks>
            /// If this matches multiple commands, only the first will be modified. If it doesn't
            /// match any, it will throw an error.
            /// </remarks>
            public CommandMatcher Match { get; set; }

            /// <summary>Edits to make for arguments of the command, by index.</summary>
            public Dictionary<int, ExpressionModifier> Arguments { get; set; } = new();

            /// <summary>Removes matching commands entirely.</summary>
            public bool Remove { get; set; } = false;

            /// <summary>Performs the chosen edit on <paramref name="cmd"/>.</summary>
            /// <param name="cmd">The command being edited.</param>
            /// <param name="doc">
            /// The ESD metadata used to decode information about <paramref name="cmd"/>.
            /// </param>
            /// <remarks>Throws an exception if no command matches <c>Match</c>.</remarks>
            public void Edit(State state, ESDDocumentation doc)
            {
                var editTypes = 0;
                if (Arguments.Count > 0) editTypes++;
                if (Remove) editTypes++;
                if (editTypes > 1)
                {
                    throw new Exception("Each CommandEdit may only contain one edit");
                }

                for (var i = 0; i < state.EntryCommands.Count; i++)
                {
                    var command = Command.Disassemble(state.EntryCommands[i], doc);
                    if (!Match.Match(command, doc)) continue;

                    if (Arguments.Count > 0)
                    {
                        foreach (var (index, edit) in Arguments)
                        {
                            command.Arguments[index] = edit.EditExpression(
                                command.Arguments[index],
                                doc,
                                $"argument {index}"
                            );
                        }
                        state.EntryCommands[i] = command.Assemble(doc);
                        return;
                    }
                    else if (Remove)
                    {
                        state.EntryCommands.RemoveAt(i);
                        return;
                    }
                }
                throw new Exception(
                    $"Expected CommandEdit {Match} to match a command in:\n" + String.Join(
                        "\n",
                        state.EntryCommands
                            .Select(call => "* " + Command.Disassemble(call, doc).ToString())
                    )
                );
            }
        }

        /// <summary>
        /// A matcher which indicates which command to choose for a <c>CommandEdit</c>.
        /// </summary>
        /// <remarks>
        /// This can include multiple matcher conditions, in which case all of them must match in
        /// order for the matcher to match.
        /// </remarks>
        public class CommandMatcher
        {
            /// <summary>A matcher for the command name.</summary>
            public string Name { get; set; }

            /// <summary>Specific arguments to match. Null arguments match any value.</summary>
            /// <remarks>
            /// <para>A command with fewer args than listed here will not match, but a command
            /// with more will.</para>
            /// </remarks>
            public List<ArgumentMatcher> Arguments { get; set; } = new();

            /// <summary>A literal command to match.</summary>
            public string Command { get; set; }

            /// <summary>
            /// Parses a literal string as a command, which is matched exactly.
            /// </summary>
            public static explicit operator CommandMatcher(string command) =>
                new() { Command = command };

            public bool Match(Command command, ESDDocumentation doc) =>
                (Name == null || command.Name == Name) &&
                MatchArguments(command, doc) &&
                MatchCommand(command, doc);

            /// <returns>
            /// Whether <paramref name="command"/>'s arguments match <see cref="Arguments"/>.
            /// </returns>
            private bool MatchArguments(Command command, ESDDocumentation doc)
            {
                if (Arguments.Count > command.Arguments.Count) return false;
                for (var i = 0; i < Arguments.Count; i++)
                {
                    var argument = Arguments[i];
                    if (argument == null) return true;
                    if (!Arguments[i].Match(command.Arguments[i], doc)) return false;
                }
                return true;
            }

            /// <returns>
            /// Whether <paramref name="command"/> matches <see cref="Command"/>.
            /// </returns>
            private bool MatchCommand(Command command, ESDDocumentation doc) =>
                Command == null || ESDParser.ParseCommand(Command, doc) == command;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Name ?? "*");
                sb.Append('(');
                sb.Append(String.Join(
                    ", ",
                    Arguments
                        .Select(arg => arg is null ? "*" : arg.ToString())
                        .Concat(new List<string>() { "..." })
                ));
                sb.Append(')');
                return sb.ToString();
            }
        }

        /// <summary>
        /// An abstract base class for classes that modify an expression. It only contains
        /// attributes related to modification; selection is handled by the implementors.
        /// </summary>
        public class ExpressionModifier {
            /// <summary>
            /// Replaces each expression that exactly matches a map key with its corresponding
            /// value. If a key matches more than once, only the first one is replaced.
            /// </summary>
            public Dictionary<string, string> Replace { get; set; } = new();

            /// <summary>Replaces the entire condition with the given expression.</summary>
            public string Set { get; set; }

            /// <summary>Edits to make for arguments of a function expression, by index.</summary>
            public Dictionary<int, ExpressionModifier> Arguments { get; set; } = new();

            /// <summary>
            /// Adds the given expression as an additional condition that must match along with the
            /// existing expression in order for the condition to match.
            /// </summary>
            public string AndAlso { get; set; }

            /// <summary>
            /// Adds the given expression as an alternative condition that may match instead of the
            /// existing expression in order for the condition to match.
            /// </summary>
            public string OrElse { get; set; }

            /// <summary>Performs the chosen edit on <paramref name="expr"/>.</summary>
            /// <param name="expr">The expression being edited.</param>
            /// <param name="doc">
            /// The ESD metadata used to decode information about <paramref name="expr"/>.
            /// </param>
            /// <param name="matchString">
            /// A string that describes the matched expression. Used for error reporting.
            /// </param>
            /// <returns>
            /// The modified expression. This may be the original, or it may be a replacement that
            /// should be used in its place.
            /// </returns>
            public Expr EditExpression(Expr expr, ESDDocumentation doc, string matchString)
            {
                var editTypes = 0;
                if (Replace.Count > 0) editTypes++;
                if (Set != null) editTypes++;
                if (Arguments.Count > 0) editTypes++;
                if (AndAlso != null) editTypes++;
                if (OrElse != null) editTypes++;
                if (editTypes > 1)
                {
                    throw new Exception("Each ExpressionModifier may only contain one edit");
                }

                if (Replace.Count > 0)
                {
                    foreach (var (matcherStr, replacementStr) in Replace)
                    {
                        var matcher = ESDParser.ParseExpression(matcherStr, doc);
                        var replacement = ESDParser.ParseExpression(replacementStr, doc);
                        var found = false;
                        expr.Visit(AstVisitor.Pre(sub =>
                        {
                            if (!found && sub == matcher)
                            {
                                found = true;
                                return replacement;
                            }
                            else
                            {
                                return null;
                            }
                        }));

                        if (!found)
                        {
                            throw new Exception(
                                $"No ESD subexpression `{matcherStr}` found in {matchString}"
                            );
                        }
                    }
                }
                else if (Set != null)
                {
                    return ESDParser.ParseExpression(Set, doc);
                }
                else if (Arguments.Count > 0)
                {
                    if (expr is not FunctionCall call)
                    {
                        throw new Exception(
                            $"Expected ESD `{expr}` found in {matchString} to be a function call"
                        );
                    }

                    foreach (var (i, edit) in Arguments)
                    {
                        call.Args[i] = edit.EditExpression(call.Args[i], doc, $"argument {i}");
                    }
                }
                else if (AndAlso != null)
                {
                    return new BinaryExpr()
                    {
                        Op = "&&",
                        Lhs = expr,
                        Rhs = ESDParser.ParseExpression(AndAlso, doc),
                    };
                }
                else if (OrElse != null)
                {
                    return new BinaryExpr()
                    {
                        Op = "||",
                        Lhs = expr,
                        Rhs = ESDParser.ParseExpression(OrElse, doc),
                    };
                }

                return expr;
            }
        }

        /// <summary>
        /// A single edit to apply to a conditional state transition from an existing state.
        /// </summary>
        /// <remarks>
        /// An edit has two critical components: the <c>Matcher</c> which determines which
        /// condition in the state to change, and the other properties which indicate which
        /// change(s) to make.
        /// </remarks>
        public class ConditionEdit : ExpressionModifier
        {
            /// <summary>The matcher which indicates which condition to choose.</summary>
            /// <remarks>
            /// If this matches multiple conditions, only the first will be modified. If it doesn't
            /// match any, it will throw an error.
            /// </remarks>
            public ConditionMatcher Match { get; set; }

            /// <summary>Performs the chosen edit on <paramref name="cond"/>.</summary>
            /// <param name="cond">The command being edited.</param>
            /// <param name="doc">
            /// The ESD metadata used to decode information about <paramref name="cond"/>.
            /// </param>
            /// <remarks>Throws an exception if no instruction matches <c>Match</c>.</remarks>
            public void Edit(State state, ESDDocumentation doc)
            {
                for (var i = 0; i < state.Conditions.Count; i++)
                {
                    var condition = state.Conditions[i];
                    if (!Match.Match(condition, i, doc)) continue;

                    var expr = DisassembleExpression(condition.Evaluator, doc);
                    condition.Evaluator = AssembleExpression(
                        EditExpression(expr, doc, Match.ToString()),
                        doc
                    );
                }
            }
        }

        /// <summary>
        /// A union type for ways to match a condition that can be passed to
        /// <see cref="ConditionEdit.Match"/>.
        /// </summary>
        /// <remarks>
        /// This can include multiple matcher conditions, in which case all of them must match in
        /// order for the matcher to match.
        /// </remarks>
        public class ConditionMatcher
        {
            /// <summary>The index of the condition in the current state's conditions.</summary>
            public int? Index { get; set; }

            /// <summary>The ID of the state that this condition transitions to.</summary>
            public long? Target { get; set; }

            /// <summary>The literal expression to match against.</summary>
            public string Expression { get; set; }

            /// <summary>
            /// Parses a literal string as a condition, which is matched exactly.
            /// </summary>
            public static explicit operator ConditionMatcher(string expression) =>
                new() { Expression = expression };

            public bool Match(Condition condition, int index, ESDDocumentation doc)
            {
                return (Index == null || index == Index) &&
                    (Target == null || condition.TargetState == Target) &&
                    MatchExpression(condition, doc);
            }

            /// <returns>
            /// Whether <paramref name="condition"/>'s evaluator matches <see cref="Expression"/>.
            /// </returns>
            private bool MatchExpression(Condition condition, ESDDocumentation doc) =>
                Expression == null ||
                ESDParser.ParseExpression(Expression, doc) ==
                    DisassembleExpression(condition.Evaluator, doc);

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append("Condition");
                if (Index != null) sb.Append($" #{Index}");
                if (Target != null) sb.Append($" ->{Target}");
                return sb.ToString();
            }
        }

        /// <summary>
        /// A union type for ESD expression matchers that match a single argument.
        /// </summary>
        public record ArgumentMatcher
        {
            /// <summary>A literal number passed as an argument.</summary>
            public record Expression(string Expr) : ArgumentMatcher()
            {
                public override string ToString() => Expr.ToString();
            }

            /// <summary>Allows any argument in this position.</summary>
            public record Anything() : ArgumentMatcher()
            {
                public override string ToString() => "*";
            }

            public static explicit operator ArgumentMatcher(string arg) =>
                arg == null ? new Anything() : new Expression(arg);

            private ArgumentMatcher() { }

            public bool Match(Expr expr, ESDDocumentation doc) =>
                this switch
                {
                    Expression arg => ESDParser.ParseExpression(arg.Expr, doc) == expr,
                    Anything => true,
                    _ => throw new Exception("Unknown ArgumentMatcher type"),
                };
        }
    }
}
