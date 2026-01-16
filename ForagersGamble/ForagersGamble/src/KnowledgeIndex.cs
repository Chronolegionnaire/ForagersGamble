using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ForagersGamble
{
    public sealed class PlantKnowledgeIndex
    {
        public struct FruitRef
        {
            public AssetLocation Code;
            public EnumItemClass Type;
        }

        private readonly Dictionary<string, FruitRef> plantToFruit = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> noFruit = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> knowledgeGatedBlocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> mushroomCodes = new(StringComparer.OrdinalIgnoreCase);

        private PlantKnowledgeIndex()
        {
        }

        public static PlantKnowledgeIndex Build(ICoreAPI api)
        {
            var idx = new PlantKnowledgeIndex();

            foreach (var bl in api.World.Blocks)
            {
                if (bl?.Code == null) continue;
                var bcode = bl.Code.ToString();

                if (bl is BlockMushroom) idx.mushroomCodes.Add(bcode);
                if (PlantKnowledgeUtil.IsKnowledgeGatedPlant(bl)) idx.knowledgeGatedBlocks.Add(bcode);
            }

            foreach (var bl in api.World.Blocks)
            {
                if (bl?.Code == null) continue;
                var bcode = bl.Code.ToString();
                if (!idx.knowledgeGatedBlocks.Contains(bcode)) continue;

                if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bl, new ItemStack(bl), out var fruit))
                {
                    var code = fruit.Collectible?.Code;
                    if (code != null)
                    {
                        idx.plantToFruit[bcode] = new FruitRef
                        {
                            Code = code,
                            Type = fruit.Collectible is Item ? EnumItemClass.Item : EnumItemClass.Block
                        };
                    }
                    else
                    {
                        idx.noFruit.Add(bcode);
                    }
                }
                else
                {
                    idx.noFruit.Add(bcode);
                }
            }

            return idx;
        }

        public bool IsMushroom(string code) => !string.IsNullOrEmpty(code) && mushroomCodes.Contains(code);

        public bool IsKnowledgeGated(string blockCode) =>
            !string.IsNullOrEmpty(blockCode)
            && knowledgeGatedBlocks.Contains(blockCode)
            && !noFruit.Contains(blockCode);

        public bool TryGetFruit(string blockCode, out FruitRef fruit) =>
            plantToFruit.TryGetValue(blockCode ?? "", out fruit);

        public bool IsNoFruit(string blockCode) => noFruit.Contains(blockCode);

        public static PlantKnowledgeIndex Get(ICoreAPI api) =>
            api.ObjectCache.TryGetValue("ForagersGamble.PlantKnowledgeIndex", out var o)
                ? (PlantKnowledgeIndex)o
                : null;

        public static void Put(ICoreAPI api, PlantKnowledgeIndex idx) =>
            api.ObjectCache["ForagersGamble.PlantKnowledgeIndex"] = idx;
    }
}
