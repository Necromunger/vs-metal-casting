using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BlockTiltingCrucibleFrame : Block
{
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        var belowBlock = world.BlockAccessor.GetBlock(blockSel.Position.DownCopy());
        if (belowBlock is not BlockForge)
        {
            failureCode = "requireforgebelow";
            return false;
        }

        return true;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BETiltingCrucibleFrame be)
        {
            if (world.Side == EnumAppSide.Server && be.TryInsertCrucible(byPlayer)) return true;
            if (be.HasBowl) return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BETiltingCrucibleFrame be)
            be.DropContents();
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
}
