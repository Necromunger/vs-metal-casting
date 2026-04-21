using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BlockRunner : Block
{
    private const int UnitsPerTick = 20;


    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BERunner be)
            be.UpdateConnections(false);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return false;

        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Collectible is not BlockSmeltedContainer crucible)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.Side != EnumAppSide.Server) return true;

        var mgr = MetalCastingModSystem.Instance?.NetworkManager;
        var net = mgr?.GetNetwork(blockSel.Position);
        world.Api.Logger.Notification($"[MetalCasting] Interact at {blockSel.Position} mgr={(mgr == null ? "null" : "ok")} net={(net == null ? "null" : $"id={net.NetworkId} runners={net.Runners.Count}")}");
        if (net == null || net.Runners.Count == 0) return true;

        var molds = net.GetConnectedMolds(world);
        if (molds.Count == 0) return true;

        DistributeCrucibleIntoMolds(world, crucible, slot, molds, byPlayer);
        return true;
    }

    private static void DistributeCrucibleIntoMolds(
        IWorldAccessor world,
        BlockSmeltedContainer crucible,
        ItemSlot crucibleSlot,
        HashSet<BlockPos> molds,
        IPlayer byPlayer)
    {
        var contents = crucible.GetContents(world, crucibleSlot.Itemstack);
        ItemStack metalStack = contents.Key;
        int totalUnits = contents.Value;
        if (metalStack == null || totalUnits <= 0) return;
        if (crucible.HasSolidifed(crucibleSlot.Itemstack, metalStack, world)) return;

        var sinks = new List<ILiquidMetalSink>();
        foreach (var pos in molds)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is ILiquidMetalSink sink
                && sink.CanReceiveAny
                && sink.CanReceive(metalStack))
            {
                sinks.Add(sink);
            }
        }

        if (sinks.Count == 0) return;

        float temperature = crucibleSlot.Itemstack.Collectible.GetTemperature(world, crucibleSlot.Itemstack);

        int budget = System.Math.Min(UnitsPerTick, totalUnits);
        int perShare = System.Math.Max(1, budget / sinks.Count);

        int totalPoured = 0;
        foreach (var sink in sinks)
        {
            int room = budget - totalPoured;
            if (room <= 0) break;
            int share = System.Math.Min(perShare, room);
            int before = share;
            PourIntoSink(sink, metalStack, ref share, temperature);
            totalPoured += before - share;
        }

        if (totalPoured <= 0) return;

        int remaining = totalUnits - totalPoured;
        crucibleSlot.Itemstack.Attributes.SetInt("units", remaining);
        if (remaining <= 0) crucibleSlot.Itemstack.Attributes.RemoveAttribute("output");
        crucibleSlot.MarkDirty();
    }

    private static void PourIntoSink(ILiquidMetalSink sink, ItemStack metal, ref int amount, float temperature)
    {
        if (sink is BlockEntityIngotMold dual && dual.QuantityMolds > 1)
        {
            dual.IsRightSideSelected = false;
            sink.ReceiveLiquidMetal(metal, ref amount, temperature);
            if (amount > 0)
            {
                dual.IsRightSideSelected = true;
                sink.ReceiveLiquidMetal(metal, ref amount, temperature);
            }
        }
        else
        {
            sink.ReceiveLiquidMetal(metal, ref amount, temperature);
        }
        sink.OnPourOver();
    }
}
