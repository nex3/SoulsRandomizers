This is my (@nex3's) best-effort attempt to document the internals of the
randomizer, particularly those parts that I expect will be most important for
anyone else looking to work on it or to help add Archipelago support for new
games. The bulk of it was originally written by @thefifthmatt, and I largely
only understand it through code archaeology.

* [Data Files](#data-files)
  * [`annotations.yaml`](#annotations-yaml)
  * [`itemevents.yaml`](#itemevents-yaml)
    * [Regions](#regions)
    * [Conditional Events](#conditional-events)
    * [`NewEvents`](#newevents)
    * [`ExistingEvents`](#existingevents)
      * [`Match`](#match)
      * [`MatchLength`](#matchlength)
      * [`Repeat`](#repeat)
      * [`Set`](#set)
      * [`Remove`](#remove)
      * [`Replace`](#replace)
      * [`AddBefore` and `AddAfter`](#addbefore-and-addafter)
      * [`Condition`](#condition)
    * [`Initialize`](#initialize)
  * [`events.yaml`](#events-yaml)

## Data Files

The YAML files in the `dist*/Base` directories are manually maintained. They
contain metadata used by the randomizer to track the structure of the game and
determine how to randomize things. They generally have the same structure across
different games.

### `annotations.yaml`

Only Matt fully understands the structure of this file, but we can say with
confidence that the bulk of it is the `Slots` list. This lists every mapped item
location in the game, including many of those that aren't randomized in
practice. The `Key` is a condensed representation of *how* the player reaches
this location; it often includes event IDs that trigger the item, as well as
shop IDs for items available in shops. I don't understand the exact structure.

The `DebugText`, contrary to the name, isn't just used for debugging, at least
in the Archipelago extensions. We use it to determine which items exist in which
locations in the vanilla game, which we use in turn to associate Archipelago
location IDs with static randomizer location keys. Specifically, we look at all
the locations with a given item and region (`Area` in `annotations.yaml`) for
both Archipelago and the static randomizer. The Archipelago locations are
ordered to match the static randomizer's, so we go down the list and assume they
match. In any case where they can't match, we have to add an explicit key to the
Archipelago item metadata.

The `Text` is a human-friendly description of the location in question. The
static randomizer uses this in its spoiler dumps; Archipelago uses it to
as the source of its [detailed location descriptions].

[detailed location descriptions]: https://nex-3.com/ds3/locations/#detailed-location-descriptions

The `Tags` are almost entirely unused by Archipelago. The only exception is
`norandom`, which will take precedence over Archipelago-assigned locations and
thus must be removed if we want to randomize something the static randomizer
doesn't.

### `itemevents.yaml`

This file describes and documents edits to make to the vanilla event code for a
game. It file was added entirely for the Archipelago fork, although it contains
some events that were previously injected in *ad hoc* ways. It edits the game's
[EMEVD code], so be sure to read up on that to be able to understand what's
going on.

[EMEVD code]: https://soulsmodding.com/doku.php?id=tutorial%3Alearning-how-to-use-emevd

#### Regions

All subsections of this file are organized by region, following the naming
conventions of the game's files. Most sections are named `m##_00_00_00`, which
corresponds to a particular in-game map; for example, `m40_00_00_00` is Firelink
Shrine in DS3, and events in it will only run when the player is in Firelink.
There are two special sections corresponding to the two special event files:

* `common`: This file contains events that always run, no matter where the
  player is. It's used for common game logic, as well as some quest stuff that's
  unavoidably cross-region, like anything that triggers based on how many bosses
  the player defeats.

* `common_func`: This file contains exclusively events that take arguments. It
  serves as a library of "event functions" that can be called from any map. For
  example, it has the basic infrastructure for triggering enemy aggro, handling
  treasure pickups, and handling map entities like doors and lifts. It's the
  only event file that doesn't initialize any events of its own.

#### Conditional Events

Not every event edit should happen for every run of the randomizer. In fact,
this is the most important reason we keep edits in a separate data file rather
than just keeping our own edited copies of the game's EMEVDs. We want to be able
to choose which edits to make based on each player's configuration. (The other
reason is that that makes it more complicated to handle patches that change the
EMEVD.)

To make those choices, each type of event edit has an `If` field. This takes a
boolean expression using the standard `&&`/`||`/`!` syntax used by most
programming languages which determines when that edit is applied. You can use
any name here that you can use with `RandomizerOptions`, as well as the special
boolean `archipelago` which is true when running in Archipelago mode and false
otherwise.

#### `NewEvents`

This group contains edits that add entirely new events. This can add both normal
events, which are automatically initialized, and event functions, which are not.

Each new event has a set of `Commands`, EMEVD commands that form the body of the
event. These are run in order when the event is invoked; for normal events, this
means they're run as soon as the player loads into the region.

Event functions have two additional fields:

* `Name`: This is a name for the event, which is used to initialize it, either
  from the [`Initialize` block](#initialize) or from C# code for more complex
  cases.
* `Arguments`: This is a list of argument names (which can then be referred to
  in the function's `Commands`).
  
For example:

```yaml
NewEvents:
  # Make Firelink Shrine greyed out without having the Coiled Sword, in combination with setting
  # ActionButtonParam in PermutationWriter.
  m40_00_00_00: # Firelink
  - Comment: |
    Rest: Restart
    Commands:
    - Set Event Flag (14005108, ON)
    - IF Player Has/Doesn't Have Item (MAIN, ItemType.GOODS, 2137, OwnershipState.Owns)
    - Set Event Flag (14005108, OFF)
```

#### `ExistingEvents`

This group contains edits that apply to events that already exist in the vanilla
game. Each event is identified by its `ID`, and in almost all cases these
contain an `Edits` field which lists changes to make to the event. Each edit
represents a single edit to make to an event. It has two general components,
each of which encompasses several fields:

* The matcher, which encompasses the `Match`, `MatchLength`, and `Repeat`
  fields. This describes which instruction(s) to apply the edit to.

* The change, which encompasses all the other fields. This describes what change
  to make to the matched instructions. An edit may have multiple edits, but
  there's no guarantee what order they're applied in.

For example:

```yaml
ExistingEvents:
  common_func:
  # Disable the event that hides treasures outside of NG+
  - ID: 20005523
    If: ngplusrings
    Edits: [AddBefore: ["GOTO Unconditionally (0)"]]
```

This may also define a `Name` field, which allows existing event functions to be
initialized in the same was as `NewEvent`s.

##### `Match`

The match can be thought of as a pattern that either does or does not match any
given instruction. Matches match the *first* matching instruction unless
`Repeat` or `MatchLength` is set.

A match can have three different forms:

* It can be a EMEVD command represented as a literal string, in which case it
  will match exactly that command invocation. For example:

  ```yaml
  - Match: "SetCharacterInvincibility(X0_4, Disabled)"
    Remove: true
  ```

* It can be be a map with `Name` and `Arguments` fields. The `Name` is the EMEVD
  command name to match, and the `Arguments` represent a *subset* of arguments
  that must match. Null arguments or those past the end of the argument list are
  allowed to be anything. Arguments themselves can either be literal numbers or
  strings representing EMEVD constants. For example:

  ```yaml
  - Match: {Name: IfEventFlag, Arguments: [null, null, null, 73500105]}
    Set: [{Param: targetEventFlagId, Value: 50006211}]
  ```

* It can be an object with `Init` and `Arguments` fields. The `Init` field
  matches an event initializer (which are almost exclusively used in event 0 or
  50 to initialize other events). The `Init.Callee` field is mandatory, and
  indicates the event being initialized; the `Init.Index` field refers to the
  index in the case of multiple initializations of the same (non-common-func)
  event. The `Arguments` field is the same as above. For example:

  ```yaml
  - Match: {Init: {Callee: 20006002}, Arguments: [4000720]}
    Remove: true
  ```

##### `MatchLength`

By default, a match only covers the single instruction it matches. The
`MatchLength` field may be used to have it cover multiple instructions at once,
starting from the matched instruction. This can only be used with edits like
`Remove` and `Replace` that make sense to cover multiple commands. For example:

```yaml
- Match: {Init: {Callee: 9120}, Arguments: [74000303]}
  MatchLength: 3
  Remove: true
```

##### `Repeat`

By default, each edit only applies to the first match in the event. If `Repeat`
is set, it repeats that many times, always moving past the last line it changed.
This may be set to a positive integer, or to `-1` which indicates that it
repeats over and over until it runs out of matches. For example:

```yaml
- Match: {Init: {Callee: 20006001}, Arguments: [null, 1356]}
  Repeat: -1
  Remove: true
```

##### `Set`

This contains a list of maps that each changes a single parameter's value. The
`Param` field determines which parameter to replace, and it can contain the
following fields:

* `Index`: This matches a parameter based on its 0-based index in the command's
  parameter list.

* `Name`: This matches a parameter based on its name in the event definitions.

* `InitArg`: For initializers only, this matches a parameter based on the index
  of the argument that will be passed to the initializer, skipping the index and
  event ID parameters.

The `Value` field determines what value to set the argument to, and it can be an
integer or an EMEVD constant name. For example:

```yaml
- Match: IfEventFlag(OR_02, ON, TargetEventFlagType.EventFlag, 73100363)
  Set: [{Param: targetEventFlagId, Value: 73100365}]
```

##### `Remove`

Set this to `true` to remove all matching commands entirely. For example:

```yaml
- Match: {Init: {Callee: 20006001}, Arguments: [3300700]}
  Remove: true
```

##### `Replace`

This replaces all matched commands with an entirely new list of commands. For
example:

```yaml
- Match: IfConditionGroup(AND_01, PASS, OR_01)
  Replace: ["IfEventFlag(AND_01, ON, TargetEventFlagType.EventFlag, 50006200)"]
```

##### `AddBefore` and `AddAfter`

These add new commands before or after the matched commands, respectively. For
example:

```yaml
- Match: "EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventIDSlotNumber, 0)"
  AddAfter: ["SetEventFlag(70000900, ON)"]
```

##### `Condition`

This is a utility to replace the condition in a conditional command (typically
of the form `If...()`) with a condition that will either always return true or
return false based on the value passed to this field. It's useful for making
certain branches of logic always happen or never happen. For example:

```yaml
- Match: IfEventFlag(AND_03, ON, TargetEventFlagType.EventFlag, 138)
  Condition: false
```

#### `Initialize`

This section adds initializers for existing events. Remember that you don't need
to manually initialize events defined in `NewEvents` unless they're event
functions. Each entry has the following fields:

* `ID`: The numeric ID of the event to initialize.

* `Name`: The name of the event to initialize, assigned via the `Name` field in
  `NewEvents` or `ExistingEvents`. Only one of this and `ID` needs to be set.

* `Arguments` any arguments to pass to the event's initializer.

For example:

```yaml
Initialize:
  m40_00_00_00: # Firelink
  - Name: countOrbeckSpells
    Arguments: [74000800, 74000801, 4, 4, 4]
```

## `events.yaml`

This file tracks events that are managed and modified by the enemy randomizer.
Only Matt really understands it, althought he `NewEvents` and `ExistingEvents`
fields are the same as for `itemevents.yaml`.
