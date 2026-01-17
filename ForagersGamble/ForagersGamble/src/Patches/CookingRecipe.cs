using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ForagersGamble;
using ForagersGamble.Config;
using ForagersGamble.Patches;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(CookingRecipe), nameof(CookingRecipe.GetOutputName))]
public static class Patch_CookingRecipe_GetOutputName
{
    private static readonly string[] Roles = { "primary", "secondary", "tertiary", "quaternary" };
    private static readonly string[] Suffixes = { "", "-instrumentalcase", "-topping" };

    private static readonly Dictionary<EnumItemClass, string> ItemClassLowerCache = new();
    private static string GetItemClassLower(EnumItemClass iclass)
    {
        lock (ItemClassLowerCache)
        {
            if (!ItemClassLowerCache.TryGetValue(iclass, out var s))
            {
                s = iclass.ToString().ToLowerInvariant();
                ItemClassLowerCache[iclass] = s;
            }
            return s;
        }
    }

    [ThreadStatic] private static Dictionary<string, string> _tokensToMask;
    [ThreadStatic] private static List<string> _sortedKeys;

    private const int ResultCacheMax = 512;

    private struct CacheKey : IEquatable<CacheKey>
    {
        public string RecipeCode;
        public string BaseName;
        public string InputsSig;
        public int KnowledgeRevision;
        public int ConfigSig;

        public bool Equals(CacheKey other)
        {
            return string.Equals(RecipeCode, other.RecipeCode, StringComparison.Ordinal) &&
                   string.Equals(BaseName, other.BaseName, StringComparison.Ordinal) &&
                   string.Equals(InputsSig, other.InputsSig, StringComparison.Ordinal) &&
                   KnowledgeRevision == other.KnowledgeRevision &&
                   ConfigSig == other.ConfigSig;
        }

