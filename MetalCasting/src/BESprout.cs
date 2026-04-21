using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BESprout : BlockEntity
{
    private const int MaxDropScan = 10;
    private const int PourIdleMs = 300;

    // Which cardinal side a runner is on (index into BlockFacing.ALLFACES), -1 if orphan
    private int runnerFacing = -1;
    private string currentVariant = "n";
    private bool isUpdating;

    private BlockPos cachedMoldPos;
    private bool moldResolved;

    public bool IsPouring { get; private set; }
    private long pourExpiryMs;

    private MeshData baseMesh;
    private MeshData liquidMesh;
    private int builtForBlockId = -1;

    public bool IsOrphan => runnerFacing < 0;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            UpdateConnections(true);
            RegisterGameTickListener(OnTickPourExpiry, 100);
        }
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        UpdateConnections(true);
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server && runnerFacing >= 0)
        {
            var runnerPos = Pos.AddCopy(BlockFacing.ALLFACES[runnerFacing]);
            base.OnBlockRemoved();
            if (Api.World.BlockAccessor.GetBlockEntity(runnerPos) is BERunner nbe)
                nbe.UpdateConnections(false);
            return;
        }
        base.OnBlockRemoved();
    }

    public void UpdateConnections(bool notifyRunner)
    {
        if (isUpdating || Api == null || Api.Side != EnumAppSide.Server) return;
        isUpdating = true;
        try
        {
            int newFacing = -1;
            for (int i = 0; i < 4; i++)
            {
                var np = Pos.AddCopy(BlockFacing.ALLFACES[i]);
                if (Api.World.BlockAccessor.GetBlock(np) is BlockRunner)
                {
                    newFacing = i;
                    break;
                }
            }
            Api.Logger.Notification($"[MC] Sprout {Pos} facing={newFacing} (prev={runnerFacing})");

            if (newFacing != runnerFacing)
            {
                runnerFacing = newFacing;
                InvalidateMoldCache();
                if (runnerFacing >= 0) UpdateVariant();
                MarkDirty(true);
            }

            if (notifyRunner && runnerFacing >= 0)
            {
                var np = Pos.AddCopy(BlockFacing.ALLFACES[runnerFacing]);
                if (Api.World.BlockAccessor.GetBlockEntity(np) is BERunner nbe)
                    nbe.UpdateConnections(false);
            }
        }
        finally
        {
            isUpdating = false;
        }
    }

    private static readonly string[] FacingVariants = { "n", "e", "s", "w" };

    private void UpdateVariant()
    {
        string newVariant = FacingVariants[runnerFacing];
        Api.Logger.Notification($"[MC] Sprout.UpdateVariant at {Pos} newVariant={newVariant} currentVariant={currentVariant}");
        if (newVariant == currentVariant) return;

        var current = Api.World.BlockAccessor.GetBlock(Pos);
        if (current == null) { Api.Logger.Notification("[MC]   bail: current block null"); return; }

        int partsToRemove = 0;
        foreach (var v in current.Variant.Values)
            partsToRemove += v.Split('-').Length;

        string[] pathParts = current.Code.Path.Split('-');
        Api.Logger.Notification($"[MC]   curCode={current.Code} partsToRemove={partsToRemove} pathParts=[{string.Join(",", pathParts)}]");
        if (pathParts.Length <= partsToRemove) { Api.Logger.Notification("[MC]   bail: pathParts.Length <= partsToRemove"); return; }
        string basePath = string.Join("-", pathParts.Take(pathParts.Length - partsToRemove));

        var newCode = new AssetLocation(current.Code.Domain, $"{basePath}-{newVariant}");
        var newBlock = Api.World.GetBlock(newCode);
        Api.Logger.Notification($"[MC]   newCode={newCode} newBlock={(newBlock == null ? "null" : newBlock.Id.ToString())} curId={current.Id}");
        if (newBlock == null || newBlock.Id == current.Id) { Api.Logger.Notification("[MC]   bail: newBlock null or same id"); return; }

        var tree = new TreeAttribute();
        ToTreeAttributes(tree);
        Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);
        var newBe = Api.World.BlockAccessor.GetBlockEntity(Pos);
        if (newBe != null)
        {
            newBe.FromTreeAttributes(tree, Api.World);
            newBe.MarkDirty();
        }
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
        currentVariant = newVariant;
    }

    public void InvalidateMoldCache()
    {
        moldResolved = false;
        cachedMoldPos = null;
    }

    public BlockPos GetTargetMoldPos()
    {
        if (IsOrphan) return null;
        if (!moldResolved) ResolveMoldDown();
        if (cachedMoldPos == null) return null;
        var be = Api.World.BlockAccessor.GetBlockEntity(cachedMoldPos);
        if (be is ILiquidMetalSink) return cachedMoldPos;
        InvalidateMoldCache();
        return null;
    }

    private void ResolveMoldDown()
    {
        moldResolved = true;
        cachedMoldPos = null;
        if (IsOrphan || Api == null) return;

        var probe = Pos.DownCopy();
        for (int d = 0; d < MaxDropScan; d++)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(probe);
            if (be is ILiquidMetalSink)
            {
                cachedMoldPos = probe.Copy();
                return;
            }
            var block = Api.World.BlockAccessor.GetBlock(probe);
            bool passable =
                block == null
                || block.Id == 0
                || (block.CollisionBoxes == null && block.SelectionBoxes == null);
            if (!passable) return;
            probe = probe.DownCopy();
        }
    }

    public void BeginFlow()
    {
        if (Api == null || IsOrphan) return;
        pourExpiryMs = Api.World.ElapsedMilliseconds + PourIdleMs;
        if (!IsPouring)
        {
            IsPouring = true;
            MarkDirty(true);
        }
    }

    private void OnTickPourExpiry(float dt)
    {
        if (!IsPouring) return;
        if (Api.World.ElapsedMilliseconds < pourExpiryMs) return;
        IsPouring = false;
        MarkDirty(true);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (Api is ICoreClientAPI capi)
        {
            int id = capi.World.BlockAccessor.GetBlock(Pos)?.Id ?? -1;
            if (id != builtForBlockId)
            {
                baseMesh = null;
                liquidMesh = null;
                BuildMeshes(capi);
                builtForBlockId = id;
            }
        }
        if (baseMesh == null) return false;
        mesher.AddMeshData(baseMesh);
        if (IsPouring && liquidMesh != null) mesher.AddMeshData(liquidMesh);
        return true;
    }

    private void BuildMeshes(ICoreClientAPI capi)
    {
        var block = capi.World.BlockAccessor.GetBlock(Pos);
        if (block?.Shape?.Base == null) return;

        var shapePath = block.Shape.Base.Clone()
            .WithPathAppendixOnce(".json")
            .WithPathPrefixOnce("shapes/");
        var shape = Shape.TryGet(capi, shapePath);
        if (shape?.Elements == null) return;

        var allNames = shape.Elements.Select(e => e.Name).Where(n => n != null).ToArray();
        var baseNames = allNames.Where(n => !n.StartsWith("liquid_")).ToArray();
        var liquidNames = allNames.Where(n => n.StartsWith("liquid_")).ToArray();

        var rotation = new Vec3f(block.Shape.rotateX, block.Shape.rotateY, block.Shape.rotateZ);

        capi.Tesselator.TesselateShape(
            typeForLogging: block.Code.ToString(),
            shapeBase: shape,
            modeldata: out baseMesh,
            texSource: capi.Tesselator.GetTextureSource(block),
            meshRotationDeg: rotation,
            generalGlowLevel: 0,
            climateColorMapId: 0,
            seasonColorMapId: 0,
            quantityElements: null,
            selectiveElements: baseNames);

        if (liquidNames.Length > 0)
        {
            capi.Tesselator.TesselateShape(
                typeForLogging: block.Code.ToString() + "-liquid",
                shapeBase: shape,
                modeldata: out liquidMesh,
                texSource: capi.Tesselator.GetTextureSource(block),
                meshRotationDeg: rotation,
                generalGlowLevel: 255,
                climateColorMapId: 0,
                seasonColorMapId: 0,
                quantityElements: null,
                selectiveElements: liquidNames);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        runnerFacing = tree.GetInt("runnerFacing", -1);
        currentVariant = tree.GetString("currentVariant", "n");
        bool wasPouring = IsPouring;
        IsPouring = tree.GetBool("isPouring", false);
        if (wasPouring != IsPouring && Api?.Side == EnumAppSide.Client)
        {
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("runnerFacing", runnerFacing);
        tree.SetString("currentVariant", currentVariant);
        tree.SetBool("isPouring", IsPouring);
    }
}
