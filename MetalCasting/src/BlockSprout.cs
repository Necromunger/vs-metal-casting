using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BlockSprout : Block
{
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BESprout be)
            be.UpdateConnections(true);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return false;

        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Collectible is not BlockSmeltedContainer crucible)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.Side != EnumAppSide.Server) return true;

        var sproutBe = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BESprout;
        if (sproutBe == null || sproutBe.IsOrphan) return true;

        // Network lookup via the sprout's attached runner
        var anchor = sproutBe.Pos;
        var mgr = MetalCastingModSystem.Instance?.NetworkManager;
        RunnerNetwork net = null;
        for (int i = 0; i < 4 && net == null; i++)
        {
            var np = anchor.AddCopy(BlockFacing.ALLFACES[i]);
            net = mgr?.GetNetwork(np);
        }
        if (net == null || net.Runners.Count == 0) return true;

        BlockRunner.TryPourFromCrucible(world, crucible, slot, net, blockSel.Position, byPlayer);
        return true;
    }
}
