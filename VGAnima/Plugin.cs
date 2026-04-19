using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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

    // These are wired in Task 13 (Plugin.Awake composition); declared here
    // so patch classes referencing them compile. All null until Awake runs.
    internal VGAnima.Config.AnimaConfig Cfg { get; set; } = null!;
    internal VGAnima.Missions.IMissionSource MissionSource { get; set; } = null!;
    internal VGAnima.Pitch.IPitchProvider PitchProvider { get; set; } = null!;
    internal VGAnima.Tts.VgttsBridge Vgtts { get; set; } = null!;
    internal VGAnima.Cache.ConversionRegistry<Source.Galaxy.POI.Station.BarPatron, VGAnima.Cache.ConversionRecord> Registry { get; set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded (stub)");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
