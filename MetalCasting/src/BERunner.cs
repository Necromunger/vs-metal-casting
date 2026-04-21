using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class BERunner : BlockEntity
{
    // Index matches BlockFacing.ALLFACES: 0=N, 1=E, 2=S, 3=W
    private readonly bool[] connectedSides = new bool[4];
    private string currentVariant = "straight-ns";
    private bool isUpdating;

    public bool[] ConnectedSides => connectedSides;

    public bool IsConnected(BlockFacing side) =>
        side != null && side.Index < 4 && connectedSides[side.Index];

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
            var neighbors = new System.Collections.Generic.List<BlockPos>();
            for (int i = 0; i < 4; i++)
            {
                if (connectedSides[i])
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
                connectedSides[i] = Api.World.BlockAccessor.GetBlock(np) is BlockRunner;
            }

            UpdateVariant();
            MarkDirty(true);

            if (!notifyNeighbors) return;
            for (int i = 0; i < 4; i++)
            {
                if (!connectedSides[i]) continue;
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
        bool n = connectedSides[0];
        bool e = connectedSides[1];
        bool s = connectedSides[2];
        bool w = connectedSides[3];
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
        var bytes = tree.GetBytes("connectedSides", null);
        if (bytes != null && bytes.Length == 4)
            for (int i = 0; i < 4; i++) connectedSides[i] = bytes[i] == 1;
        currentVariant = tree.GetString("currentVariant", "straight-ns");
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var bytes = new byte[4];
        for (int i = 0; i < 4; i++) bytes[i] = (byte)(connectedSides[i] ? 1 : 0);
        tree.SetBytes("connectedSides", bytes);
        tree.SetString("currentVariant", currentVariant);
    }
}

