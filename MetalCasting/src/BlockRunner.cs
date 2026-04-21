using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BlockRunner : Block
{
    private const int UnitsPerTickPerMold = 2;


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

        var net = MetalCastingModSystem.Instance?.NetworkManager?.GetNetwork(blockSel.Position);
        if (net == null || net.Runners.Count == 0) return true;

        var molds = net.GetConnectedMolds(world);
        if (molds.Count == 0) return true;

        var interactBe = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BERunner;
        bool wasPouring = interactBe?.IsPouring == true;

        if (DistributeCrucibleIntoMolds(world, crucible, slot, molds, byPlayer))
        {
            foreach (var rpos in net.Runners)
            {
                if (world.BlockAccessor.GetBlockEntity(rpos) is BERunner rbe)
                    rbe.BeginFlow();
            }

            float temperature = slot.Itemstack.Collectible.GetTemperature(world, slot.Itemstack);

            if (!wasPouring)
            {
                world.PlaySoundAt(
                    new AssetLocation("sounds/pourmetal"),
                    blockSel.Position.X + 0.5,
                    blockSel.Position.Y + 0.5,
                    blockSel.Position.Z + 0.5,
                    byPlayer);
            }

            SpawnPourParticles(world, blockSel.Position, temperature, byPlayer);
        }
        return true;
    }

    private static void SpawnPourParticles(IWorldAccessor world, BlockPos pos, float temperature, IPlayer byPlayer)
    {
        Vec3d target = pos.ToVec3d().Add(0.5, 0.2, 0.5);

        BlockSmeltedContainer.bigMetalSparks.MinQuantity = 0.4f;
        BlockSmeltedContainer.bigMetalSparks.MinVelocity.Set(-2f, 1f, -2f);
        BlockSmeltedContainer.bigMetalSparks.AddVelocity.Set(4f, 5f, 4f);
        BlockSmeltedContainer.bigMetalSparks.MinPos = target.AddCopy(-0.25, 0.0, -0.25);
        BlockSmeltedContainer.bigMetalSparks.AddPos.Set(0.5, 0.0, 0.5);
        BlockSmeltedContainer.bigMetalSparks.VertexFlags = (byte)GameMath.Clamp((int)temperature - 770, 48, 128);
        world.SpawnParticles(BlockSmeltedContainer.bigMetalSparks, byPlayer);

        world.SpawnParticles(
            4f,
            ColorUtil.ToRgba(50, 180, 180, 180),
            target.AddCopy(-0.5, 0.0, -0.5),
            target.AddCopy(0.5, 0.15, 0.5),
            new Vec3f(-0.5f, 0f, -0.5f),
            new Vec3f(0.5f, 0f, 0.5f),
            1.5f,
            -0.05f,
            0.4f,
            EnumParticleModel.Quad,
            byPlayer);
    }

    private static bool DistributeCrucibleIntoMolds(
        IWorldAccessor world,
        BlockSmeltedContainer crucible,
        ItemSlot crucibleSlot,
        HashSet<BlockPos> molds,
        IPlayer byPlayer)
    {
        var contents = crucible.GetContents(world, crucibleSlot.Itemstack);
        ItemStack metalStack = contents.Key;
        int totalUnits = contents.Value;
        if (metalStack == null || totalUnits <= 0) return false;
        if (crucible.HasSolidifed(crucibleSlot.Itemstack, metalStack, world)) return false;

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

        if (sinks.Count == 0) return false;

        float temperature = crucibleSlot.Itemstack.Collectible.GetTemperature(world, crucibleSlot.Itemstack);

        int totalPoured = 0;
        foreach (var sink in sinks)
        {
            int remaining = totalUnits - totalPoured;
            if (remaining <= 0) break;
            int share = System.Math.Min(UnitsPerTickPerMold, remaining);
            int before = share;
            PourIntoSink(sink, metalStack, ref share, temperature);
            totalPoured += before - share;
        }

        if (totalPoured <= 0) return false;

        int leftInCrucible = totalUnits - totalPoured;
        crucibleSlot.Itemstack.Attributes.SetInt("units", leftInCrucible);
        if (leftInCrucible <= 0)
        {
            var emptiedCode = crucible.Attributes["emptiedBlockCode"].AsString();
            if (emptiedCode != null)
            {
                var emptyBlock = world.GetBlock(AssetLocation.Create(emptiedCode, crucible.Code.Domain));
                if (emptyBlock != null) crucibleSlot.Itemstack = new ItemStack(emptyBlock);
            }
        }
        crucibleSlot.MarkDirty();
        return true;
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
