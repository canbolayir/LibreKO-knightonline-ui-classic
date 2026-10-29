using KnightOnlineUiClassic.Native;

public static class WorldRootPreviewMembers
{
    extension(WorldRoot world)
    {
        public bool ItemTooltipVisible => Native.Get<bool>(world, "ItemTooltip.Visible");

        public bool StageAnvilUpgrade(int originItemId, int materialItemId, int extraItemId = 0) =>
            Native.Call(world, "Upgrade.StageAnvil", originItemId, materialItemId, extraItemId) is true;

        public void OpenLottery() => Native.Call(world, "Lottery.Open");
    }
}
