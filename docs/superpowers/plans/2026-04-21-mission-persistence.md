# Mission Persistence — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make LLM-authored missions and their brokers survive game restarts. Each vanilla save file gets a pair-named sidecar holding the mission block + broker record; Harmony hooks on vanilla's save-write and save-load sync our in-memory registry with disk. No crashes when the sidecar is missing/corrupt.

**Architecture:** In-memory `PersistedBrokerRegistry` is source of truth during a session. A Harmony postfix on vanilla save-write flushes the registry to `<save>.vganima.json`. A Harmony prefix on vanilla save-load clears state, reads the sidecar, and registers mission factories in `StoryMission.allMissions` before vanilla deserializes the player's mission list. A Harmony patch on vanilla's mission lookup returns a placeholder `Mission` for any `vganima_llm_*` storyId the registry doesn't have — prevents `KeyNotFoundException` on missing/stale sidecars.

**Tech Stack:** Unchanged — BepInEx 5.4 + HarmonyX 2.10, netstandard2.1, Newtonsoft.Json (game-provided), xUnit cross-TFM. No new package references.

**Spec:** `docs/superpowers/specs/2026-04-21-mission-persistence-design.md`
**Prior plan:** `docs/superpowers/plans/2026-04-21-llm-mission-gen-v2-mission.md` (v2-mission epic, shipped)

---

## File layout

| File | Change |
|---|---|
| `docs/vanilla-reference.md` | **Modify** — append T1 scout findings (save/load API, mission lookup, resolution events) |
| `VGAnima/Persistence/PersistedEntry.cs` | **Create** — record types for sidecar entries |
| `VGAnima/Persistence/SidecarSchema.cs` | **Create** — top-level `{version, entries}` record |
| `VGAnima/Persistence/SidecarPathResolver.cs` | **Create** — derive sidecar / quarantine path from vanilla save path |
| `VGAnima/Persistence/SidecarIO.cs` | **Create** — atomic read/write + version check + quarantine |
| `VGAnima/Persistence/PersistedBrokerRegistry.cs` | **Create** — in-memory CRUD + secondary seed index |
| `VGAnima/Persistence/OrphanPurger.cs` | **Create** — given registry + vanilla state, compute purge set |
| `VGAnima/Persistence/ISaveContext.cs` | **Create** — abstraction over "current active save path" |
| `VGAnima/Missions/PlaceholderMission.cs` | **Create** — archivable Mission returned on registry miss |
| `VGAnima/Missions/MissionFactoryFromJson.cs` | **Modify** — rebuild-from-block factory path for load hook |
| `VGAnima/Missions/LlmMissionAssigner.cs` | **Modify** — push entry to registry on creation |
| `VGAnima/Patches/SaveWritePatch.cs` | **Create** — Harmony postfix, flushes registry to sidecar |
| `VGAnima/Patches/SaveLoadPatch.cs` | **Create** — Harmony prefix, reads sidecar + registers factories |
| `VGAnima/Patches/MissionLookupPatch.cs` | **Create** — Harmony patch, safety net for missing entries |
| `VGAnima/Patches/MissionLifecyclePatches.cs` | **Create** — Harmony on accept / complete / fail / archive → registry updates |
| `VGAnima/Patches/BarRefreshPatches.cs` | **Modify** — pin on registry entry (not just active mission); rewrite RegistryRehydratePatches to use registry |
| `VGAnima/Cache/ConversionRecord.cs` | **Modify** — add display name, isMale, stationId fields so rehydration has complete restore data |
| `VGAnima/Plugin.cs` | **Modify** — wire registry singleton, install new patches, run startup sweep |
| `VGAnima.Tests/Persistence/SidecarPathResolverTests.cs` | **Create** |
| `VGAnima.Tests/Persistence/SidecarIOTests.cs` | **Create** |
| `VGAnima.Tests/Persistence/PersistedBrokerRegistryTests.cs` | **Create** |
| `VGAnima.Tests/Persistence/OrphanPurgerTests.cs` | **Create** |
| `VGAnima.Tests/Missions/PlaceholderMissionTests.cs` | **Create** |

**Implementation deviations from the layout above:**
- `VGAnima/Persistence/ISaveContext.cs` not created; the "current active save path" abstraction was folded into static fields `SaveLoadPatch.LastKnownSavePath` / `SaveWritePatch.LastKnownSavePath` (read by `Plugin.OnAppQuitting`). One less indirection, same semantics.
- `VGAnima/Persistence/VGAnimaSidecarSerializationBinder.cs` added (not in the original layout). Locks down `TypeNameHandling.Auto` via an allowlist — raised by code review during T2 and delivered in T4.
- `VGAnima/Persistence/IClock.cs` + `GameClock.cs` added (not in the original layout but listed in Task 10 steps). Injectable time abstraction for tests.
- `VGAnima/Persistence/DeadSidecarSweeper.cs` added (listed in Task 15 steps, not in top table). Startup sweep of orphaned sidecars.
- `VGAnima.Tests/Missions/PlaceholderMissionTests.cs` added (not in table).
- T8 "MissionFactoryFromJson load-aware path" collapsed to no-op: scout finding #6 confirmed vanilla doesn't re-invoke the factory on load (missions restore from their own serialized fields). No code change needed.

---

## Task 1: Scout vanilla save/load API

**Purpose:** Identify the Harmony target methods and behavioral semantics required by every downstream task. Blocking prerequisite for T7, T8, T9, T11. Outputs documented in `docs/vanilla-reference.md` so future contributors find them.

**Files:**
- Modify: `docs/vanilla-reference.md` (append new section)

- [ ] **Step 1: Dispatch scout subagent**

