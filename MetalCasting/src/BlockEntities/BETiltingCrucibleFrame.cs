using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MetalCasting.BlockEntities;
using MetalCasting.Blocks;

namespace MetalCasting;

public class BETiltingCrucibleFrame : BlockEntityContainer
{
    public const int BowlSlotId = 0;
    public const int OreSlotStart = 1;
    public const int OreSlotCount = 12;
    private const int TotalSlots = 1 + OreSlotCount;
    private const int HeatTickMs = 500;
    private const float HeatRatePerSec = 80f; //TODO: removing this, its driven by used coke
    private const float CoolRatePerSec = 20f;
    private const float MinSmeltTemp = 1000f; //TODO: removing this, its driven by used coke
    private const int UnitsPerOreItem = 5;
    private const int PourRatePerTick = 4;

    private bool isTilted;
    public bool IsTilted => isTilted;

    private readonly InventoryGeneric inv;
    private GuiDialogTiltingCrucibleFrame clientDialog;

    // Rendering
    private MeshData frameMesh;
    private MeshData bowlMesh;
    private int frameBuiltForBlockId = -1;
    private int bowlBuiltForBlockId = -1;
    private const string CrucibleAttachmentCode = "CruciblePoint";

    public override InventoryBase Inventory => inv;
    public override string InventoryClassName => "tiltingcrucibleframe";

    public ItemSlot BowlSlot => inv[BowlSlotId];
    public bool HasBowl => !BowlSlot.Empty;

    public bool BowlHasLiquid
    {
        get
        {
            if (!HasBowl) return false;
            var path = BowlSlot.Itemstack?.Collectible?.Code?.Path;
            return path != null && path.Contains("-smelted");
        }
    }

    public float BowlTemperature
    {
        get
        {
            if (!HasBowl) return 20f;
            return BowlSlot.Itemstack.Collectible.GetTemperature(Api.World, BowlSlot.Itemstack);
        }
    }

    public BETiltingCrucibleFrame()
    {
        inv = new InventoryGeneric(TotalSlots, null, null, NewSlot);
    }

    private ItemSlot NewSlot(int slotId, InventoryGeneric inventory)
    {
        if (slotId == BowlSlotId) return new ItemSlotBowl(inventory);
        return new ItemSlotSurvival(inventory);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inv.LateInitialize($"tiltingcrucibleframe-{Pos}", api);
        inv.SlotModified += OnSlotModified;
        if (api.Side == EnumAppSide.Server)
        {
            RegisterGameTickListener(OnHeatTick, HeatTickMs);
        }
    }

    private void OnSlotModified(int slotId)
    {
        MarkDirty(true);
        if (slotId == BowlSlotId)
        {
            bowlMesh = null;
            bowlBuiltForBlockId = -1;
            if (Api is ICoreClientAPI capi) capi.World.BlockAccessor.MarkBlockDirty(Pos);
        }
        if (Api is ICoreClientAPI && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.RebuildLayout();
        }
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (Api is ICoreClientAPI capi)
        {
            EnsureFrameMesh(capi);
            EnsureBowlMesh(capi);
        }
        if (frameMesh == null) return false;
        mesher.AddMeshData(frameMesh);
        if (HasBowl && bowlMesh != null) mesher.AddMeshData(bowlMesh);
        return true;
    }

    private void EnsureFrameMesh(ICoreClientAPI capi)
    {
        var block = capi.World.BlockAccessor.GetBlock(Pos);
        int id = block?.Id ?? -1;
        if (id == frameBuiltForBlockId && frameMesh != null) return;
        frameMesh = null;
        frameBuiltForBlockId = id;

        if (block?.Shape?.Base == null) return;
        var shapePath = block.Shape.Base.Clone()
            .WithPathAppendixOnce(".json")
            .WithPathPrefixOnce("shapes/");
        var shape = Shape.TryGet(capi, shapePath);
        if (shape == null) return;

        var rot = new Vec3f(block.Shape.rotateX, block.Shape.rotateY, block.Shape.rotateZ);
        capi.Tesselator.TesselateShape(
            typeForLogging: block.Code.ToString(),
            shapeBase: shape,
            modeldata: out frameMesh,
            texSource: capi.Tesselator.GetTextureSource(block),
            meshRotationDeg: rot);
    }

