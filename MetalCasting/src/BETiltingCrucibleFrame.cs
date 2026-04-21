using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace MetalCasting;

public class BETiltingCrucibleFrame : BlockEntity
{
    private ItemStack bowlStack;

    public ItemStack Bowl => bowlStack;
    public bool HasBowl => bowlStack != null;

    public bool TryInsertCrucible(IPlayer byPlayer)
    {
        if (HasBowl) return false;

        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack == null) return false;
        if (!IsLargeCrucible(slot.Itemstack)) return false;

        bowlStack = slot.Itemstack.Clone();
        bowlStack.StackSize = 1;
        slot.TakeOut(1);
        slot.MarkDirty();
        MarkDirty(true);
        return true;
    }

    public void DropContents()
    {
        if (bowlStack == null || Api == null) return;
        Api.World.SpawnItemEntity(bowlStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        bowlStack = null;
    }

    private static bool IsLargeCrucible(ItemStack stack)
    {
        var path = stack?.Collectible?.Code?.Path;
        return path != null && path.StartsWith("largecrucible-");
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        bowlStack = tree.GetItemstack("bowl");
        bowlStack?.ResolveBlockOrItem(world);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (bowlStack != null) tree.SetItemstack("bowl", bowlStack);
    }
}
