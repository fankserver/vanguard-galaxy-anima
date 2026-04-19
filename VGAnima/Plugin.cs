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
