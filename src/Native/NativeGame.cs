using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public readonly record struct GamePanelRow(string Id, string[] Cells, string Hint, Color Color, bool Online = true,
    bool Trackable = false, bool Abandonable = false, bool Claimable = false, bool Tracked = false);

/// <summary>Character-window presentation over the client's existing services.</summary>
public interface IGameCharacterPanel
{
    string SelectedPage { get; }
    string RaceName { get; }
    string JobName { get; }
    string LevelLabel { get; }
    string TitleName { get; }
    MyClanInfo Clan { get; }
    int QuestFilter { get; }
    int QuestKind { get; }
    IReadOnlyList<Window> Dialogs { get; }
    int StatBonus(int row);
    IReadOnlyList<GamePanelRow> Rows(string section);
    string Status(string section);
    void SelectPage(string page);
    void Refresh(string section);
    void Act(string action, string selection = "", string value = "");
}

/// <summary>An isolated model factory for a single cached dialog portrait.</summary>
public sealed record GameNpcPortrait(string AppearanceKey, string Name, int Level, Func<Node3D?> BuildModel);

/// <summary>
/// Game services the Classic skins need beyond the client's public plugin API. The default source
/// reads the running client; the preview harness substitutes its own fixture data.
/// </summary>
public interface INativeGame
{
    IGameCharacterPanel? CharacterPanel { get; }
    GameNpcPortrait? NpcPortrait { get; }
    int NotificationCount(string id);

    event Action<int, string>? ItemLinkRequested;
    event Action<string>? ChannelRequested;
    IReadOnlyList<string> ReadHistory(int mask, bool timestamps, string colors);
    bool LinkInventoryItem(int slot);
    void ShowLinkTooltip(int itemId);
    void HideLinkTooltip();
    void PlayerMenu(string name, Vector2 at);
    IReadOnlyList<NearbyRow> NearbyPlayers();

    int SelectedAbs { get; }
    void Select(int abs);

    bool Running { get; }
    bool Sitting { get; }
    bool AutoAttacking { get; }
    void ToggleRun();
    void ToggleAttack();
    void TurnCamera();
    void OpenGameMenu();

    bool MiniMapVisible { get; }

    void MoveAmount(int from, int to, int count);
    int TransferToInventorySlot(int from);
    void ConfirmDrop(int slot, int itemId);
}

public static class NativeGame
{
    private static INativeGame? _source;

    /// <summary>The active source; the preview replaces it with fixture data.</summary>
    public static INativeGame Source
    {
        get => _source ??= new ClientGame();
        set => _source = value;
    }
}

public static class NativeGameExtensions
{
    extension(IGameWindows windows)
    {
        public IGameCharacterPanel? CharacterPanel => NativeGame.Source.CharacterPanel;
        public GameNpcPortrait? NpcPortrait => NativeGame.Source.NpcPortrait;
        public int NotificationCount(string id) => NativeGame.Source.NotificationCount(id);
    }

    extension(IGameChat chat)
    {
        public IReadOnlyList<string> ReadHistory(int mask, bool timestamps, string colors) => NativeGame.Source.ReadHistory(mask, timestamps, colors);
        public bool LinkInventoryItem(int slot) => NativeGame.Source.LinkInventoryItem(slot);
        public void ShowLinkTooltip(int itemId) => NativeGame.Source.ShowLinkTooltip(itemId);
        public void HideLinkTooltip() => NativeGame.Source.HideLinkTooltip();
        public void PlayerMenu(string name, Vector2 at) => NativeGame.Source.PlayerMenu(name, at);
        public IReadOnlyList<NearbyRow> NearbyPlayers() => NativeGame.Source.NearbyPlayers();
    }

    extension(IGameHotbar hotbar)
    {
        public int SelectedAbs => NativeGame.Source.SelectedAbs;
        public void Select(int abs) => NativeGame.Source.Select(abs);
    }

    extension(IGameCommands commands)
    {
        public bool Running => NativeGame.Source.Running;
        public bool Sitting => NativeGame.Source.Sitting;
        public bool AutoAttacking => NativeGame.Source.AutoAttacking;
        public void ToggleRun() => NativeGame.Source.ToggleRun();
        public void ToggleAttack() => NativeGame.Source.ToggleAttack();
        public void TurnCamera() => NativeGame.Source.TurnCamera();
        public void OpenGameMenu() => NativeGame.Source.OpenGameMenu();
    }

    extension(IGameMap map)
    {
        public bool MiniMapVisible => NativeGame.Source.MiniMapVisible;
    }

    extension(IGameInventory inventory)
    {
        public void MoveAmount(int from, int to, int count) => NativeGame.Source.MoveAmount(from, to, count);
        public int TransferToInventorySlot(int from) => NativeGame.Source.TransferToInventorySlot(from);
        public void ConfirmDrop(int slot, int itemId) => NativeGame.Source.ConfirmDrop(slot, itemId);
    }
}

/// <summary>
/// Reads the running client. Members fall back to the behaviour the client's public API already
/// offers when the corresponding native support is absent.
/// </summary>
public partial class ClientGame : INativeGame
{
    private static PluginGame Game => Plugin.Kit.Game;

    public virtual IGameCharacterPanel? CharacterPanel => CharacterPanelSource;
    public virtual GameNpcPortrait? NpcPortrait => NpcPortraitSource;
    public virtual int NotificationCount(string id) => NativeHudChat.NotificationCount(id);

    public event Action<int, string>? ItemLinkRequested;
    public event Action<string>? ChannelRequested;
    protected void RaiseItemLink(int id, string name) => ItemLinkRequested?.Invoke(id, name);
    protected void RaiseChannel(string prefix) => ChannelRequested?.Invoke(prefix);

    public virtual IReadOnlyList<string> ReadHistory(int mask, bool timestamps, string colors) => NativeChat.ReadHistory(mask, timestamps, colors);

    public virtual bool LinkInventoryItem(int slot)
    {
        if (NativeChat.LinkInventoryItem(slot) is not { } link) return false;
        RaiseItemLink(link.Id, link.Name);
        return true;
    }

    public virtual void ShowLinkTooltip(int itemId) => NativeChat.ShowLinkTooltip(itemId);
    public virtual void HideLinkTooltip() => NativeChat.HideLinkTooltip();
    public virtual void PlayerMenu(string name, Vector2 at) => NativeChat.PlayerMenu(name, at);
    public virtual IReadOnlyList<NearbyRow> NearbyPlayers() => NativeChat.NearbyPlayers();

    public virtual int SelectedAbs => NativeHudChat.SelectedAbs;
    public virtual void Select(int abs) => NativeHudChat.Select(abs);

    public virtual bool Running => NativeHudChat.Running;
    public virtual bool Sitting => NativeHudChat.Sitting;
    public virtual bool AutoAttacking => NativeHudChat.AutoAttacking;
    public virtual void ToggleRun() => NativeHudChat.ToggleRun();
    public virtual void ToggleAttack() => NativeHudChat.ToggleAttack();
    public virtual void TurnCamera() => NativeHudChat.TurnCamera();
    public virtual void OpenGameMenu() => NativeHudChat.OpenGameMenu();

    public virtual bool MiniMapVisible => NativeHudChat.MiniMapVisible;

    public virtual void MoveAmount(int from, int to, int count) => NativeInventory.MoveAmount(from, to, count);

    public virtual int TransferToInventorySlot(int from) => NativeInventory.TransferToInventorySlot(from);

    public virtual void ConfirmDrop(int slot, int itemId) => NativeInventory.ConfirmDrop(slot, itemId);
}