        public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ (RecipeCode?.GetHashCode() ?? 0);
                h = (h * 31) ^ (BaseName?.GetHashCode() ?? 0);
                h = (h * 31) ^ (InputsSig?.GetHashCode() ?? 0);
                h = (h * 31) ^ KnowledgeRevision;
                h = (h * 31) ^ ConfigSig;
                return h;
            }
        }
    }

    private static readonly object CacheLock = new();
    private static readonly Dictionary<CacheKey, string> ResultCache = new();
    private static readonly Queue<CacheKey> CacheFifo = new();

    private static bool TryGetCached(in CacheKey key, out string value)
    {
        lock (CacheLock)
        {
            if (ResultCache.TryGetValue(key, out value) && value != null)
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void PutCached(in CacheKey key, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        lock (CacheLock)
        {
            if (ResultCache.ContainsKey(key))
            {
                ResultCache[key] = value;
                return;
            }

            ResultCache[key] = value;
            CacheFifo.Enqueue(key);

            while (ResultCache.Count > ResultCacheMax && CacheFifo.Count > 0)
            {
                var old = CacheFifo.Dequeue();
                ResultCache.Remove(old);
            }
        }
    }
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            ResultCache.Clear();
            CacheFifo.Clear();
        }
    }

    private static int ComputeConfigSig(object cfg)
    {
        return cfg?.GetHashCode() ?? 0;
    }

    private static string BuildInputsSignature(ItemStack[] inputStacks)
    {
        var sb = new StringBuilder(128);

        for (int i = 0; i < inputStacks.Length; i++)
        {
            var st = inputStacks[i];
            if (st == null || st.StackSize <= 0 || st.Collectible?.Code == null) continue;

            sb.Append(st.Collectible.Code.Domain);
            sb.Append(':');
            sb.Append(st.Collectible.Code.Path);
            sb.Append('#');
            sb.Append((int)st.Class);
            sb.Append(';');
        }

        return sb.ToString();
    }
    private static readonly string[] RevisionMemberNames =
    {
        "Revision", "Version", "ChangeCounter", "Changes", "DirtyCounter", "UpdateCounter", "Counter"
    };

    private static bool TryGetKnowledgeRevision(object idx, out int revision)
    {
        revision = 0;
        if (idx == null) return false;

        var t = idx.GetType();
        for (int i = 0; i < RevisionMemberNames.Length; i++)
        {
            var name = RevisionMemberNames[i];
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
            {
                var pt = p.PropertyType;
                if (pt == typeof(int))
                {
                    revision = (int)p.GetValue(idx);
                    return true;
                }
                if (pt == typeof(long))
                {
                    revision = unchecked((int)(long)p.GetValue(idx));
                    return true;
                }
            }
        }
        for (int i = 0; i < RevisionMemberNames.Length; i++)
        {
            var name = RevisionMemberNames[i];
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var ft = f.FieldType;
                if (ft == typeof(int))
                {
                    revision = (int)f.GetValue(idx);
                    return true;
                }
                if (ft == typeof(long))
                {
                    revision = unchecked((int)(long)f.GetValue(idx));
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string ReplaceAllWithBoundaries(string input, string token, string replacement)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(token)) return input;

        int startIndex = 0;
        int tokenLen = token.Length;

        int first = input.IndexOf(token, 0, StringComparison.OrdinalIgnoreCase);
        if (first < 0) return input;

        var sb = new StringBuilder(input.Length + Math.Max(0, replacement.Length - tokenLen) * 2);

        while (true)
        {
            int idx = input.IndexOf(token, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                sb.Append(input, startIndex, input.Length - startIndex);
                break;
            }

            int before = idx - 1;
            int after = idx + tokenLen;

            bool beforeOk = before < 0 || !IsWordChar(input[before]);
            bool afterOk = after >= input.Length || !IsWordChar(input[after]);

            if (beforeOk && afterOk)
            {
                sb.Append(input, startIndex, idx - startIndex);
                sb.Append(replacement);
                startIndex = idx + tokenLen;
            }
            else
            {
                sb.Append(input, startIndex, (idx + 1) - startIndex);
                startIndex = idx + 1;
            }
        }

        return sb.ToString();
    }

    static void Postfix(CookingRecipe __instance, IWorldAccessor worldForResolve, ItemStack[] inputStacks, ref string __result)
    {
        if (string.IsNullOrWhiteSpace(__result) || inputStacks == null || inputStacks.Length == 0) return;

        var cworld = worldForResolve as IClientWorldAccessor;
        var agent = cworld?.Player?.Entity as EntityPlayer;
        if (agent == null) return;
        if (agent.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

        var api = cworld.Api;
        if (api == null) return;

        var cfg = ModConfig.Instance.Main;
        var idx = PlantKnowledgeIndex.Get(api);

        bool canCache = TryGetKnowledgeRevision(idx, out int knowledgeRev);
        int configSig = ComputeConfigSig(cfg);

        CacheKey cacheKey = default;

        if (canCache)
        {
            string recipeCode = __instance?.Code?.ToString() ?? "";
            string inputsSig = BuildInputsSignature(inputStacks);

            cacheKey = new CacheKey
            {
                RecipeCode = recipeCode,
                BaseName = __result,
                InputsSig = inputsSig,
                KnowledgeRevision = knowledgeRev,
                ConfigSig = configSig
            };

            if (TryGetCached(cacheKey, out var cached))
            {
                __result = cached;
                return;
            }
        }
        var tokensToMask = _tokensToMask ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        tokensToMask.Clear();
        for (int i = 0; i < inputStacks.Length; i++)
        {
            var stack = inputStacks[i];
            if (stack == null || stack.StackSize <= 0 || stack.Collectible == null) continue;

            var masked = IngredientMasking.MaskedIngredientLabel(api, agent, stack, cfg, idx);
            if (string.IsNullOrWhiteSpace(masked)) continue;

            var dom = stack.Collectible.Code?.Domain ?? "game";
            var path = stack.Collectible.Code?.Path ?? "";
            var first = stack.Collectible.FirstCodePart(0) ?? "";
            for (int r = 0; r < Roles.Length; r++)
            {
                var role = Roles[r];

                var keyFull = $"meal-ingredient-{__instance.Code}-{role}-{path}";
                var keyFirst = $"meal-ingredient-{__instance.Code}-{role}-{first}";

                if (Lang.HasTranslation(keyFull, true, true))
                {
                    var match = Lang.GetMatching(keyFull);
                    if (!string.IsNullOrWhiteSpace(match)) tokensToMask[match] = masked;
                }

                if (Lang.HasTranslation(keyFirst, true, true))
                {
                    var match = Lang.GetMatching(keyFirst);
                    if (!string.IsNullOrWhiteSpace(match)) tokensToMask[match] = masked;
                }
            }
            var iclassLower = GetItemClassLower(stack.Class);

            for (int s = 0; s < Suffixes.Length; s++)
            {
                var suffix = Suffixes[s];

                var ikeyFull = $"{dom}:recipeingredient-{iclassLower}-{path}{suffix}";
                var ikeyFirst = $"{dom}:recipeingredient-{iclassLower}-{first}{suffix}";

                if (Lang.HasTranslation(ikeyFull, true, true))
                {
                    var match = Lang.GetMatching(ikeyFull);
                    if (!string.IsNullOrWhiteSpace(match)) tokensToMask[match] = masked;
                }

                if (Lang.HasTranslation(ikeyFirst, true, true))
                {
                    var match = Lang.GetMatching(ikeyFirst);
                    if (!string.IsNullOrWhiteSpace(match)) tokensToMask[match] = masked;
                }
            }
            var plain = stack.GetName();
            if (!string.IsNullOrWhiteSpace(plain))
            {
                tokensToMask[plain] = masked;
            }
        }

        if (tokensToMask.Count == 0) return;
        var sortedKeys = _sortedKeys ??= new List<string>(64);
        sortedKeys.Clear();
        foreach (var k in tokensToMask.Keys)
        {
            if (!string.IsNullOrWhiteSpace(k)) sortedKeys.Add(k);
        }

        if (sortedKeys.Count == 0) return;

        sortedKeys.Sort(static (a, b) => b.Length.CompareTo(a.Length));

        string result = __result;
        for (int i = 0; i < sortedKeys.Count; i++)
        {
            var key = sortedKeys[i];
            if (!tokensToMask.TryGetValue(key, out var repl) || string.IsNullOrEmpty(repl)) continue;
            if (string.Equals(key, repl, StringComparison.OrdinalIgnoreCase)) continue;

            result = ReplaceAllWithBoundaries(result, key, repl);
        }

        __result = result;
        if (canCache)
        {
            PutCached(cacheKey, __result);
        }
    }
}