    private void EnsureBowlMesh(ICoreClientAPI capi)
    {
        if (!HasBowl)
        {
            bowlMesh = null;
            bowlBuiltForBlockId = -1;
            return;
        }

        if (BowlSlot.Itemstack.Collectible is not Block bowlBlock) return;
        int id = bowlBlock.Id;
        if (id == bowlBuiltForBlockId && bowlMesh != null) return;
        bowlMesh = null;
        bowlBuiltForBlockId = id;

        if (bowlBlock.Shape?.Base == null) return;
        var shapePath = bowlBlock.Shape.Base.Clone()
            .WithPathAppendixOnce(".json")
            .WithPathPrefixOnce("shapes/");
        var shape = Shape.TryGet(capi, shapePath);
        if (shape == null) return;

        capi.Tesselator.TesselateShape(
            typeForLogging: bowlBlock.Code.ToString(),
            shapeBase: shape,
            modeldata: out bowlMesh,
            texSource: capi.Tesselator.GetTextureSource(bowlBlock));

        // Translate to the attachment point the shape author defined on the frame
        var frameBlock = capi.World.BlockAccessor.GetBlock(Pos);
        var frameAnchor = GetFrameCrucibleAnchor(capi, frameBlock);
        if (frameAnchor != null)
        {
            bowlMesh.Translate(frameAnchor.X, frameAnchor.Y, frameAnchor.Z);
        }

        // Match the frame's rotation so the bowl sits square in rotated variants
        float frameRotY = frameBlock?.Shape?.rotateY ?? 0f;
        if (Math.Abs(frameRotY) > 0.01f)
        {
            var m = new Matrixf();
            m.Translate(0.5f, 0f, 0.5f);
            m.RotateY(frameRotY * GameMath.DEG2RAD);
            m.Translate(-0.5f, 0f, -0.5f);
            bowlMesh.MatrixTransform(m.Values);
        }
    }

    private static Vec3f GetFrameCrucibleAnchor(ICoreClientAPI capi, Block frameBlock)
    {
        if (frameBlock?.Shape?.Base == null) return null;

        var shapePath = frameBlock.Shape.Base.Clone()
            .WithPathAppendixOnce(".json")
            .WithPathPrefixOnce("shapes/");

        var shape = Shape.TryGet(capi, shapePath);
        if (shape?.Elements == null) return null;

        foreach (var el in shape.Elements)
        {
            var ap = FindAttachmentPoint(el, CrucibleAttachmentCode);
            if (ap != null)
                return new Vec3f((float)ap.PosX / 16f, (float)ap.PosY / 16f, (float)ap.PosZ / 16f);
        }

        return null;
    }

