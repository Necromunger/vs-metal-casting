using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MetalCasting.BlockEntities;

namespace MetalCasting.Blocks;

public class BlockCrucibleFurnace : Block, IIgnitable, IHeatSource
{
    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (byEntity.World.BlockAccessor.GetBlockEntity(pos) is not BECrucibleFurnace be)
            return EnumIgniteState.NotIgnitablePreventDefault;
        if (be.IsBurning) return EnumIgniteState.NotIgnitablePreventDefault;
        if (be.FuelSlot.Empty) return EnumIgniteState.NotIgnitablePreventDefault;
        if (secondsIgniting < 2f) return EnumIgniteState.Ignitable;
        return EnumIgniteState.IgniteNow;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (byEntity.World.BlockAccessor.GetBlockEntity(pos) is BECrucibleFurnace be)
        {
            be.Ignite();
            handling = EnumHandling.PreventDefault;
        }
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
    {
        return EnumIgniteState.NotIgnitable;
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        if (world?.BlockAccessor.GetBlockEntity(heatSourcePos) is BECrucibleFurnace be)
        {
            return be.GetHeatStrength(world, heatSourcePos, heatReceiverPos);
        }

        return 0f;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BECrucibleFurnace be)
        {
            if (world.Side == EnumAppSide.Server && be.TryAddFuel(byPlayer)) return true;
            if (world.Side == EnumAppSide.Client) return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}
