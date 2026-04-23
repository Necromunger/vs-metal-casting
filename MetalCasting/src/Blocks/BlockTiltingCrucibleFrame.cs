using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MetalCasting.Blocks;

public class BlockTiltingCrucibleFrame : Block
{
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        var belowBlock = world.BlockAccessor.GetBlock(blockSel.Position.DownCopy());
        if (belowBlock is not BlockCrucibleFurnace)
        {
            failureCode = "requirecruciblefurnacebelow";
            return false;
        }

        return true;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BETiltingCrucibleFrame be)
            return be.OnInteract(byPlayer);

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}
