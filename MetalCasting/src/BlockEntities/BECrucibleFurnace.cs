using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting.BlockEntities;

public class BECrucibleFurnace : BlockEntityContainer, IHeatSource
{
    public const int FuelSlotId = 0;
    private const float MaxTemp = 1300f;
    private const float HeatRatePerSec = 40f;
    private const float CoolRatePerSec = 10f;
    private const float DefaultBurnDuration = 45f;
    private const int TickIntervalMs = 500;

    private readonly InventoryGeneric inv;

    private bool burning;
    private float partialFuelConsumed;
    private float temperature = 20f;

    public override InventoryBase Inventory => inv;
    public override string InventoryClassName => "cruciblefurnace";

    public ItemSlot FuelSlot => inv[FuelSlotId];
    public bool IsBurning => burning;
    public float Temperature => temperature;

    public BECrucibleFurnace()
    {
        inv = new InventoryGeneric(1, null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inv.LateInitialize($"cruciblefurnace-{Pos}", api);
        if (api.Side == EnumAppSide.Server)
        {
            RegisterGameTickListener(OnFurnaceTick, TickIntervalMs);
        }
    }

    private void OnFurnaceTick(float dt)
    {
        if (burning)
        {
            if (FuelSlot.Empty)
            {
                ExtinguishInternal();
                return;
            }

            var combust = FuelSlot.Itemstack.Collectible.CombustibleProps;
            float burnDuration = (combust?.BurnDuration > 0) ? combust.BurnDuration : DefaultBurnDuration;

            partialFuelConsumed += dt / burnDuration;
            if (partialFuelConsumed >= 1f)
            {
                FuelSlot.TakeOut(1);
                FuelSlot.MarkDirty();
                partialFuelConsumed = 0f;
                if (FuelSlot.Empty)
                {
                    ExtinguishInternal();
                    return;
                }
            }

            temperature = Math.Min(MaxTemp, temperature + HeatRatePerSec * dt);
        }
        else
        {
            temperature = Math.Max(20f, temperature - CoolRatePerSec * dt);
        }

        MarkDirty(false);
    }

    public void Ignite()
    {
        if (burning || FuelSlot.Empty) return;
        burning = true;
        SwapVariant("lit");
        MarkDirty(true);
    }

    private void ExtinguishInternal()
    {
        burning = false;
        partialFuelConsumed = 0f;
        SwapVariant("unlit");
        MarkDirty(true);
    }

    private void SwapVariant(string newState)
    {
        if (Api == null) return;
        var current = Api.World.BlockAccessor.GetBlock(Pos);
        if (current == null) return;

        int partsToRemove = 0;
        foreach (var v in current.Variant.Values)
            partsToRemove += v.Split('-').Length;

        string[] pathParts = current.Code.Path.Split('-');
        if (pathParts.Length <= partsToRemove) return;
        string basePath = string.Join("-", pathParts.Take(pathParts.Length - partsToRemove));

        var newCode = new AssetLocation(current.Code.Domain, $"{basePath}-{newState}");
        var newBlock = Api.World.GetBlock(newCode);
        if (newBlock == null || newBlock.Id == current.Id) return;

        var tree = new TreeAttribute();
        ToTreeAttributes(tree);
        Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);
        var newBe = Api.World.BlockAccessor.GetBlockEntity(Pos);
        if (newBe != null)
        {
            newBe.FromTreeAttributes(tree, Api.World);
            newBe.MarkDirty(true);
        }
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    public bool TryAddFuel(IPlayer byPlayer)
    {
        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack == null) return false;
        if (!IsValidFuel(slot.Itemstack)) return false;

        int before = FuelSlot.StackSize;
        int moved = slot.TryPutInto(Api.World, FuelSlot, slot.StackSize);
        if (FuelSlot.StackSize > before || moved > 0)
        {
            slot.MarkDirty();
            MarkDirty(true);
            return true;
        }
        return false;
    }

    private static bool IsValidFuel(ItemStack stack)
    {
        var combust = stack?.Collectible?.CombustibleProps;
        return combust != null && combust.BurnDuration > 0;
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return burning ? 10f : 0f;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        burning = tree.GetBool("burning");
        partialFuelConsumed = tree.GetFloat("partialFuelConsumed");
        temperature = tree.GetFloat("temperature", 20f);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("burning", burning);
        tree.SetFloat("partialFuelConsumed", partialFuelConsumed);
        tree.SetFloat("temperature", temperature);
    }
}