    private static AttachmentPoint FindAttachmentPoint(ShapeElement element, string code)
    {
        if (element.AttachmentPoints != null)
        {
            foreach (var ap in element.AttachmentPoints)
            {
                if (ap.Code == code) return ap;
            }
        }

        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                var result = FindAttachmentPoint(child, code);
                if (result != null) return result;
            }
        }

        return null;
    }

    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        bool emptyHand = slot?.Itemstack == null;

        // Empty hand + bowl holds liquid → toggle tilt (authoritative on server)
        if (emptyHand && BowlHasLiquid)
        {
            if (Api.Side == EnumAppSide.Server) ToggleTilt();
            return true;
        }

        // Otherwise open GUI on client
        if (Api is ICoreClientAPI capi)
        {
            if (clientDialog == null)
            {
                clientDialog = new GuiDialogTiltingCrucibleFrame(this, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
            }
            clientDialog.TryOpen();
        }
        return true;
    }

    public void ToggleTilt()
    {
        if (!BowlHasLiquid) { isTilted = false; return; }
        isTilted = !isTilted;
        MarkDirty(true);
    }

    private void OnHeatTick(float dt)
    {
        if (!HasBowl) return;

        float furnaceTemp = GetFurnaceTemperature();
        float newTemp = ApproachTemperature(BowlTemperature, furnaceTemp, dt);
        ApplyTemperature(BowlSlot.Itemstack, newTemp);

        if (BowlHasLiquid)
        {
            if (isTilted) TryPourForward();
            MarkDirty(false);
            return;
        }

        for (int i = OreSlotStart; i < OreSlotStart + OreSlotCount; i++)
        {
            if (inv[i].Itemstack != null) ApplyTemperature(inv[i].Itemstack, newTemp);
        }

        MarkDirty(false);

        if (newTemp >= MinSmeltTemp) TrySmelt(newTemp);
    }

    private BlockFacing GetFacing()
    {
        var block = Api.World.BlockAccessor.GetBlock(Pos);
        string facing = block?.Variant?["facing"];
        return facing switch
        {
            "n" => BlockFacing.NORTH,
            "e" => BlockFacing.EAST,
            "s" => BlockFacing.SOUTH,
            "w" => BlockFacing.WEST,
            _ => BlockFacing.NORTH
        };
    }

    private void TryPourForward()
    {
        if (!BowlHasLiquid) return;
        var bowl = BowlSlot.Itemstack;
        if (bowl?.Collectible is not BlockSmeltedContainer smelted) return;

        var contents = smelted.GetContents(Api.World, bowl);
        if (contents.Key == null || contents.Value <= 0) return;

        float temp = bowl.Collectible.GetTemperature(Api.World, bowl);

        BlockFacing facing = GetFacing();
        BlockPos targetPos = Pos.AddCopy(facing);

        // 1) Direct liquid-metal sink (mold) adjacent in facing direction
        var beTarget = Api.World.BlockAccessor.GetBlockEntity(targetPos);
        if (beTarget is ILiquidMetalSink sink && sink.CanReceiveAny && sink.CanReceive(contents.Key))
        {
            int share = Math.Min(PourRatePerTick, contents.Value);
            int before = share;
            PourIntoSink(sink, contents.Key, ref share, temp);
            int poured = before - share;
            if (poured > 0) SubtractBowlUnits(smelted, bowl, poured);
            return;
        }

        // 2) Runner network via adjacent runner
        if (Api.World.BlockAccessor.GetBlock(targetPos) is BlockRunner)
        {
            var net = MetalCastingModSystem.Instance?.NetworkManager?.GetNetwork(targetPos);
            if (net != null && net.Runners.Count > 0)
            {
                BlockRunner.TryPourFromCrucible(Api.World, smelted, BowlSlot, net, targetPos, null);
                return;
            }
        }
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

    private void SubtractBowlUnits(BlockSmeltedContainer smelted, ItemStack bowl, int poured)
    {
        int remaining = bowl.Attributes.GetInt("units") - poured;
        if (remaining <= 0)
        {
            var emptiedCode = smelted.Attributes["emptiedBlockCode"].AsString();
            if (emptiedCode != null)
            {
                var emptyBlock = Api.World.GetBlock(AssetLocation.Create(emptiedCode, smelted.Code.Domain));
                if (emptyBlock != null)
                {
                    BowlSlot.Itemstack = new ItemStack(emptyBlock);
                    isTilted = false;
                }
            }
        }
        else
        {
            bowl.Attributes.SetInt("units", remaining);
        }
        BowlSlot.MarkDirty();
        MarkDirty(true);
    }

    private float GetFurnaceTemperature()
    {
        var beLow = Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy());
        if (beLow is not BECrucibleFurnace furnace) return 20f;
        return furnace.Temperature;
    }

    private static float ApproachTemperature(float cur, float target, float dt)
    {
        if (cur < target) return Math.Min(target, cur + HeatRatePerSec * dt);
        if (cur > target) return Math.Max(target, cur - CoolRatePerSec * dt);
        return cur;
    }

    private void ApplyTemperature(ItemStack stack, float temp)
    {
        if (stack?.Collectible == null) return;
        stack.Collectible.SetTemperature(Api.World, stack, temp, delayCooldown: true);
    }

    private void TrySmelt(float temperature)
    {
        var ores = new List<ItemStack>();
        for (int i = OreSlotStart; i < OreSlotStart + OreSlotCount; i++)
        {
            if (!inv[i].Empty) ores.Add(inv[i].Itemstack);
        }
        if (ores.Count == 0) return;

        // Ensure every ore is past its melting point
        foreach (var ore in ores)
        {
            float mp = ore.Collectible.GetMeltingPoint(Api.World, null, new DummySlot(ore));
            if (mp <= 0 || temperature < mp) return;
        }

        // Aggregate by smelted result (metal code)
        var totals = new Dictionary<string, int>();
        foreach (var ore in ores)
        {
            var smelted = ore.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack;
            if (smelted == null) continue;
            string metalCode = smelted.Collectible.Code.ToString();
            int units = ore.StackSize * UnitsPerOreItem;
            totals.TryGetValue(metalCode, out int prior);
            totals[metalCode] = prior + units;
        }
        if (totals.Count == 0) return;

        // Pick the dominant metal (MVP: single-metal smelt; if mixed, winner takes total)
        string winnerCode = null;
        int winnerUnits = 0;
        int allUnits = 0;
        foreach (var kv in totals)
        {
            allUnits += kv.Value;
            if (kv.Value > winnerUnits) { winnerUnits = kv.Value; winnerCode = kv.Key; }
        }
        if (winnerCode == null) return;

        var outputStack = ResolveStack(winnerCode);
        if (outputStack == null) return;

        // Swap bowl to the -smelted variant
        var bowl = BowlSlot.Itemstack;
        string bowlColor = ExtractBowlColor(bowl);
        if (bowlColor == null) return;

        var smeltedBlock = Api.World.GetBlock(new AssetLocation("metalcasting", $"largecrucible-{bowlColor}-smelted"));
        if (smeltedBlock == null) return;

        var newBowl = new ItemStack(smeltedBlock);
        newBowl.Attributes.SetItemstack("output", outputStack);
        newBowl.Attributes.SetInt("units", allUnits);
        newBowl.Collectible.SetTemperature(Api.World, newBowl, temperature, delayCooldown: false);

        inv[BowlSlotId].Itemstack = newBowl;
        inv[BowlSlotId].MarkDirty();

        for (int i = OreSlotStart; i < OreSlotStart + OreSlotCount; i++)
        {
            inv[i].Itemstack = null;
            inv[i].MarkDirty();
        }

        MarkDirty(true);
        Api.Logger.Notification($"[MC] Smelted {allUnits} units of {winnerCode} at {Pos}");
    }

    private ItemStack ResolveStack(string fullCode)
    {
        var loc = new AssetLocation(fullCode);
        var item = Api.World.GetItem(loc);
        if (item != null) return new ItemStack(item);
        var block = Api.World.GetBlock(loc);
        if (block != null) return new ItemStack(block);
        return null;
    }

    private static string ExtractBowlColor(ItemStack bowl)
    {
        var path = bowl?.Collectible?.Code?.Path;
        if (path == null) return null;
        var parts = path.Split('-');
        return parts.Length >= 3 ? parts[1] : null;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (!HasBowl) return;
        dsc.AppendLine($"Crucible: {(int)BowlTemperature}°C");
        if (BowlHasLiquid)
        {
            var units = BowlSlot.Itemstack.Attributes.GetInt("units");
            dsc.AppendLine($"Contents: {units} units molten");
            dsc.AppendLine(isTilted ? "Pouring." : "Upright — right-click to tilt.");
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        isTilted = tree.GetBool("isTilted");
        if (Api is ICoreClientAPI && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.RebuildLayout();
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("isTilted", isTilted);
    }
}
