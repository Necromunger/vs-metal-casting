using System.Linq;
using MetalCasting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MetalCasting.BlockEntities;

public class BECrucibleFurnace : BlockEntityContainer, IHeatSource
{
    public const int FuelSlotId = 0;
    private const int TickIntervalMs = 500;

    private readonly InventoryGeneric inv;

    private bool burning;
    private float fuelBurnTime;
    private float maxFuelBurnTime;
    private float maxTemperature = HeatMath.AmbientTemperature;
    private float temperature = HeatMath.AmbientTemperature;

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
            fuelBurnTime -= dt;
            if (fuelBurnTime <= 0f && !ConsumeNextFuel())
            {
                ExtinguishInternal();
            }

            if (burning)
            {
                temperature = HeatMath.ChangeTemperature(temperature, maxTemperature, dt);
            }
        }
        else
        {
            temperature = HeatMath.ChangeTemperature(temperature, HeatMath.AmbientTemperature, dt);
        }

        HeatInventoryAbove(dt);
        MarkDirty(false);
    }

    private void HeatInventoryAbove(float dt)
    {
        if (Api?.Side != EnumAppSide.Server || temperature <= HeatMath.AmbientTemperature) return;

        var abovePos = Pos.UpCopy();
        if (Api.World.BlockAccessor.GetBlockEntity(abovePos) is MetalCasting.BETiltingCrucibleFrame frame)
        {
            frame.ReceiveHeat(temperature, dt);
        }
    }

    public void Ignite()
    {
        if (burning || FuelSlot.Empty) return;
        if (!ConsumeNextFuel()) return;

        burning = true;
        SwapVariant("lit");
        MarkDirty(true);
    }

    private void ExtinguishInternal()
    {
        burning = false;
        fuelBurnTime = 0f;
        maxFuelBurnTime = 0f;
        maxTemperature = HeatMath.AmbientTemperature;
        SwapVariant("unlit");
        MarkDirty(true);
    }

    private bool ConsumeNextFuel()
    {
        if (FuelSlot.Empty) return false;

        var combust = FuelSlot.Itemstack.Collectible.GetCombustibleProperties(Api.World, FuelSlot.Itemstack, null);
        if (combust == null || combust.BurnDuration <= 0 || combust.BurnTemperature <= 0) return false;

        fuelBurnTime = maxFuelBurnTime = combust.BurnDuration;
        maxTemperature = combust.BurnTemperature;
        FuelSlot.TakeOut(1);
        FuelSlot.MarkDirty();
        return true;
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
        fuelBurnTime = tree.GetFloat("fuelBurnTime");
        maxFuelBurnTime = tree.GetFloat("maxFuelBurnTime");
        maxTemperature = tree.GetFloat("maxTemperature", HeatMath.AmbientTemperature);
        temperature = tree.GetFloat("temperature", HeatMath.AmbientTemperature);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("burning", burning);
        tree.SetFloat("fuelBurnTime", fuelBurnTime);
        tree.SetFloat("maxFuelBurnTime", maxFuelBurnTime);
        tree.SetFloat("maxTemperature", maxTemperature);
        tree.SetFloat("temperature", temperature);
    }
}