Send a research prompt (not a coding task) via the Explore agent:
> Investigate the decompiled Vanguard Galaxy source accessible from `/home/fank/repo/vanguard-galaxy-anima` (paths under `Source.*`, potentially in `/tmp/decomp/` or the plugin's assembly references). Identify:
>
> 1. **Top-level save-write method:** class + method + signature. How is the target file path passed/exposed? Can a Harmony postfix safely write a sibling file after it returns? Is it called from multiple threads?
> 2. **Top-level save-load method:** class + method + signature. Where in the load orchestration does `Mission.FromJson` (or the mission-list deserializer) fire — is there a clean hook BEFORE that so a Harmony prefix can register factories first?
> 3. **Mission lookup method:** `StoryMission.Get(player, storyId)` or equivalent. Where is the `KeyNotFoundException` thrown for an unknown storyId? Can we prefix to intercept missing `vganima_llm_*` IDs and return a placeholder?
> 4. **Mission resolution events:** what method(s) fire when a mission completes successfully, fails, or is archived by the player? Ideal target for Harmony postfix to update our registry.
> 5. **Mission acceptance event:** what method fires when the player clicks "Accept" in a broker's dialogue? Ideal target for Harmony postfix.
> 6. **Deserialization semantics of `Mission`:** on load, does vanilla re-invoke the `StoryMission` factory delegate, or does it restore the `Mission` from its own serialized fields? This determines whether our factory body must be load-aware.
> 7. **`GamePlayer.missions` / archive access:** how do we enumerate the player's active and archived missions at load time (needed for orphan purge)?
>
> For each finding: class path, method signature, 3-10 line excerpt, and one-sentence behavioral note. Under 800 words total.

- [ ] **Step 2: Review scout output**

Check that all 7 items have concrete answers. If any are "unclear" or "not found", re-dispatch a focused follow-up scout before proceeding. Open questions at this stage will cascade into vague tasks.

- [ ] **Step 3: Append findings to `docs/vanilla-reference.md`**

Add a new `## Save/Load/Mission-Lifecycle API` section at the end of the file, containing all 7 findings verbatim with their code excerpts. Format to match the existing conventions in the file.

- [ ] **Step 4: Commit**

```bash
git add docs/vanilla-reference.md
git commit -m "docs: scout findings for vanilla save/load/mission-lifecycle API"
```

---

## Task 2: Persistence data model

**Files:**
- Create: `VGAnima/Persistence/PersistedEntry.cs`
- Create: `VGAnima/Persistence/SidecarSchema.cs`
- Test: `VGAnima.Tests/Persistence/SidecarSchemaRoundtripTests.cs` (new)

**Note:** All records use Newtonsoft.Json attributes (matches existing `LlmContext` pattern in `VGAnima/Llm/`).

- [ ] **Step 1: Write failing roundtrip test**

Create `VGAnima.Tests/Persistence/SidecarSchemaRoundtripTests.cs`:

```csharp
using Newtonsoft.Json;
using VGAnima.Persistence;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarSchemaRoundtripTests
{
    [Fact]
    public void Serializes_AndRoundtripsEntryShape()
    {
        var schema = new SidecarSchema(
            Version: 1,
            Entries: new[]
            {
                new PersistedEntry(
                    StoryId: "vganima_llm_station-42_broker-abc_nonce-xyz",
                    State: "offered",
                    MissionBlock: BuildMinimalBlock(),
                    Broker: new PersistedBroker(
                        Seed: "vganima-broker-abc-0",
                        StationId: "station-42",
                        DisplayName: "Shawn Jenkins",
                        IsMale: true,
                        Story: BuildMinimalStory()),
                    Timestamps: new PersistedTimestamps(
                        CreatedGameSeconds: 18420.5,
                        CreatedRealUtc: "2026-04-21T10:15:30Z",
                        LastSeenGameSeconds: 19800.0,
                        LastSeenRealUtc: "2026-04-21T10:38:00Z")),
            });

        var json = JsonConvert.SerializeObject(schema);
        var parsed = JsonConvert.DeserializeObject<SidecarSchema>(json)!;

        Assert.Equal(1, parsed.Version);
        Assert.Single(parsed.Entries);
        var entry = parsed.Entries[0];
        Assert.Equal("vganima_llm_station-42_broker-abc_nonce-xyz", entry.StoryId);
        Assert.Equal("offered", entry.State);
        Assert.Equal("vganima-broker-abc-0", entry.Broker.Seed);
        Assert.Equal("Shawn Jenkins", entry.Broker.DisplayName);
        Assert.True(entry.Broker.IsMale);
        Assert.Equal(18420.5, entry.Timestamps.CreatedGameSeconds);
        Assert.Equal("2026-04-21T10:15:30Z", entry.Timestamps.CreatedRealUtc);
    }

    [Fact]
    public void Serializes_TopLevelKeysUseSnakeCasePerSchema()
    {
        var schema = new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>());
        var json = JsonConvert.SerializeObject(schema);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"entries\":[]", json);
    }

    private static LlmMissionBlock BuildMinimalBlock() =>
        // existing LlmMissionBlock factory — match what MissionBlockValidatorTests does
        throw new System.NotImplementedException("inline a minimal block — see VGAnima.Tests/Llm/MissionBlockValidatorTests.cs for a template");

    private static LlmStory BuildMinimalStory() =>
        throw new System.NotImplementedException("inline a minimal story — see existing tests");
}
```

**Note to implementer:** Replace the two `BuildMinimal*` throws with actual fixtures by copying the shape used in `VGAnima.Tests/Llm/MissionBlockValidatorTests.cs` and `VGAnima.Tests/Llm/ResponseValidatorTests.cs`.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarSchemaRoundtripTests" 2>&1 | tail -20
```
Expected: compile error ("type or namespace `PersistedEntry` could not be found").

- [ ] **Step 3: Implement `PersistedEntry.cs`**

```csharp
using Newtonsoft.Json;
using VGAnima.Llm;

namespace VGAnima.Persistence;

internal sealed record PersistedEntry(
    [property: JsonProperty("storyId")]      string StoryId,
    [property: JsonProperty("state")]        string State,
    [property: JsonProperty("missionBlock")] LlmMissionBlock MissionBlock,
    [property: JsonProperty("broker")]       PersistedBroker Broker,
    [property: JsonProperty("timestamps")]   PersistedTimestamps Timestamps);

internal sealed record PersistedBroker(
    [property: JsonProperty("seed")]        string Seed,
    [property: JsonProperty("stationId")]   string StationId,
    [property: JsonProperty("displayName")] string DisplayName,
    [property: JsonProperty("isMale")]      bool IsMale,
    [property: JsonProperty("story")]       LlmStory Story);

internal sealed record PersistedTimestamps(
    [property: JsonProperty("createdGameSeconds")]  double CreatedGameSeconds,
    [property: JsonProperty("createdRealUtc")]      string CreatedRealUtc,
    [property: JsonProperty("lastSeenGameSeconds")] double LastSeenGameSeconds,
    [property: JsonProperty("lastSeenRealUtc")]     string LastSeenRealUtc);

internal static class PersistedEntryStates
{
    public const string Offered  = "offered";
    public const string Accepted = "accepted";
}
```

- [ ] **Step 4: Implement `SidecarSchema.cs`**

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
/// Version 1 is the only shape the current build understands; unknown
/// versions are quarantined by <see cref="SidecarIO"/>.</summary>
internal sealed record SidecarSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("entries")] IReadOnlyList<PersistedEntry> Entries)
{
    public const int CurrentVersion = 1;
}
```

- [ ] **Step 5: Run tests to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarSchemaRoundtripTests" 2>&1 | tail -10
```
Expected: PASS 2/2.

- [ ] **Step 6: Commit**

```bash
git add VGAnima/Persistence/PersistedEntry.cs VGAnima/Persistence/SidecarSchema.cs VGAnima.Tests/Persistence/SidecarSchemaRoundtripTests.cs
git commit -m "feat(persistence): data model for sidecar entries"
```

---

## Task 3: SidecarPathResolver

**Files:**
- Create: `VGAnima/Persistence/SidecarPathResolver.cs`
- Test: `VGAnima.Tests/Persistence/SidecarPathResolverTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarPathResolverTests
{
    [Fact]
    public void From_AppendsVganimaJsonSuffix()
    {
        var result = SidecarPathResolver.From("/home/user/saves/slot0.sav");
        Assert.Equal("/home/user/saves/slot0.sav.vganima.json", result);
    }

    [Fact]
    public void IsSidecar_ReturnsTrueForSidecarPaths()
    {
        Assert.True(SidecarPathResolver.IsSidecar("/x/y.sav.vganima.json"));
        Assert.False(SidecarPathResolver.IsSidecar("/x/y.sav"));
        Assert.False(SidecarPathResolver.IsSidecar("/x/y.vganima.corrupt.20260421120000.json"));
    }

    [Fact]
    public void BaseSavePathFrom_StripsVganimaSuffix()
    {
        Assert.Equal("/x/y.sav", SidecarPathResolver.BaseSavePathFrom("/x/y.sav.vganima.json"));
    }

    [Fact]
    public void QuarantineName_AppendsTimestampInUtc()
    {
        var when = new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc);
        var result = SidecarPathResolver.QuarantineName("/x/y.sav.vganima.json", when);
        Assert.Equal("/x/y.sav.vganima.corrupt.20260421120000.json", result);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarPathResolverTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
using System;

namespace VGAnima.Persistence;

/// <summary>Pure-function helpers for deriving sidecar paths from vanilla
/// save paths. No I/O. No state.</summary>
internal static class SidecarPathResolver
{
    private const string Suffix = ".vganima.json";

    public static string From(string vanillaSavePath) => vanillaSavePath + Suffix;

    public static bool IsSidecar(string path) =>
        path.EndsWith(Suffix, StringComparison.Ordinal)
        && !path.Contains(".vganima.corrupt.");

    public static string BaseSavePathFrom(string sidecarPath) =>
        sidecarPath.EndsWith(Suffix, StringComparison.Ordinal)
            ? sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length)
            : sidecarPath;

    public static string QuarantineName(string sidecarPath, DateTime utcNow)
    {
        var stamp = utcNow.ToString("yyyyMMddHHmmss");
        var withoutSuffix = sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length);
        return $"{withoutSuffix}.vganima.corrupt.{stamp}.json";
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarPathResolverTests" 2>&1 | tail -10
```
Expected: PASS 4/4.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Persistence/SidecarPathResolver.cs VGAnima.Tests/Persistence/SidecarPathResolverTests.cs
git commit -m "feat(persistence): sidecar path resolver + quarantine naming"
```

---

## Task 4: SidecarIO (atomic read/write + version + quarantine)

**Files:**
- Create: `VGAnima/Persistence/SidecarIO.cs`
- Test: `VGAnima.Tests/Persistence/SidecarIOTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using System.IO;
using Newtonsoft.Json;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarIOTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vganima-test-{Guid.NewGuid()}");

    public SidecarIOTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Read_MissingFile_ReturnsMissingStatus()
    {
        var io = new SidecarIO(() => DateTime.UtcNow);
        var result = io.Read(Path.Combine(_tempDir, "no-such.vganima.json"));
        Assert.Equal(SidecarReadStatus.MissingFile, result.Status);
        Assert.Null(result.Schema);
    }

    [Fact]
    public void Read_CorruptedJson_QuarantinesFileAndReturnsCorruptedStatus()
    {
        var path = Path.Combine(_tempDir, "bad.vganima.json");
        File.WriteAllText(path, "{ this is not valid json");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Corrupted, result.Status);
        Assert.Null(result.Schema);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_tempDir, "bad.vganima.corrupt.20260421120000.json")));
    }

    [Fact]
    public void Read_UnsupportedVersion_QuarantinesFileAndReturnsUnsupportedStatus()
    {
        var path = Path.Combine(_tempDir, "futuristic.vganima.json");
        File.WriteAllText(path, "{\"version\":99,\"entries\":[]}");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.UnsupportedVersion, result.Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_ThenRead_RoundtripsSchema()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        var schema = new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>());

        io.Write(path, schema);
        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Loaded, result.Status);
        Assert.Equal(1, result.Schema!.Version);
        Assert.Empty(result.Schema.Entries);
    }

    [Fact]
    public void Write_IsAtomic_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        io.Write(path, new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>()));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        File.WriteAllText(path, "previous contents");

        io.Write(path, new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>()));

        var contents = File.ReadAllText(path);
        Assert.Contains("\"version\":1", contents);
        Assert.DoesNotContain("previous", contents);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarIOTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement `SidecarIO.cs`**

```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace VGAnima.Persistence;

internal enum SidecarReadStatus { Loaded, MissingFile, Corrupted, UnsupportedVersion }

internal sealed record SidecarReadResult(
    SidecarReadStatus Status,
    SidecarSchema? Schema,
    string? QuarantinedTo);

/// <summary>Reads and writes the sidecar JSON file. Writes are atomic
/// (tmp + rename); reads quarantine corrupt or future-version files so
/// the game can proceed with an empty registry.</summary>
internal sealed class SidecarIO
{
    private readonly Func<DateTime> _utcNow;

    public SidecarIO(Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
    }

    public SidecarReadResult Read(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
            return new SidecarReadResult(SidecarReadStatus.MissingFile, null, null);

        string raw;
        try { raw = File.ReadAllText(sidecarPath); }
        catch (IOException) { return new SidecarReadResult(SidecarReadStatus.MissingFile, null, null); }

        SidecarSchema? schema;
        try { schema = JsonConvert.DeserializeObject<SidecarSchema>(raw); }
        catch (JsonException) { return Quarantine(sidecarPath, SidecarReadStatus.Corrupted); }

        if (schema is null) return Quarantine(sidecarPath, SidecarReadStatus.Corrupted);
        if (schema.Version != SidecarSchema.CurrentVersion)
            return Quarantine(sidecarPath, SidecarReadStatus.UnsupportedVersion);

        return new SidecarReadResult(SidecarReadStatus.Loaded, schema, null);
    }

    public void Write(string sidecarPath, SidecarSchema schema)
    {
        var tmp = sidecarPath + ".tmp";
        var json = JsonConvert.SerializeObject(schema);
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
        File.Move(tmp, sidecarPath);
    }

    private SidecarReadResult Quarantine(string sidecarPath, SidecarReadStatus status)
    {
        var quarantinePath = SidecarPathResolver.QuarantineName(sidecarPath, _utcNow());
        File.Move(sidecarPath, quarantinePath);
        return new SidecarReadResult(status, null, quarantinePath);
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~SidecarIOTests" 2>&1 | tail -10
```
Expected: PASS 6/6.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Persistence/SidecarIO.cs VGAnima.Tests/Persistence/SidecarIOTests.cs
git commit -m "feat(persistence): atomic sidecar I/O with version + quarantine handling"
```

---

## Task 5: PersistedBrokerRegistry

**Files:**
- Create: `VGAnima/Persistence/PersistedBrokerRegistry.cs`
- Test: `VGAnima.Tests/Persistence/PersistedBrokerRegistryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class PersistedBrokerRegistryTests
{
    [Fact]
    public void Add_ThenGet_ReturnsEntry()
    {
        var reg = new PersistedBrokerRegistry();
        var entry = MakeEntry("story-1", "seed-1");

        reg.Add(entry);

        Assert.Same(entry, reg.Get("story-1"));
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        var reg = new PersistedBrokerRegistry();
        Assert.Null(reg.Get("nope"));
    }

    [Fact]
    public void FindBySeed_ReturnsEntryWithMatchingSeed()
    {
        var reg = new PersistedBrokerRegistry();
        var entry = MakeEntry("story-1", "seed-abc");
        reg.Add(entry);

        Assert.Same(entry, reg.FindBySeed("seed-abc"));
        Assert.Null(reg.FindBySeed("seed-other"));
    }

    [Fact]
    public void Remove_DropsEntryFromBothIndexes()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));

        reg.Remove("story-1");

        Assert.Null(reg.Get("story-1"));
        Assert.Null(reg.FindBySeed("seed-1"));
    }

    [Fact]
    public void Clear_WipesAllEntries()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));
        reg.Add(MakeEntry("story-2", "seed-2"));

        reg.Clear();

        Assert.Empty(reg.All());
    }

    [Fact]
    public void MarkAccepted_TransitionsStateFromOfferedToAccepted()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1", state: PersistedEntryStates.Offered));

        reg.MarkAccepted("story-1");

        Assert.Equal(PersistedEntryStates.Accepted, reg.Get("story-1")!.State);
    }

    [Fact]
    public void BumpLastSeen_UpdatesTimestamps()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));

        reg.BumpLastSeen("story-1", gameSeconds: 99999.0, realUtc: "2026-04-22T00:00:00Z");

        var e = reg.Get("story-1")!;
        Assert.Equal(99999.0, e.Timestamps.LastSeenGameSeconds);
        Assert.Equal("2026-04-22T00:00:00Z", e.Timestamps.LastSeenRealUtc);
    }

    [Fact]
    public void LoadFrom_ReplacesAllEntries()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("old-story", "old-seed"));

        reg.LoadFrom(new[] { MakeEntry("new-story", "new-seed") });

        Assert.Null(reg.Get("old-story"));
        Assert.NotNull(reg.Get("new-story"));
    }

    [Fact]
    public void Add_SameStoryId_Overwrites()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-old"));
        reg.Add(MakeEntry("story-1", "seed-new"));

        Assert.Equal("seed-new", reg.Get("story-1")!.Broker.Seed);
        Assert.Null(reg.FindBySeed("seed-old"));
    }

    private static PersistedEntry MakeEntry(string storyId, string seed, string state = PersistedEntryStates.Offered)
    {
        return new PersistedEntry(
            StoryId: storyId,
            State: state,
            MissionBlock: null!,   // shape-only test; registry doesn't read block
            Broker: new PersistedBroker(
                Seed: seed,
                StationId: "station-1",
                DisplayName: "Test Broker",
                IsMale: true,
                Story: null!),     // same rationale
            Timestamps: new PersistedTimestamps(0, "2026-04-21T00:00:00Z", 0, "2026-04-21T00:00:00Z"));
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~PersistedBrokerRegistryTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Persistence;

/// <summary>In-memory authoritative registry of persisted broker entries
/// during a session. Mutated by broker creation, mission lifecycle hooks,
/// and bar refresh (lastSeen bumps). Flushed to disk by
/// <see cref="SidecarIO"/> on vanilla save-write. Replaced wholesale by
/// sidecar contents on vanilla save-load.</summary>
internal sealed class PersistedBrokerRegistry
{
    private readonly Dictionary<string, PersistedEntry> _byStoryId = new();
    private readonly Dictionary<string, string>         _storyIdBySeed = new();

    public void Clear()
    {
        _byStoryId.Clear();
        _storyIdBySeed.Clear();
    }

    public void LoadFrom(IEnumerable<PersistedEntry> entries)
    {
        Clear();
        foreach (var e in entries) Add(e);
    }

    public void Add(PersistedEntry entry)
    {
        if (_byStoryId.TryGetValue(entry.StoryId, out var existing))
            _storyIdBySeed.Remove(existing.Broker.Seed);

        _byStoryId[entry.StoryId] = entry;
        _storyIdBySeed[entry.Broker.Seed] = entry.StoryId;
    }

    public void Remove(string storyId)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId.Remove(storyId);
        _storyIdBySeed.Remove(entry.Broker.Seed);
    }

    public PersistedEntry? Get(string storyId) =>
        _byStoryId.TryGetValue(storyId, out var e) ? e : null;

    public PersistedEntry? FindBySeed(string seed) =>
        _storyIdBySeed.TryGetValue(seed, out var storyId) ? _byStoryId[storyId] : null;

    public IReadOnlyCollection<PersistedEntry> All() => _byStoryId.Values;

    public void MarkAccepted(string storyId)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId[storyId] = entry with { State = PersistedEntryStates.Accepted };
    }

    public void BumpLastSeen(string storyId, double gameSeconds, string realUtc)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId[storyId] = entry with
        {
            Timestamps = entry.Timestamps with
            {
                LastSeenGameSeconds = gameSeconds,
                LastSeenRealUtc     = realUtc,
            },
        };
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~PersistedBrokerRegistryTests" 2>&1 | tail -10
```
Expected: PASS 9/9.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Persistence/PersistedBrokerRegistry.cs VGAnima.Tests/Persistence/PersistedBrokerRegistryTests.cs
git commit -m "feat(persistence): in-memory broker registry with seed index"
```

---

## Task 6: OrphanPurger

**Files:**
- Create: `VGAnima/Persistence/OrphanPurger.cs`
- Test: `VGAnima.Tests/Persistence/OrphanPurgerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Generic;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class OrphanPurgerTests
{
    [Fact]
    public void Purge_RemovesAcceptedEntriesMissingFromMissionLists()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("keep-story", "keep-seed"));
        reg.Add(Accepted("orphan-story", "orphan-seed"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: new[] { "keep-story" },
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>());

        Assert.NotNull(reg.Get("keep-story"));
        Assert.Null(reg.Get("orphan-story"));
    }

    [Fact]
    public void Purge_KeepsAcceptedEntriesInArchive()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("archived-story", "archived-seed"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: new[] { "archived-story" },
            knownPatronSeeds: System.Array.Empty<string>());

        Assert.NotNull(reg.Get("archived-story"));
    }

    [Fact]
    public void Purge_RemovesOfferedEntriesWithUnknownPatronSeeds()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Offered("alive-story", "alive-seed"));
        reg.Add(Offered("dead-story",  "dead-seed"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: new[] { "alive-seed" });

        Assert.NotNull(reg.Get("alive-story"));
        Assert.Null(reg.Get("dead-story"));
    }

    [Fact]
    public void Purge_ReportsDroppedIds()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("orphan-a", "seed-a"));
        reg.Add(Offered("orphan-b", "seed-b"));

        var dropped = OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>());

        Assert.Equal(2, dropped.Count);
        Assert.Contains("orphan-a", dropped);
        Assert.Contains("orphan-b", dropped);
    }

    private static PersistedEntry Accepted(string storyId, string seed) => Make(storyId, seed, PersistedEntryStates.Accepted);
    private static PersistedEntry Offered(string storyId, string seed)  => Make(storyId, seed, PersistedEntryStates.Offered);

    private static PersistedEntry Make(string storyId, string seed, string state) =>
        new(storyId, state, null!,
            new PersistedBroker(seed, "station-1", "Name", true, null!),
            new PersistedTimestamps(0, "2026-04-21T00:00:00Z", 0, "2026-04-21T00:00:00Z"));
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~OrphanPurgerTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Persistence;

