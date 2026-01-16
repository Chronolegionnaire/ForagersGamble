using System;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches
{
    internal static class NameMaskingScope
    {
        [ThreadStatic] private static int _depth;
        public static bool IsActive => _depth > 0;

        public static void Enter() { _depth++; }
        public static void Exit()  { if (_depth > 0) _depth--; }
        public static IDisposable Push() => new Scope();

        private sealed class Scope : IDisposable
        {
            public Scope() { Enter(); }
            public void Dispose() => Exit();
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), "GetHeldItemName")]
    public static class Patch_CollectibleObject_GetHeldItemName
    {
        static void Postfix(CollectibleObject __instance, ItemStack itemStack, ref string __result, ICoreAPI ___api)
        {
            // 0. Guard rails / early exits
            if (NameMaskingScope.IsActive) return;

            var cfg = ModConfig.Instance.Main;
            if (cfg == null) return;
            if (itemStack == null || ___api?.World == null) return;

            var world = ___api.World;
            var clientWorld = world as IClientWorldAccessor;

            var agent = clientWorld?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            var code = Knowledge.ItemKey(itemStack);
            if (string.IsNullOrWhiteSpace(code)) return;
            if (!Knowledge.IsInUnknownUniverse(code)) return;
            if (Knowledge.IsKnown(agent, code)) return;
            if (Knowledge.IsLiquidContainer(code))
            {
                if (Knowledge.TryResolveUnknownLiquidName(
                        agent,
                        ___api,
                        __instance,
                        itemStack,
                        cfg.UnknownAll == true,
                        cfg.UnknownPlants,
                        cfg.UnknownMushrooms,
                        out var liquidKey))
                {
                    __result = Lang.Get(liquidKey);
                    return;
                }
            }
            if (Knowledge.TryResolveUnknownName(code, out var langKey))
            {
                __result = Lang.Get(langKey);
            }
        }
    }
}
