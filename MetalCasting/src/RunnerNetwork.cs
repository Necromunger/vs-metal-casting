using System.Collections.Generic;
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
}
