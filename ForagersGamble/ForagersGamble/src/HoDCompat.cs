using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ForagersGamble.Compat
{
    internal static class HodCompat
    {
        private const string ThirstBehaviorFullName   = "HydrateOrDiedrate.EntityBehaviorThirst";
        private const string HydrationManagerFullName = "HydrateOrDiedrate.HydrationManager";

        private static Type _hydrationManagerType;
        private static MethodInfo _miGetHydration;

        private static Type _thirstBehaviorType;
        private static MethodInfo _miModifyThirst1;
        private static MethodInfo _miModifyThirst2;

        private static MethodInfo _miGetBehaviorByType;
        private static MethodInfo _miGetBehaviorGeneric;

        private static bool _initialized;

        private static void EnsureInit(EntityAgent sampleEntity)
        {
            if (_initialized) return;
            var asms = AppDomain.CurrentDomain.GetAssemblies();

            _hydrationManagerType = asms.Select(a => a.GetType(HydrationManagerFullName, false))
                                        .FirstOrDefault(t => t != null);
            if (_hydrationManagerType != null)
            {
                _miGetHydration = _hydrationManagerType.GetMethod("GetHydration",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    binder: null, types: new[] { typeof(ItemStack) }, modifiers: null);
            }

            _thirstBehaviorType = asms.Select(a => a.GetType(ThirstBehaviorFullName, false))
                                      .FirstOrDefault(t => t != null);

            if (_thirstBehaviorType != null)
            {
                _miModifyThirst1 = _thirstBehaviorType.GetMethod("ModifyThirst",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: new[] { typeof(float) }, modifiers: null);

                _miModifyThirst2 = _thirstBehaviorType.GetMethod("ModifyThirst",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: new[] { typeof(float), typeof(float) }, modifiers: null);
            }

            var entType = sampleEntity?.GetType();
            _miGetBehaviorByType = entType?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetBehavior" &&
                                     m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType == typeof(Type));

            _miGetBehaviorGeneric = entType?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetBehavior" &&
                                     m.IsGenericMethodDefinition &&
                                     m.GetGenericArguments().Length == 1 &&
                                     m.GetParameters().Length == 0);

            _initialized = true;
        }

        public static bool TryApplyHydration(EntityAgent byEntity, ItemStack stack, float multiplier = 1f)
        {
            try
            {
                if (byEntity == null || stack == null) return false;
                EnsureInit(byEntity);

                if (_miGetHydration == null) return false;

                var objHydration = _miGetHydration.Invoke(null, new object[] { stack });
                if (objHydration == null) return false;

                float hydration = Convert.ToSingle(objHydration);
                if (Math.Abs(hydration) <= 0f) return false;

                hydration *= multiplier;
                if (Math.Abs(hydration) <= 0f) return false;

                object thirstBehavior = null;

                if (_thirstBehaviorType != null)
                {
                    if (_miGetBehaviorByType != null)
                        thirstBehavior = _miGetBehaviorByType.Invoke(byEntity, new object[] { _thirstBehaviorType });
                    else if (_miGetBehaviorGeneric != null)
                        thirstBehavior = _miGetBehaviorGeneric.MakeGenericMethod(_thirstBehaviorType)
                            .Invoke(byEntity, Array.Empty<object>());
                }

                if (thirstBehavior == null && _thirstBehaviorType != null)
                {
                    var fld = byEntity.GetType().GetField("behaviors",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var list = fld?.GetValue(byEntity) as System.Collections.IEnumerable;
                    if (list != null)
                        foreach (var b in list)
                            if (b != null && _thirstBehaviorType.IsInstanceOfType(b))
                            {
                                thirstBehavior = b;
                                break;
                            }
                }

                if (thirstBehavior == null) return false;

                if (_miModifyThirst2 != null)
                {
                    _miModifyThirst2.Invoke(thirstBehavior, new object[] { hydration, 0f });
                    return true;
                }

                if (_miModifyThirst1 != null)
                {
                    _miModifyThirst1.Invoke(thirstBehavior, new object[] { hydration });
                    return true;
                }

                var dyn = thirstBehavior.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == "ModifyThirst")
                    .ToArray();

                foreach (var m in dyn)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(float))
                    {
                        m.Invoke(thirstBehavior, new object[] { hydration });
                        return true;
                    }

                    if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float))
                    {
                        m.Invoke(thirstBehavior, new object[] { hydration, 0f });
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