/// <summary>Drops stale entries at load time. Two rules:
/// <list type="bullet">
///   <item>Accepted entries whose storyId isn't in vanilla's active or
///     archived mission lists — the mission is gone from the player's
///     timeline; keeping the entry is a memory leak.</item>
///   <item>Offered entries whose broker seed doesn't match any current
///     patron — the broker is gone; the cached inference has nowhere
///     to live.</item>
/// </list></summary>
internal static class OrphanPurger
{
    public static IReadOnlyCollection<string> Purge(
        PersistedBrokerRegistry registry,
        IReadOnlyCollection<string> activeStoryIds,
        IReadOnlyCollection<string> archivedStoryIds,
        IReadOnlyCollection<string> knownPatronSeeds)
    {
        var activeSet   = new HashSet<string>(activeStoryIds);
        var archivedSet = new HashSet<string>(archivedStoryIds);
        var seedSet     = new HashSet<string>(knownPatronSeeds);

        var toDrop = new List<string>();
        foreach (var entry in registry.All())
        {
            var isOrphan = entry.State switch
            {
                PersistedEntryStates.Accepted => !activeSet.Contains(entry.StoryId) && !archivedSet.Contains(entry.StoryId),
                PersistedEntryStates.Offered  => !seedSet.Contains(entry.Broker.Seed),
                _ => true,
            };
            if (isOrphan) toDrop.Add(entry.StoryId);
        }

        foreach (var storyId in toDrop) registry.Remove(storyId);
        return toDrop;
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~OrphanPurgerTests" 2>&1 | tail -10
```
Expected: PASS 4/4.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Persistence/OrphanPurger.cs VGAnima.Tests/Persistence/OrphanPurgerTests.cs
git commit -m "feat(persistence): load-time orphan purge for stale entries"
```

---

## Task 7: PlaceholderMission + MissionLookupPatch

**Prerequisite:** T1 scout output — specifically, the mission-lookup method signature (§3 of scout) and `Mission` constructor requirements.

**Files:**
- Create: `VGAnima/Missions/PlaceholderMission.cs`
- Create: `VGAnima/Patches/MissionLookupPatch.cs`
- Test: `VGAnima.Tests/Missions/PlaceholderMissionTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using VGAnima.Missions;
using Xunit;

namespace VGAnima.Tests.Missions;

public class PlaceholderMissionTests
{
    [Fact]
    public void Build_ReturnsMissionWithStoryIdSet()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing");
        Assert.Equal("vganima_llm_missing", mission.storyId);
    }

    [Fact]
    public void Build_MarksMissionAsImmediatelyArchivable()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing");
        // Contract from spec §7: must be archivable without vanilla crashing.
        // Trivially-complete mission with zero steps means vanilla's tick
        // treats it as done on first evaluation.
        Assert.Empty(mission.steps);
    }

    [Fact]
    public void Build_UsesPlaceholderNameAndDescription()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing");
        Assert.NotNull(mission.name);
        Assert.NotEmpty(mission.name);
        Assert.NotNull(mission.description);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~PlaceholderMissionTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement `PlaceholderMission.cs`**

Reference the `Mission` class shape from the existing v2-mission plan's "Decomp-confirmed ground truth" section:
> `Mission` public fields: `name`, `description`, `completionText`, `sourcePoi`, `turnIn`, `sourceFaction`, `iconName`, `storyId`, `dynamicLevel`, `trackedOnHud`, `canBeIdled`, `difficulty`; `steps` and `rewards` are `List<T>`.

```csharp
using Source.MissionSystem;

namespace VGAnima.Missions;

/// <summary>Returned by <see cref="VGAnima.Patches.MissionLookupPatch"/>
/// when vanilla asks for a <c>vganima_llm_*</c> storyId that our registry
/// doesn't have — typically because the sidecar is missing, corrupt, or
/// was quarantined. The mission has zero steps, so vanilla's tick treats
/// it as trivially complete and archives it on first evaluation. Logs
/// a warning on construction identifying the missing storyId.</summary>
internal static class PlaceholderMission
{
    public static Mission Build(string storyId)
    {
        BepInEx.Logging.Logger.CreateLogSource("VGAnima")
            .LogWarning($"No sidecar entry for storyId `{storyId}` — returning placeholder (auto-archive)");

        return new Mission
        {
            storyId        = storyId,
            name           = "Archived VGAnima Mission",
            description    = "This LLM-authored mission's data is no longer available.",
            completionText = "Archived.",
            dynamicLevel   = false,
            trackedOnHud   = false,
            canBeIdled     = true,
        };
    }
}
```

**Note to implementer:** Placeholder construction may need adjustment based on T1 scout findings about `Mission`'s constructor + required fields. If `Mission` has validation that rejects zero-step missions at construction, adjust the contract (e.g., single auto-completing TriggerObjective step) to satisfy it.

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~PlaceholderMissionTests" 2>&1 | tail -10
```
Expected: PASS 3/3.

- [ ] **Step 5: Implement `MissionLookupPatch.cs`**

This is a Harmony patch that can't be unit-tested directly (requires Harmony + Unity game context). The patch target comes from T1 scout findings item #3 (mission lookup method). Update the patch attribute and signature to match.

**Template** (update method name/target based on scout):

```csharp
using HarmonyLib;
using Source.MissionSystem;
using VGAnima.Missions;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Safety net for vanilla save references that our sidecar
/// doesn't cover. When vanilla calls <c>StoryMission.Get(player, storyId)</c>
/// (or whatever the scout identified) with a <c>vganima_llm_*</c> ID that
/// isn't in the registry, return a placeholder Mission instead of letting
/// vanilla throw <see cref="System.Collections.Generic.KeyNotFoundException"/>.</summary>
[HarmonyPatch(typeof(StoryMission), nameof(StoryMission.Get))]
internal static class MissionLookupPatch
{
    // Injected by Plugin.Awake via reflection on the static field,
    // or via a static `Install(PersistedBrokerRegistry)` call.
    public static PersistedBrokerRegistry? Registry;

    [HarmonyPrefix]
    static bool Prefix(GamePlayer player, string storyId, ref Mission __result)
    {
        // Only intercept VGAnima IDs; let vanilla handle everything else.
        if (!storyId.StartsWith("vganima_llm_", System.StringComparison.Ordinal))
            return true;  // continue to original

        // If our registry has it, the factory is already registered; let vanilla proceed.
        if (Registry?.Get(storyId) is not null) return true;

        // Registry miss — return placeholder, skip original.
        __result = PlaceholderMission.Build(storyId);
        return false;  // skip original (don't throw)
    }
}
```

**Note to implementer:** Verify with T1 findings that (a) `StoryMission.Get` is the correct target, (b) its signature matches, and (c) the static `Registry` injection pattern matches how `Plugin.cs` already wires similar singletons.

- [ ] **Step 6: Build and confirm patch compiles**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -10
```
Expected: build succeeds (0 errors). Any Harmony attribute / method signature mismatch will surface here.

- [ ] **Step 7: Commit**

```bash
git add VGAnima/Missions/PlaceholderMission.cs VGAnima/Patches/MissionLookupPatch.cs VGAnima.Tests/Missions/PlaceholderMissionTests.cs
git commit -m "feat(persistence): placeholder mission + lookup safety net"
```

---

## Task 8: MissionFactoryFromJson — rebuild-from-block load path

**Prerequisite:** T1 scout finding #6 (Mission deserialization semantics).

**Rationale:** `LlmMissionAssigner` currently registers a factory `_ => mission` that returns a pre-built Mission instance. That works within a session but can't rebuild after a restart. For load-hook rehydration (T10), we need a factory that rebuilds from the persisted `LlmMissionBlock` on demand.

**Files:**
- Modify: `VGAnima/Missions/MissionFactoryFromJson.cs`
- Add: `VGAnima.Tests/Missions/MissionFactoryFromJsonRehydrateTests.cs` (new tests for load-aware build)

- [ ] **Step 1: Understand current Build signature**

```bash
grep -n "public static.*Build" /home/fank/repo/vanguard-galaxy-anima/VGAnima/Missions/MissionFactoryFromJson.cs
```

- [ ] **Step 2: Write failing test for load-aware build**

The load-aware build path differs from the fresh-author path ONLY if T1 scout finding #6 reports "vanilla re-invokes factory on load". In that case, the test below asserts POI spawning is skipped when `isRehydrating: true`. If scout reports "vanilla restores from serialized fields", this task collapses to "no change needed" — mark all steps complete with a commit-less skip.

Add `VGAnima.Tests/Missions/MissionFactoryFromJsonRehydrateTests.cs`:

```csharp
using VGAnima.Missions;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Missions;

public class MissionFactoryFromJsonRehydrateTests
{
    // Only populated if scout finding #6 requires load-aware factory behavior.
    // Otherwise this file is deleted at step 4.

    [Fact]
    public void Build_WithIsRehydratingTrue_DoesNotSpawnNewPois()
    {
        // Shape depends on scout: typically asserts the returned Mission's
        // KillEnemies objective points at a pre-existing POI from the
        // live SystemMapData rather than a newly-spawned one.
        // See T1 scout finding #6 for the specific assertion shape.
        Assert.True(true);  // placeholder — implementer replaces based on scout
    }
}
```

- [ ] **Step 3: If scout says factory is NOT re-called on load**

Delete the test file and skip to step 5 commit (with no actual modification).

- [ ] **Step 4: If scout says factory IS re-called on load, modify `Build`**

Add an `isRehydrating: bool = false` parameter. When true, the `BuildClearPoi` helper must NOT call `station.system.AddCombat(...)` — instead it must look up the existing POI in the system's POI list by the storyId-derived reference that vanilla persisted. Update the test body to match the actual restoration pattern. Run tests to confirm both fresh-build and rehydrate paths pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(persistence): load-aware MissionFactoryFromJson rebuild path"
```

(Or commit empty if scout confirmed no change needed — use `--allow-empty` with a note-only message.)

---

## Task 9: ConversionRecord extension for full rehydration

**Rationale:** Current `ConversionRecord` stores `WarmedLines`, `Station`, `StoryId`, `LlmStory`. Spec §4 schema requires also persisting `DisplayName` and `IsMale` for broker rehydration. `StationId` (string) rather than the live `Station` ref is what gets serialized.

**Files:**
- Modify: `VGAnima/Cache/ConversionRecord.cs`
- Modify: `VGAnima/Patches/BarRefreshPatches.cs` (every ConversionRecord constructor call site)

- [ ] **Step 1: Find all ConversionRecord construction sites**

```bash
grep -rn "new ConversionRecord" /home/fank/repo/vanguard-galaxy-anima/VGAnima/
```

- [ ] **Step 2: Extend ConversionRecord with display name + isMale**

```csharp
// Add two fields to the record; keep backward-compat by adding constructor parameters
// at the END with sensible defaults (empty string + false).
internal sealed class ConversionRecord
{
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public string StoryId { get; }
    public LlmStory? LlmStory { get; }
    public string DisplayName { get; }   // NEW
    public bool IsMale { get; }          // NEW

    public ConversionRecord(
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station,
        string storyId,
        LlmStory? llmStory,
        string displayName,
        bool isMale)
    {
        WarmedLines = warmedLines;
        Station = station;
        StoryId = storyId;
        LlmStory = llmStory;
        DisplayName = displayName;
        IsMale = isMale;
    }
}
```

- [ ] **Step 3: Update all construction sites from step 1**

At every `new ConversionRecord(...)` call site, pass the broker's display name and gender flag (the values already exist in scope because `BarRefreshPatches` builds the LLM `BrokerInfo` with these fields).

- [ ] **Step 4: Build + existing tests pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj 2>&1 | tail -15
```
Expected: all existing tests still pass (264+ currently). No new tests — this is a data-carrier extension.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Cache/ConversionRecord.cs VGAnima/Patches/BarRefreshPatches.cs
git commit -m "refactor(persistence): carry displayName + isMale on ConversionRecord"
```

---

## Task 10: LlmMissionAssigner — push to registry on creation

**Files:**
- Modify: `VGAnima/Missions/LlmMissionAssigner.cs`
- Test: `VGAnima.Tests/Missions/LlmMissionAssignerRegistryTests.cs` (new)

- [ ] **Step 1: Write failing test**

```csharp
using System;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Missions;

public class LlmMissionAssignerRegistryTests
{
    [Fact]
    public void Assign_PushesEntryToRegistryInOfferedState()
    {
        var reg = new PersistedBrokerRegistry();
        var clock = new FakeClock(
            gameSeconds: 18420.5,
            realUtc: new DateTime(2026, 04, 21, 10, 15, 30, DateTimeKind.Utc));
        var assigner = new LlmMissionAssigner(
            register: _ => { },
            registry: reg,
            clock: clock);

        // Block + broker-info shape: see MissionBlockValidatorTests / BrokerInfo usages
        // Fixture patterns: copy `Block()` helper from
        // VGAnima.Tests/Missions/LlmMissionAssignerTests.cs:21
        // and `Story()` helper from VGAnima.Tests/Pitch/LlmPitchProviderTests.cs:13
        // — both already exist in the test project and match the real record shapes.
        var storyId = assigner.Assign(
            block: FixtureBlock(),
            missionLevel: 1,
            brokerStation: null,
            brokerSeed: "vganima-broker-abc-0",
            brokerDisplayName: "Shawn Jenkins",
            brokerIsMale: true,
            brokerStory: FixtureStory());

        var entry = reg.Get(storyId);
        Assert.NotNull(entry);
        Assert.Equal(PersistedEntryStates.Offered, entry!.State);
        Assert.Equal("vganima-broker-abc-0", entry.Broker.Seed);
        Assert.Equal("Shawn Jenkins", entry.Broker.DisplayName);
        Assert.True(entry.Broker.IsMale);
        Assert.Equal(18420.5, entry.Timestamps.CreatedGameSeconds);
        Assert.Equal("2026-04-21T10:15:30Z", entry.Timestamps.CreatedRealUtc);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(double gameSeconds, DateTime realUtc) { GameSeconds = gameSeconds; UtcNow = realUtc; }
        public double GameSeconds { get; }
        public DateTime UtcNow { get; }
    }
}
```

- [ ] **Step 2: Create `IClock` abstraction**

`VGAnima/Persistence/IClock.cs`:

```csharp
using System;

namespace VGAnima.Persistence;

internal interface IClock
{
    double GameSeconds { get; }   // vanilla elapsed game seconds
    DateTime UtcNow { get; }
}
```

Real impl (production) wires `Source.Galaxy` or whatever the scout finds for game-time; `DateTime.UtcNow` for real time. Add a `GameClock.cs` as well:

```csharp
using System;

namespace VGAnima.Persistence;

internal sealed class GameClock : IClock
{
    // Real impl: wire to vanilla's game-time accessor (see GameStateView.ElapsedSeconds).
    public double GameSeconds => /* T1 scout identifies the accessor */ 0.0;
    public DateTime UtcNow => DateTime.UtcNow;
}
```

- [ ] **Step 3: Run failing test**

Expected: compile error — `LlmMissionAssigner` ctor doesn't take registry/clock yet.

- [ ] **Step 4: Modify `LlmMissionAssigner`**

Add overloaded ctor that accepts `PersistedBrokerRegistry` + `IClock`. In `Assign`, after minting `storyId`, build a `PersistedEntry` and push it into the registry. The `Assign` signature grows three new params: `brokerDisplayName`, `brokerIsMale`, `brokerStory`.

- [ ] **Step 5: Update BarRefreshPatches to pass new params to `Assign`**

`BarRefreshPatches` already holds display name and gender — wire them through.

- [ ] **Step 6: Run all tests**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj 2>&1 | tail -10
```
Expected: all pass, including the new registry test.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(persistence): LlmMissionAssigner pushes entry to registry on creation"
```

---

## Task 11: Mission lifecycle patches (accept / resolve → registry updates)

**Prerequisite:** T1 scout findings #4 (resolution events) and #5 (acceptance event).

**Files:**
- Create: `VGAnima/Patches/MissionLifecyclePatches.cs`

**Note:** These are Harmony patches, not unit-testable. The file contains one class per event (keeps each patch focused). Scout output determines exact method targets.

- [ ] **Step 1: Identify targets from scout**

Confirm from T1 output:
- Acceptance: e.g., `Mission.Accept(GamePlayer)` or similar
- Completion: e.g., `Mission.Complete(GamePlayer)`
- Failure: e.g., `Mission.Fail()`
- Archive: e.g., `GamePlayer.ArchiveMission(Mission)`

- [ ] **Step 2: Implement patches**

```csharp
using HarmonyLib;
using Source.MissionSystem;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Wires vanilla mission lifecycle events into the persisted
/// broker registry. Acceptance transitions an entry from offered→accepted;
/// completion/failure/archive removes the entry entirely.
/// Targets confirmed in docs/vanilla-reference.md §Save/Load/Mission-Lifecycle API.</summary>
internal static class MissionLifecyclePatches
{
    public static PersistedBrokerRegistry? Registry;

    [HarmonyPatch(typeof(Mission), nameof(Mission.Accept))]   // <-- confirm from scout
    internal static class OnAcceptPatch
    {
        [HarmonyPostfix]
        static void Postfix(Mission __instance)
        {
            if (!__instance.storyId.StartsWith("vganima_llm_", System.StringComparison.Ordinal)) return;
            Registry?.MarkAccepted(__instance.storyId);
        }
    }

    [HarmonyPatch(typeof(Mission), nameof(Mission.Complete))]  // <-- confirm from scout
    internal static class OnCompletePatch
    {
        [HarmonyPostfix]
        static void Postfix(Mission __instance) => Remove(__instance);
    }

    [HarmonyPatch(typeof(Mission), nameof(Mission.Fail))]      // <-- confirm from scout
    internal static class OnFailPatch
    {
        [HarmonyPostfix]
        static void Postfix(Mission __instance) => Remove(__instance);
    }

    // Archive hook: scout may indicate GamePlayer.ArchiveMission rather than Mission.*;
    // adjust target and body accordingly.
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.ArchiveMission))]
    internal static class OnArchivePatch
    {
        [HarmonyPostfix]
        static void Postfix(Mission mission) => Remove(mission);
    }

    private static void Remove(Mission mission)
    {
        if (!mission.storyId.StartsWith("vganima_llm_", System.StringComparison.Ordinal)) return;
        Registry?.Remove(mission.storyId);
    }
}
```

**Note to implementer:** If any target method name differs from the template above, update the `[HarmonyPatch(...)]` attributes and parameter names. The patches must be no-ops for non-`vganima_llm_*` storyIds — defensive StartsWith guard is load-bearing.

- [ ] **Step 3: Build to confirm patches compile**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -10
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VGAnima/Patches/MissionLifecyclePatches.cs
git commit -m "feat(persistence): mission lifecycle patches update registry on accept/complete/fail/archive"
```

---

## Task 12: SaveWritePatch (postfix → flush registry to sidecar)

**Prerequisite:** T1 scout finding #1 (save-write method target).

**Files:**
- Create: `VGAnima/Patches/SaveWritePatch.cs`

- [ ] **Step 1: Implement patch**

Template (update `[HarmonyPatch(...)]` attribute to match scout):

```csharp
using System;
using HarmonyLib;
using BepInEx.Logging;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Harmony postfix on vanilla's save-write. Flushes the in-memory
/// broker registry to the sidecar paired with the save file that vanilla
/// just wrote. Postfix (not prefix) ensures the sidecar only lands on disk
/// if vanilla's own save succeeded — avoids orphaned sidecars referencing
/// saves that never got written. Atomic write (tmp + rename) via
/// <see cref="SidecarIO"/>.
/// Target method confirmed in docs/vanilla-reference.md §Save/Load/Mission-Lifecycle API.</summary>
[HarmonyPatch(/* scout-confirmed type */, /* scout-confirmed method */)]
internal static class SaveWritePatch
{
    public static PersistedBrokerRegistry? Registry;
    public static SidecarIO? Io;
    public static ManualLogSource? Log;

    [HarmonyPostfix]
    static void Postfix(/* args matching target — typically includes save path */)
    {
        if (Registry is null || Io is null) return;
        try
        {
            var savePath = /* extract from args per scout */ "";
            var sidecarPath = SidecarPathResolver.From(savePath);
            var schema = new SidecarSchema(
                Version: SidecarSchema.CurrentVersion,
                Entries: System.Linq.Enumerable.ToList(Registry.All()));
            Io.Write(sidecarPath, schema);
            Log?.LogInfo($"Flushed {schema.Entries.Count} entr{(schema.Entries.Count == 1 ? "y" : "ies")} to {sidecarPath}");
        }
        catch (Exception e)
        {
            Log?.LogError($"Sidecar flush failed: {e}");
            // Never throw — must not break vanilla save success.
        }
    }
}
```

**Note to implementer:** (a) `[HarmonyPatch(...)]` type+method from scout. (b) `Postfix` parameter signature depends on whether vanilla's save-write takes a path argument or exposes it through an instance field. Match the scout's finding exactly. (c) The try/catch is mandatory — a throwing postfix poisons vanilla's save success.

- [ ] **Step 2: Build to confirm**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -10
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/SaveWritePatch.cs
git commit -m "feat(persistence): save-write postfix flushes registry to sidecar"
```

---

## Task 13: SaveLoadPatch (prefix → read sidecar + register factories)

**Prerequisite:** T1 scout findings #2 (save-load method target), #6 (Mission deserialization semantics), #7 (player mission list access).

**Files:**
- Create: `VGAnima/Patches/SaveLoadPatch.cs`

- [ ] **Step 1: Implement patch**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using BepInEx.Logging;
using Source.MissionSystem;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Patches;

/// <summary>Harmony prefix on vanilla's save-load. Runs BEFORE vanilla
/// processes the mission list or invokes <c>Mission.FromJson</c>.
/// <list type="number">
///   <item>Clears in-memory registry + unregisters any VGAnima factories
///     from <c>StoryMission.allMissions</c> (prevents cross-slot leakage
///     when loading a different save mid-session).</item>
///   <item>Reads the paired sidecar. Missing / corrupt / unsupported-version
///     → empty registry (see <see cref="SidecarIO"/>).</item>
///   <item>Registers a rebuild-from-block factory for every loaded entry
///     in <c>StoryMission.allMissions</c>.</item>
/// </list>
/// Orphan purge runs later (after bar refresh populates patron seeds) —
/// see T14.
/// Target method confirmed in docs/vanilla-reference.md §Save/Load/Mission-Lifecycle API.</summary>
[HarmonyPatch(/* scout-confirmed type */, /* scout-confirmed method */)]
internal static class SaveLoadPatch
{
    public static PersistedBrokerRegistry? Registry;
    public static SidecarIO? Io;
    public static ManualLogSource? Log;
    public static readonly HashSet<string> RegisteredStoryIds = new();

    [HarmonyPrefix]
    static void Prefix(/* args matching target — typically includes save path */)
    {
        if (Registry is null || Io is null) return;

        // (1) Clear state from previous load/session. Vanilla's
        // `StoryMission.allMissions` is a static dictionary — likely no public
        // "unregister" exists. Use HarmonyX's AccessTools to grab the private
        // field and remove our IDs directly. This is documented in scout
        // finding #3 — if scout shows the field is public, drop the reflection.
        var allMissionsField = AccessTools.Field(typeof(StoryMissionRegistry), "allMissions");
        var allMissions = (System.Collections.IDictionary)allMissionsField.GetValue(null);
        foreach (var storyId in RegisteredStoryIds) allMissions.Remove(storyId);
        RegisteredStoryIds.Clear();
        Registry.Clear();

        // (2) Read sidecar.
        var savePath = /* extract from args per scout */ "";
        var sidecarPath = SidecarPathResolver.From(savePath);
        var result = Io.Read(sidecarPath);

        switch (result.Status)
        {
            case SidecarReadStatus.Loaded:
                Registry.LoadFrom(result.Schema!.Entries);
                Log?.LogInfo($"Loaded {result.Schema.Entries.Count} entr{(result.Schema.Entries.Count == 1 ? "y" : "ies")} from {sidecarPath}");
                break;
            case SidecarReadStatus.MissingFile:
                Log?.LogInfo($"No sidecar at {sidecarPath} — starting with empty registry");
                break;
            case SidecarReadStatus.Corrupted:
                Log?.LogWarning($"Sidecar corrupted; quarantined to {result.QuarantinedTo} — empty registry");
                break;
            case SidecarReadStatus.UnsupportedVersion:
                Log?.LogWarning($"Sidecar version too new; quarantined to {result.QuarantinedTo} — empty registry. Check for plugin update.");
                break;
        }

        // (3) Register rebuild factories.
        foreach (var entry in Registry.All())
        {
            var block = entry.MissionBlock;
            var storyId = entry.StoryId;
            var brokerSeed = entry.Broker.Seed;
            var storyMission = new StoryMissionRegistry(
                storyId,
                _ => MissionFactoryFromJson.Build(block, missionLevel: 1, brokerStation: null, brokerSeed: brokerSeed),
                available: null,
                pickupHint: "VGAnima Broker (rehydrated)");
            StoryMissionRegistry.Add(storyMission);
            RegisteredStoryIds.Add(storyId);
        }
    }
}
```

**Implementer notes:**
- `missionLevel: 1` in the factory body is a placeholder — actual value should come from the sidecar entry's originally-captured station level. Spec §4 schema currently doesn't persist this; **if the scout determines it's needed for reward scaling on rehydration, extend the schema via T15 before finalizing this task**.
- The `StoryMissionRegistry.Add` call may throw if the ID is already registered. Use try/catch and log, rather than letting vanilla crash.
- If T8 established `isRehydrating: true` is required, pass it here.

- [ ] **Step 2: Build to confirm**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -10
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/SaveLoadPatch.cs
git commit -m "feat(persistence): save-load prefix rehydrates registry + factories"
```

---

## Task 14: BarRefreshPatches — pin on registry entry + rewrite rehydration

**Files:**
- Modify: `VGAnima/Patches/BarRefreshPatches.cs`

- [ ] **Step 1: Locate current pinning logic**

```bash
grep -n "IsActive\|RegistryRehydrate\|RefreshPatrons_Prefix" /home/fank/repo/vanguard-galaxy-anima/VGAnima/Patches/BarRefreshPatches.cs
```

- [ ] **Step 2: Update `IsActive` (pinning predicate)**

Currently: `IsActive(record, playerView)` checks "storyId is not archived and has active mission".
New: `IsActive(record, playerView, registry)` also returns true when the entry exists in our registry (offered or accepted). This is the key change that makes unaccepted brokers survive rotation.

```csharp
private static bool IsActive(ConversionRecord record, IGamePlayerView playerView, PersistedBrokerRegistry registry)
{
    // Registry entry pins regardless of vanilla mission-list state.
    if (registry.Get(record.StoryId) is not null) return true;

    // Fallback: existing vanilla-mission-list check for defensive redundancy.
    return playerView.GetActive(record.StoryId) is not null && !playerView.IsArchived(record.StoryId);
}
```

- [ ] **Step 3: Rewrite `RegistryRehydratePatches` to pull from registry**

Currently: detects seed prefix, calls LLM with legacy fallback.
New: detects seed prefix, looks up entry in registry. If found, restore `ConversionRecord` from saved fields (no LLM call). If not found (registry miss on a VGAnima-seeded patron), fall back to plain patron — NO LLM call, NO legacy mission assignment.

```csharp
// Inside RegistryRehydratePatches.RefreshPatrons_Prefix:
foreach (var patron in patrons)
{
    if (!patron.salesman.seed.StartsWith("vganima-broker-", StringComparison.Ordinal)) continue;

    var entry = Registry.FindBySeed(patron.salesman.seed);
    if (entry is null)
    {
        Log.LogInfo($"Seed-prefixed patron {patron.salesman.seed} has no registry entry; leaving as plain patron");
        continue;
    }

    // Restore ConversionRecord from the entry's broker section.
    var record = new ConversionRecord(
        warmedLines: new List<(string Speaker, string Text)>(),
        station:     patron.GetStation(),  // actual accessor per existing code
        storyId:     entry.StoryId,
        llmStory:    entry.Broker.Story,
        displayName: entry.Broker.DisplayName,
        isMale:      entry.Broker.IsMale);

    ConversionRegistry.Associate(patron, record);
}
```

- [ ] **Step 4: Build + existing tests pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj 2>&1 | tail -10
```
Expected: 264+ existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Patches/BarRefreshPatches.cs
git commit -m "feat(persistence): bar pinning + rehydration pull from registry, no LLM fallback"
```

---

## Task 15: Startup dead-sidecar sweep + Plugin.cs integration

**Prerequisite:** T1 scout identifies the save directory path (or a method that returns it).

**Files:**
- Create: `VGAnima/Persistence/DeadSidecarSweeper.cs`
- Test: `VGAnima.Tests/Persistence/DeadSidecarSweeperTests.cs`
- Modify: `VGAnima/Plugin.cs`

- [ ] **Step 1: Write failing test for sweeper**

```csharp
using System;
using System.IO;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class DeadSidecarSweeperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vganima-sweep-{Guid.NewGuid()}");

    public DeadSidecarSweeperTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Sweep_DeletesSidecarsWithNoPairedSaveFile()
    {
        var orphan = Path.Combine(_tempDir, "deleted-save.sav.vganima.json");
        var paired = Path.Combine(_tempDir, "alive-save.sav.vganima.json");
        File.WriteAllText(orphan, "{}");
        File.WriteAllText(paired, "{}");
        File.WriteAllText(Path.Combine(_tempDir, "alive-save.sav"), "vanilla save");

        var deleted = DeadSidecarSweeper.Sweep(_tempDir);

        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(paired));
        Assert.Single(deleted);
        Assert.Contains(orphan, deleted);
    }

    [Fact]
    public void Sweep_IgnoresQuarantinedFiles()
    {
        var quarantined = Path.Combine(_tempDir, "slot0.sav.vganima.corrupt.20260421000000.json");
        File.WriteAllText(quarantined, "{}");

        var deleted = DeadSidecarSweeper.Sweep(_tempDir);

        Assert.True(File.Exists(quarantined));
        Assert.Empty(deleted);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~DeadSidecarSweeperTests" 2>&1 | tail -10
```
Expected: compile error.

- [ ] **Step 3: Implement sweeper**

```csharp
using System.Collections.Generic;
using System.IO;

namespace VGAnima.Persistence;

/// <summary>Startup-time cleanup of sidecars whose vanilla save file no
/// longer exists. Prevents accumulation when users delete saves outside
/// the game. Ignores quarantined files (they carry forensic value).</summary>
internal static class DeadSidecarSweeper
{
    public static IReadOnlyList<string> Sweep(string saveDirectory)
    {
        if (!Directory.Exists(saveDirectory)) return System.Array.Empty<string>();

        var deleted = new List<string>();
        foreach (var sidecar in Directory.EnumerateFiles(saveDirectory, "*.vganima.json"))
        {
            if (!SidecarPathResolver.IsSidecar(sidecar)) continue;  // skips .corrupt.*.json
            var baseSave = SidecarPathResolver.BaseSavePathFrom(sidecar);
            if (File.Exists(baseSave)) continue;

            File.Delete(sidecar);
            deleted.Add(sidecar);
        }
        return deleted;
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~DeadSidecarSweeperTests" 2>&1 | tail -10
```
Expected: PASS 2/2.

- [ ] **Step 5: Wire everything into `Plugin.cs`**

```csharp
// Inside Plugin.Awake, after existing Harmony patches:

// 1. Singleton registry + I/O.
var registry = new PersistedBrokerRegistry();
var io = new SidecarIO(() => System.DateTime.UtcNow);

// 2. Inject into Harmony patches.
SaveWritePatch.Registry    = registry;
SaveWritePatch.Io          = io;
SaveWritePatch.Log         = Logger;

SaveLoadPatch.Registry     = registry;
SaveLoadPatch.Io           = io;
SaveLoadPatch.Log          = Logger;

MissionLookupPatch.Registry     = registry;
MissionLifecyclePatches.Registry = registry;

// 3. Inject registry + clock into LlmMissionAssigner when Plugin constructs it.
// (Specific wiring depends on how Plugin.cs currently builds LlmMissionAssigner —
//  follow the existing pattern.)

// 4. Dead-sidecar sweep on startup.
var saveDir = /* T1 scout finding — path accessor */ "";
var swept = DeadSidecarSweeper.Sweep(saveDir);
if (swept.Count > 0) Logger.LogInfo($"Swept {swept.Count} dead sidecar(s)");

// 5. ApplicationQuit safety-net flush. Spec §5 requires flushing to the
// most-recently-active save path on plugin shutdown *only if* the session
// has a known active slot. SaveLoadPatch + SaveWritePatch both update a
// static LastKnownSavePath (add the field to both patches) when they fire;
// if still null on quit, nothing to flush.
UnityEngine.Application.quitting += () =>
{
    var path = SaveLoadPatch.LastKnownSavePath ?? SaveWritePatch.LastKnownSavePath;
    if (path is null) return;
    try
    {
        var sidecarPath = SidecarPathResolver.From(path);
        io.Write(sidecarPath, new SidecarSchema(
            Version: SidecarSchema.CurrentVersion,
            Entries: System.Linq.Enumerable.ToList(registry.All())));
    }
    catch (System.Exception e) { Logger.LogError($"Quit-time flush failed: {e}"); }
};
```

**Implementer note:** add `public static string? LastKnownSavePath;` to both `SaveLoadPatch` and `SaveWritePatch`, populated at the top of their respective hooks.

- [ ] **Step 6: Build + all tests pass**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -5
dotnet test /home/fank/repo/vanguard-galaxy-anima/VGAnima.Tests/VGAnima.Tests.csproj 2>&1 | tail -10
```
Expected: build succeeds, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add VGAnima/Persistence/DeadSidecarSweeper.cs VGAnima.Tests/Persistence/DeadSidecarSweeperTests.cs VGAnima/Plugin.cs
git commit -m "feat(persistence): dead-sidecar startup sweep + Plugin.cs wiring"
```

---

## Task 16: Orphan purge wiring into SaveLoadPatch

**Rationale:** T13 leaves orphan purge out (registry is populated but not pruned — stale entries would persist). This task connects `OrphanPurger.Purge(...)` to the load flow. Delayed to its own task because it needs the bar-refresh cycle to populate `knownPatronSeeds`.

**Files:**
- Modify: `VGAnima/Patches/SaveLoadPatch.cs`
- Modify: `VGAnima/Patches/BarRefreshPatches.cs` (trigger orphan purge after first post-load refresh)

- [ ] **Step 1: Add a one-shot post-load orphan-purge trigger**

Spec §8 says orphan purge runs inside the load hook, after registry is populated. In practice, `knownPatronSeeds` is only meaningful after vanilla's patron rehydration finishes — which happens during the first bar refresh. So we set a "purge pending" flag at load-time and run the actual purge during the first `BarRefreshPatches` after load.

In `SaveLoadPatch.cs`, add a static flag:

```csharp
public static bool OrphanPurgePending = true;
```

Set it to `true` at the end of `Prefix`.

- [ ] **Step 2: Run the purge during first post-load bar refresh**

In `BarRefreshPatches` (the `Postfix` that runs after all patrons are processed), add:

```csharp
if (SaveLoadPatch.OrphanPurgePending && Registry is not null)
{
    // IGamePlayerView exposes IsArchived(id) + GetActive(id) per-id only.
    // Iterate registry entries; classify each by querying vanilla.
    var activeIds   = new System.Collections.Generic.List<string>();
    var archivedIds = new System.Collections.Generic.List<string>();
    foreach (var entry in Registry.All())
    {
        if (playerView.GetActive(entry.StoryId) is not null) activeIds.Add(entry.StoryId);
        else if (playerView.IsArchived(entry.StoryId))       archivedIds.Add(entry.StoryId);
    }
    var seeds   = EnumerateCurrentStationBarPatronSeeds();
    var dropped = OrphanPurger.Purge(Registry, activeIds, archivedIds, seeds);
    if (dropped.Count > 0) Log.LogInfo($"Orphan-purged {dropped.Count} stale entr{(dropped.Count == 1 ? "y" : "ies")}");
    SaveLoadPatch.OrphanPurgePending = false;
}
```

`EnumerateCurrentStationBarPatronSeeds` returns seeds for patrons in the station the player is currently docked at (the `BarRefreshPatches` postfix has this context already — it just fired for a specific bar). This means **offered-state orphans for unvisited stations will not purge on this load** — they'll purge on the next load after the player revisits. Acceptable v1 behavior: disk doesn't balloon because each entry is small (~5-10 KB) and eventual cleanup is still bounded by revisit cadence.

- [ ] **Step 3: Build + all tests pass**

```bash
dotnet build /home/fank/repo/vanguard-galaxy-anima/VGAnima/VGAnima.csproj 2>&1 | tail -5
```

- [ ] **Step 4: Commit**

```bash
git add VGAnima/Patches/SaveLoadPatch.cs VGAnima/Patches/BarRefreshPatches.cs
git commit -m "feat(persistence): orphan purge on first post-load bar refresh"
```

---

## Task 17: Manual E2E verification

**Purpose:** Spec §10 outlines the live-game test matrix. This task runs each scenario in the real game and captures observations.

**Files:**
- Modify: `README.md` (append a "Persistence" section summarizing live-test outcomes + any behavioral caveats discovered)

- [ ] **Step 1: Test scenario — accept → save → quit → launch → load → verify**

Accept an LLM-authored mission from a broker. Save the game. Quit to main menu. Launch the game fresh (cold restart). Load the save. Expected: mission present in active list, broker still pinned at the same bar, reward dialog works at turn-in. Record any deviations.

- [ ] **Step 2: Test scenario — offer → close without accepting → relaunch → verify**

Enter a broker's dialogue, close without accepting. Save. Quit. Relaunch. Load. Return to the bar. Expected: same broker with same pitch present; no new LLM call fires on re-visit (check logs).

- [ ] **Step 3: Test scenario — multiple save slots isolated**

Slot A: accept broker A's mission. Save to slot A. Slot B: new save, accept broker B's mission. Save to slot B. Load slot A. Expected: only broker A's mission present, slot B's broker not visible. Switch to slot B: only B's content. Verify sidecar files on disk show separate contents.

- [ ] **Step 4: Test scenario — sidecar missing**

Accept a mission, save, quit. Manually delete the `.vganima.json` sidecar. Launch, load. Expected: game doesn't crash. Log shows "no sidecar" warning. Authored mission is auto-archived via placeholder. Player's active list has a brief "Archived VGAnima Mission" entry that drops.

- [ ] **Step 5: Test scenario — sidecar corrupted**

Accept a mission, save, quit. Manually corrupt the sidecar (e.g., truncate to half its length, or inject invalid JSON). Launch, load. Expected: file quarantined to `.vganima.corrupt.<timestamp>.json`, game continues, original authored mission placeholder-archives.

- [ ] **Step 6: Test scenario — mission complete → purge**

Accept a mission, complete it (win the objective, collect reward). Save. Inspect sidecar. Expected: entry removed.

- [ ] **Step 7: Update README**

Append or replace the persistence section with the actual current behavior, any caveats discovered during E2E, and the resolved known-limitations list (remove the old "v1.1 adds persistence" note).

- [ ] **Step 8: Commit**

```bash
git add README.md
git commit -m "docs: README reflects shipped mission persistence"
```

---

## Completion

When all 17 tasks pass their reviews, the `finishing-a-development-branch` skill takes over for merge/PR handling. Run the full test suite once more (`dotnet test`) to confirm the final state: 264 existing + ~30 new persistence tests all green, build clean, no TODOs introduced in committed code.
