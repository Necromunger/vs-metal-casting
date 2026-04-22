using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class GuiDialogTiltingCrucibleFrame : GuiDialogBlockEntity
{
    private readonly BETiltingCrucibleFrame be;
    private readonly BlockPos pos;

    private int[] oreSlotIds = [
        BETiltingCrucibleFrame.OreSlotStart + 0,
        BETiltingCrucibleFrame.OreSlotStart + 1,
        BETiltingCrucibleFrame.OreSlotStart + 2,
        BETiltingCrucibleFrame.OreSlotStart + 3,
        BETiltingCrucibleFrame.OreSlotStart + 4,
        BETiltingCrucibleFrame.OreSlotStart + 5,
        BETiltingCrucibleFrame.OreSlotStart + 6,
        BETiltingCrucibleFrame.OreSlotStart + 7,
        BETiltingCrucibleFrame.OreSlotStart + 8,
        BETiltingCrucibleFrame.OreSlotStart + 9,
        BETiltingCrucibleFrame.OreSlotStart + 10,
        BETiltingCrucibleFrame.OreSlotStart + 11,
    ];

    public GuiDialogTiltingCrucibleFrame(BETiltingCrucibleFrame be, BlockPos pos, ICoreClientAPI capi)
        : base("Tilting Crucible", be.Inventory, pos, capi)
    {
        this.be = be;
        this.pos = pos;
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        RebuildLayout();
    }

    public void RebuildLayout()
    {
        bool showOreGrid = be.HasBowl && !be.BowlHasLiquid;

        const int oreCols = 4;
        const int oreRows = 3;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        ElementBounds oreGridBounds = null;
        ElementBounds bowlSlotBounds;

        if (showOreGrid)
        {
            oreGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, oreCols, oreRows);
            bowlSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, 1).FixedUnder(oreGridBounds, 10);
        }
        else
        {
            bowlSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
        }

        var composer = capi.Gui
            .CreateCompo("tiltingcruciblecompo-" + pos, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Tilting Crucible", OnTitleBarClose)
            .BeginChildElements(bgBounds);

        if (showOreGrid)
            composer.AddItemSlotGrid(Inventory, DoSendPacket, oreCols, oreSlotIds, oreGridBounds, "oreslots");

        composer.AddItemSlotGrid(
            Inventory,
            DoSendPacket,
            1,
            [BETiltingCrucibleFrame.BowlSlotId],
            bowlSlotBounds,
            "bowlslot"
        );

        composer.EndChildElements();
        SingleComposer = composer.Compose();
    }

    private void DoSendPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(pos.X, pos.InternalY, pos.Z, p);
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }
}
