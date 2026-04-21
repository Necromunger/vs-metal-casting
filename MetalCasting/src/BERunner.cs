using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class BERunner : BlockEntity
{
    // Index matches BlockFacing.ALLFACES: 0=N, 1=E, 2=S, 3=W
    private readonly bool[] connectedRunners = new bool[4];
    private readonly bool[] connectedSprouts = new bool[4];
    private string currentVariant = "straight-ns";
    private bool isUpdating;

    // Visual flow state
    private const int PourIdleMs = 300;
    public bool IsPouring { get; private set; }
    private long pourExpiryMs;
    private MeshData baseMesh;
    private MeshData liquidMesh;
    private int builtForBlockId = -1;

    public bool IsConnected(BlockFacing side) =>
        side != null && side.Index < 4 && (connectedRunners[side.Index] || connectedSprouts[side.Index]);

    public bool IsConnectedToRunner(BlockFacing side) =>
        side != null && side.Index < 4 && connectedRunners[side.Index];

    public bool IsConnectedToSprout(BlockFacing side) =>
        side != null && side.Index < 4 && connectedSprouts[side.Index];

    public IEnumerable<BlockPos> GetConnectedSprouts()
    {
        for (int i = 0; i < 4; i++)
            if (connectedSprouts[i]) yield return Pos.AddCopy(BlockFacing.ALLFACES[i]);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            MetalCastingModSystem.Instance?.NetworkManager?.AddRunner(Pos);
            UpdateConnections(true);
            RegisterGameTickListener(OnTickPourExpiry, 100);
        }
    }

    public void BeginFlow()
    {
        if (Api == null) return;
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

        var rotation = new Vec3f(
            block.Shape.rotateX,
            block.Shape.rotateY,
            block.Shape.rotateZ);

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

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        UpdateConnections(true);
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server)
        {
            var neighbors = new List<BlockPos>();
            for (int i = 0; i < 4; i++)
            {
                if (connectedRunners[i])
                    neighbors.Add(Pos.AddCopy(BlockFacing.ALLFACES[i]));
            }

            MetalCastingModSystem.Instance?.NetworkManager?.RemoveRunner(Pos);
            base.OnBlockRemoved();

            foreach (var np in neighbors)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(np) is BERunner nbe)
                    nbe.UpdateConnections(false);
            }
            return;
        }
        base.OnBlockRemoved();
    }

    public void UpdateConnections(bool notifyNeighbors)
    {
        if (isUpdating || Api == null || Api.Side != EnumAppSide.Server) return;
        isUpdating = true;
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var np = Pos.AddCopy(BlockFacing.ALLFACES[i]);
                var nb = Api.World.BlockAccessor.GetBlock(np);
                connectedRunners[i] = nb is BlockRunner;
                connectedSprouts[i] = !connectedRunners[i] && nb is BlockSprout;
            }
            Api.Logger.Notification($"[MC] Runner {Pos} N={(connectedRunners[0]?"R":connectedSprouts[0]?"S":".")}E={(connectedRunners[1]?"R":connectedSprouts[1]?"S":".")}S={(connectedRunners[2]?"R":connectedSprouts[2]?"S":".")}W={(connectedRunners[3]?"R":connectedSprouts[3]?"S":".")}");

            UpdateVariant();
            MarkDirty(true);

            if (!notifyNeighbors) return;
            for (int i = 0; i < 4; i++)
            {
                if (!connectedRunners[i]) continue;
                var np = Pos.AddCopy(BlockFacing.ALLFACES[i]);
                if (Api.World.BlockAccessor.GetBlockEntity(np) is BERunner nbe)
                    nbe.UpdateConnections(false);
            }
        }
        finally
        {
            isUpdating = false;
        }
    }

    private void UpdateVariant()
    {
        string newVariant = DetermineVariant();
        if (newVariant == currentVariant) return;

        var current = Api.World.BlockAccessor.GetBlock(Pos);
        if (current == null) return;

        int partsToRemove = 0;
        foreach (var v in current.Variant.Values)
            partsToRemove += v.Split('-').Length;

        string[] pathParts = current.Code.Path.Split('-');
        if (pathParts.Length <= partsToRemove) return;
        string basePath = string.Join("-", pathParts.Take(pathParts.Length - partsToRemove));

        var newCode = new AssetLocation(current.Code.Domain, $"{basePath}-{newVariant}");
        var newBlock = Api.World.GetBlock(newCode);
        if (newBlock == null || newBlock.Id == current.Id) return;

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

    private string DetermineVariant()
    {
        bool n = connectedRunners[0] || connectedSprouts[0];
        bool e = connectedRunners[1] || connectedSprouts[1];
        bool s = connectedRunners[2] || connectedSprouts[2];
        bool w = connectedRunners[3] || connectedSprouts[3];
        int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);

        switch (count)
        {
            case 0:
                return "straight-ns";
            case 1:
                return (n || s) ? "straight-ns" : "straight-ew";
            case 2:
                if (n && s) return "straight-ns";
                if (e && w) return "straight-ew";
                if (n && e) return "corner-ne";
                if (e && s) return "corner-se";
                if (s && w) return "corner-sw";
                if (n && w) return "corner-nw";
                return "straight-ns";
            case 3:
                // tee-<dir> = stem direction (opposite of missing side)
                if (!w) return "tee-e";
                if (!e) return "tee-w";
                if (!s) return "tee-n";
                if (!n) return "tee-s";
                return "cross";
            case 4:
                return "cross";
            default:
                return "straight-ns";
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        var runnerBytes = tree.GetBytes("connectedRunners", null);
        if (runnerBytes != null && runnerBytes.Length == 4)
            for (int i = 0; i < 4; i++) connectedRunners[i] = runnerBytes[i] == 1;
        var moldBytes = tree.GetBytes("connectedSprouts", null);
        if (moldBytes != null && moldBytes.Length == 4)
            for (int i = 0; i < 4; i++) connectedSprouts[i] = moldBytes[i] == 1;
        currentVariant = tree.GetString("currentVariant", "straight-ns");
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
        var runnerBytes = new byte[4];
        var moldBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            runnerBytes[i] = (byte)(connectedRunners[i] ? 1 : 0);
            moldBytes[i] = (byte)(connectedSprouts[i] ? 1 : 0);
        }
        tree.SetBytes("connectedRunners", runnerBytes);
        tree.SetBytes("connectedSprouts", moldBytes);
        tree.SetString("currentVariant", currentVariant);
        tree.SetBool("isPouring", IsPouring);
    }
}
