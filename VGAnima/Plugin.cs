using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Galaxy.POI.Station;
using VGAnima.Cache;
using VGAnima.Config;
using VGAnima.Missions;
using VGAnima.Patches;
using VGAnima.Pitch;
using VGAnima.Tts;

namespace VGAnima;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Vanguard Galaxy Anima";
    public const string PluginVersion = "0.1.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal AnimaConfig Cfg { get; private set; } = null!;
    internal IMissionSource MissionSource { get; private set; } = null!;
    internal IPitchProvider PitchProvider { get; private set; } = null!;
    internal VgttsBridge Vgtts { get; private set; } = null!;
    internal ConversionRegistry<BarPatron, ConversionRecord> Registry { get; private set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Cfg = new AnimaConfig(Config);

        var missionTypes = Cfg.MissionTypes.Value
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        MissionSource = new VanillaMissionSource(missionTypes);

        PitchProvider = new StaticPitchProvider();  // v0.2 swaps to LlmPitchProvider
        Vgtts = new VgttsBridge();
        Registry = new ConversionRegistry<BarPatron, ConversionRecord>();

        Log.LogInfo($"[vganima] VGTTS detected: {(Vgtts.IsAvailable ? "yes" : "no")}");
        Log.LogInfo($"[vganima] MissionTypes: [{string.Join(", ", missionTypes)}]  " +
                    $"Chance: {Cfg.MissionChance.Value}  Backend: {Cfg.LlmBackend.Value}");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(BarPatronPatches));
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
