using Vintagestory.API.Common;

namespace MetalCasting;

public class ItemSlotBowl : ItemSlotSurvival
{
    public ItemSlotBowl(InventoryBase inventory) : base(inventory) { }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot)) return false;
        return IsLargeCrucible(sourceSlot.Itemstack);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (!base.CanTakeFrom(sourceSlot, priority)) return false;
        return IsLargeCrucible(sourceSlot.Itemstack);
    }

    public override int GetRemainingSlotSpace(ItemStack forItemstack)
    {
        return Empty ? 1 : 0;
    }

    private static bool IsLargeCrucible(ItemStack stack)
    {
        var path = stack?.Collectible?.Code?.Path;
        return path != null && path.StartsWith("largecrucible-");
    }
}
