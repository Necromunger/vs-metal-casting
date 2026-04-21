using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting;

public class BERunner : BlockEntity
{
    // Index matches BlockFacing.ALLFACES: 0=N, 1=E, 2=S, 3=W
    private readonly bool[] connectedRunners = new bool[4];
    private readonly bool[] connectedMolds = new bool[4];
    private string currentVariant = "straight-ns";
    private bool isUpdating;

    public bool IsConnected(BlockFacing side) =>
        side != null && side.Index < 4 && (connectedRunners[side.Index] || connectedMolds[side.Index]);

    public bool IsConnectedToRunner(BlockFacing side) =>
        side != null && side.Index < 4 && connectedRunners[side.Index];

    public bool IsConnectedToMold(BlockFacing side) =>
        side != null && side.Index < 4 && connectedMolds[side.Index];

    public IEnumerable<BlockPos> GetConnectedMolds()
    {
        for (int i = 0; i < 4; i++)
            if (connectedMolds[i]) yield return Pos.AddCopy(BlockFacing.ALLFACES[i]);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;
        MetalCastingModSystem.Instance?.NetworkManager?.AddRunner(Pos);
        UpdateConnections(true);
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

                if (connectedRunners[i] = nb is BlockRunner)
                    continue;

                connectedMolds[i] = IsMoldBlock(nb);
            }

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

    private static bool IsMoldBlock(Block block) => block is BlockIngotMold || block is BlockToolMold;

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
        bool n = connectedRunners[0] || connectedMolds[0];
        bool e = connectedRunners[1] || connectedMolds[1];
        bool s = connectedRunners[2] || connectedMolds[2];
        bool w = connectedRunners[3] || connectedMolds[3];
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
        var moldBytes = tree.GetBytes("connectedMolds", null);
        if (moldBytes != null && moldBytes.Length == 4)
            for (int i = 0; i < 4; i++) connectedMolds[i] = moldBytes[i] == 1;
        currentVariant = tree.GetString("currentVariant", "straight-ns");
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var runnerBytes = new byte[4];
        var moldBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            runnerBytes[i] = (byte)(connectedRunners[i] ? 1 : 0);
            moldBytes[i] = (byte)(connectedMolds[i] ? 1 : 0);
        }
        tree.SetBytes("connectedRunners", runnerBytes);
        tree.SetBytes("connectedMolds", moldBytes);
        tree.SetString("currentVariant", currentVariant);
    }
}
