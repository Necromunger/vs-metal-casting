using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class RunnerNetwork
{
    public long NetworkId { get; }
    public List<BlockPos> Runners { get; } = new();

    public RunnerNetwork(long id) => NetworkId = id;

    public void AddRunner(BlockPos pos)
    {
        if (!Runners.Contains(pos)) Runners.Add(pos.Copy());
    }

    public void RemoveRunner(BlockPos pos) => Runners.Remove(pos);

    public void Merge(RunnerNetwork other)
    {
        foreach (var p in other.Runners)
            if (!Runners.Contains(p)) Runners.Add(p.Copy());
    }

    public List<BESprout> GetConnectedSprouts(IWorldAccessor world)
    {
        var result = new List<BESprout>();
        var seen = new HashSet<BlockPos>();
        foreach (var p in Runners)
        {
            if (world.BlockAccessor.GetBlockEntity(p) is not BERunner be) continue;
            foreach (var sp in be.GetConnectedSprouts())
            {
                if (!seen.Add(sp)) continue;
                if (world.BlockAccessor.GetBlockEntity(sp) is BESprout sbe && !sbe.IsOrphan)
                    result.Add(sbe);
            }
        }
        return result;
    }

    public HashSet<BlockPos> GetDeliveryMolds(IWorldAccessor world, List<BESprout> sprouts)
    {
        var molds = new HashSet<BlockPos>();
        foreach (var sbe in sprouts)
        {
            var moldPos = sbe.GetTargetMoldPos();
            if (moldPos != null) molds.Add(moldPos);
        }
        return molds;
    }
}
