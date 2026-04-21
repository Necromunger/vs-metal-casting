using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class BlockRunner : Block
{
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BERunner be)
            be.UpdateConnections(false);
    }
}
