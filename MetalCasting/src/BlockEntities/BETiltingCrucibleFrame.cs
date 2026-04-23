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
    private const int FrameTickMs = 500;
    private const int ClientVisualTickMs = 30;
    private const int PourRatePerTick = 4;
    private const float MaxTiltDeg = 45f;
    private const float TiltSpeed = 3.5f;
    private static readonly Vec3f TiltPivot = new(15f / 16f, 8.7f / 16f, 8f / 16f);
    private static readonly string[] StaticFrameElements = ["Cube12", "Cube14", "Cube15", "Cube16"];
    private static readonly string[] MovingFrameElements =
    [
        "Cube2", "Cube3", "Cube4", "Cube5", "Cube6", "Cube7", "Cube8", "Cube9",
        "Cube10", "Cube11", "Cube13", "Cube17", "Cube22", "Handle"
    ];

    private bool isTilted;
    private float inputStackCookingTime;
    private float visualTilt;
    public bool IsTilted => isTilted;

    private readonly InventoryGeneric inv;
    private GuiDialogTiltingCrucibleFrame clientDialog;

    // Rendering
    private MeshData staticFrameMesh;
    private MeshData movingFrameMesh;
    private MeshData bowlMesh;
    private int frameBuiltForBlockId = -1;
    private int bowlBuiltForBlockId = -1;
    private Vec3f frameAnchor;
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
            RegisterGameTickListener(OnFrameTick, FrameTickMs);
        }
        else
        {
            visualTilt = isTilted ? 1f : 0f;
            RegisterGameTickListener(OnClientVisualTick, ClientVisualTickMs);
        }
    }

    private void OnSlotModified(int slotId)
    {
        MarkDirty(true);
        if (slotId == BowlSlotId || (slotId >= OreSlotStart && slotId < OreSlotStart + OreSlotCount))
        {
            inputStackCookingTime = 0f;
        }

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

    private void OnClientVisualTick(float dt)
    {
        float target = isTilted ? 1f : 0f;
        float prev = visualTilt;

        if (visualTilt < target)
        {
            visualTilt = Math.Min(target, visualTilt + dt * TiltSpeed);
        }
        else if (visualTilt > target)
        {
            visualTilt = Math.Max(target, visualTilt - dt * TiltSpeed);
        }

        if (Math.Abs(visualTilt - prev) > 0.0001f && Api is ICoreClientAPI capi)
        {
            capi.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (Api is not ICoreClientAPI capi) return false;

        EnsureFrameMeshes(capi);
        EnsureBowlMesh(capi);

        var block = capi.World.BlockAccessor.GetBlock(Pos);
        float tiltDeg = MaxTiltDeg * visualTilt;

        if (staticFrameMesh != null)
        {
            var mesh = CopyMesh(staticFrameMesh);
            ApplyStaticFrameTransform(mesh, block);
            mesher.AddMeshData(mesh);
        }

        if (movingFrameMesh != null)
        {
            var mesh = CopyMesh(movingFrameMesh);
            ApplyMovingAssemblyTransform(mesh, block, tiltDeg);
            mesher.AddMeshData(mesh);
        }

        if (HasBowl && bowlMesh != null)
        {
            var mesh = CopyMesh(bowlMesh);
            ApplyBowlTransform(mesh, block, tiltDeg);
            mesher.AddMeshData(mesh);
        }

        return true;
    }

    private void EnsureFrameMeshes(ICoreClientAPI capi)
    {
        var block = capi.World.BlockAccessor.GetBlock(Pos);
        int id = block?.Id ?? -1;
        if (id == frameBuiltForBlockId && (staticFrameMesh != null || movingFrameMesh != null)) return;

        staticFrameMesh = null;
        movingFrameMesh = null;
        frameBuiltForBlockId = id;

        if (block?.Shape?.Base == null) return;
        var shapePath = block.Shape.Base.Clone()
            .WithPathAppendixOnce(".json")
            .WithPathPrefixOnce("shapes/");
        var shape = Shape.TryGet(capi, shapePath);
        if (shape == null) return;

        frameAnchor = GetFrameCrucibleAnchor(shape);

        capi.Tesselator.TesselateShape(
            typeForLogging: block.Code.ToString(),
            shapeBase: shape,
            modeldata: out staticFrameMesh,
            texSource: capi.Tesselator.GetTextureSource(block),
            selectiveElements: StaticFrameElements);

        capi.Tesselator.TesselateShape(
            typeForLogging: block.Code.ToString() + "-moving",
            shapeBase: shape,
            modeldata: out movingFrameMesh,
            texSource: capi.Tesselator.GetTextureSource(block),
            selectiveElements: MovingFrameElements);
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
    }

    private static MeshData CopyMesh(MeshData mesh)
    {
        return mesh?.Clone();
    }

    private void ApplyStaticFrameTransform(MeshData mesh, Block frameBlock)
    {
        if (mesh == null) return;

        float frameRotY = frameBlock?.Shape?.rotateY ?? 0f;
        if (Math.Abs(frameRotY) <= 0.01f) return;

        var m = new Matrixf();
        m.Translate(0.5f, 0f, 0.5f);
        m.RotateY(frameRotY * GameMath.DEG2RAD);
        m.Translate(-0.5f, 0f, -0.5f);
        mesh.MatrixTransform(m.Values);
    }

    private void ApplyMovingAssemblyTransform(MeshData mesh, Block frameBlock, float tiltDeg)
    {
        if (mesh == null) return;

        var m = new Matrixf();
        m.Translate(TiltPivot.X, TiltPivot.Y, TiltPivot.Z);
        m.RotateX(-tiltDeg * GameMath.DEG2RAD);
        m.Translate(-TiltPivot.X, -TiltPivot.Y, -TiltPivot.Z);

        float frameRotY = frameBlock?.Shape?.rotateY ?? 0f;
        if (Math.Abs(frameRotY) > 0.01f)
        {
            m.Translate(0.5f, 0f, 0.5f);
            m.RotateY(frameRotY * GameMath.DEG2RAD);
            m.Translate(-0.5f, 0f, -0.5f);
        }

        mesh.MatrixTransform(m.Values);
    }

    private void ApplyBowlTransform(MeshData mesh, Block frameBlock, float tiltDeg)
    {
        if (mesh == null) return;

        var anchor = frameAnchor ?? new Vec3f();
        var m = new Matrixf();
        m.Translate(anchor.X, anchor.Y, anchor.Z);
        m.Translate(TiltPivot.X, TiltPivot.Y, TiltPivot.Z);
        m.RotateX(-tiltDeg * GameMath.DEG2RAD);
        m.Translate(-TiltPivot.X, -TiltPivot.Y, -TiltPivot.Z);

        float frameRotY = frameBlock?.Shape?.rotateY ?? 0f;
        if (Math.Abs(frameRotY) > 0.01f)
        {
            m.Translate(0.5f, 0f, 0.5f);
            m.RotateY(frameRotY * GameMath.DEG2RAD);
            m.Translate(-0.5f, 0f, -0.5f);
        }

        mesh.MatrixTransform(m.Values);
    }

    private static Vec3f GetFrameCrucibleAnchor(Shape shape)
    {
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

        // Empty hand toggles pouring posture; if the bowl empties while tilted, the player can still untilt it.
        if (emptyHand && (BowlHasLiquid || isTilted))
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
        isTilted = !isTilted;
        MarkDirty(true);
    }

    private void OnFrameTick(float dt)
    {
        if (BowlHasLiquid && isTilted)
        {
            TryPourForward();
            MarkDirty(false);
        }
    }

    public void ReceiveHeat(float sourceTemperature, float dt)
    {
        if (!HasBowl || sourceTemperature <= HeatMath.AmbientTemperature) return;

        var cookingSlotsProvider = new CookingSlotProvider(GetOreSlots());
        bool changed = HeatMath.HeatSlot(Api.World, BowlSlot, sourceTemperature, dt, cookingSlotsProvider);

        if (!BowlHasLiquid)
        {
            foreach (var slot in cookingSlotsProvider.Slots)
            {
                changed |= HeatMath.HeatSlot(Api.World, slot, sourceTemperature, dt);
            }

            changed |= UpdateVanillaSmelting(cookingSlotsProvider, dt);
        }

        if (changed) MarkDirty(false);
    }

    private ItemSlot[] GetOreSlots()
    {
        var slots = new ItemSlot[OreSlotCount];
        for (int i = 0; i < OreSlotCount; i++)
        {
            slots[i] = inv[OreSlotStart + i];
        }
        return slots;
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
            LiquidMetalUtil.PourIntoSink(sink, contents.Key, ref share, temp);
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

    private bool UpdateVanillaSmelting(ISlotProvider cookingSlotsProvider, float dt)
    {
        var bowl = BowlSlot.Itemstack;
        if (bowl?.Collectible is not BlockSmeltingContainer)
        {
            return ResetCookingProgress();
        }

        if (bowl.Collectible.OnSmeltAttempt(inv))
        {
            MarkDirty(true);
        }

        if (!CanSmeltInput(cookingSlotsProvider))
        {
            return ResetCookingProgress();
        }

        float meltingPoint = bowl.Collectible.GetMeltingPoint(Api.World, cookingSlotsProvider, BowlSlot);
        if (meltingPoint <= 0f)
        {
            return ResetCookingProgress();
        }

        float before = inputStackCookingTime;
        float inputStackTemp = HeatMath.GetLowestStackTemperature(Api.World, cookingSlotsProvider);
        if (inputStackTemp >= meltingPoint)
        {
            float tempMul = inputStackTemp / meltingPoint;
            inputStackCookingTime += (float)GameMath.Clamp((int)tempMul, 1, 30) * dt;
        }
        else if (inputStackCookingTime > 0f)
        {
            inputStackCookingTime = Math.Max(0f, inputStackCookingTime - 1f);
        }

        float maxCookingTime = bowl.Collectible.GetMeltingDuration(Api.World, cookingSlotsProvider, BowlSlot);
        if (maxCookingTime > 0f && inputStackCookingTime > maxCookingTime)
        {
            SmeltItemsWithVanilla(cookingSlotsProvider);
            return true;
        }

        return Math.Abs(inputStackCookingTime - before) > HeatMath.TemperatureEpsilon;
    }

    private bool ResetCookingProgress()
    {
        if (inputStackCookingTime <= 0f) return false;
        inputStackCookingTime = 0f;
        return true;
    }

    private bool CanSmeltInput(ISlotProvider cookingSlotsProvider)
    {
        var bowl = BowlSlot.Itemstack;
        if (bowl == null) return false;
        if (!bowl.Collectible.CanSmelt(Api.World, cookingSlotsProvider, bowl, null)) return false;

        var combust = bowl.Collectible.GetCombustibleProperties(Api.World, bowl, null);
        return combust == null || !combust.RequiresContainer;
    }

    private void SmeltItemsWithVanilla(ISlotProvider cookingSlotsProvider)
    {
        var bowl = BowlSlot.Itemstack;
        if (bowl == null) return;

        var outputSlot = new DummySlot();
        bowl.Collectible.DoSmelt(Api.World, cookingSlotsProvider, BowlSlot, outputSlot);
        inputStackCookingTime = 0f;

        if (outputSlot.Itemstack != null)
        {
            BowlSlot.Itemstack = outputSlot.Itemstack;
        }

        BowlSlot.MarkDirty();
        foreach (var slot in cookingSlotsProvider.Slots)
        {
            slot.MarkDirty();
        }

        bowlMesh = null;
        bowlBuiltForBlockId = -1;
        MarkDirty(true);
        Api.Logger.Notification($"[MC] Smelted crucible contents at {Pos} using vanilla smelting");
    }

    private sealed class CookingSlotProvider : ISlotProvider
    {
        public ItemSlot[] Slots { get; }

        public CookingSlotProvider(ItemSlot[] slots)
        {
            Slots = slots;
        }
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
        inputStackCookingTime = tree.GetFloat("inputStackCookingTime");
        if (Api is ICoreClientAPI && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.RebuildLayout();
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("isTilted", isTilted);
        tree.SetFloat("inputStackCookingTime", inputStackCookingTime);
    }
}
