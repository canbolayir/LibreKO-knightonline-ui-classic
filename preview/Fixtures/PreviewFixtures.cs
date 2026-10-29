using System.Reflection;
using Godot;
using LibreKO;

/// <summary>
/// Preview fixtures that build native windows in a known state. They used to live in the client as
/// World.UiPreview partials; they now reach the client's internals through <see cref="Native"/> so the
/// client carries no Classic-specific fixture code. Each window group adds its own partial file.
/// </summary>
public static partial class PreviewFixtures
{
    public static T Field<T>(WorldRoot world, string name) => Target(world, ref name) is var owner && name.StartsWith(TopIconPrefix)
        ? (T)Native.Call(owner, "TopIcons.Icon", Enum.Parse(Native.ClientType("LibreKO.Features.Hud.TopIconSlot")!, name[TopIconPrefix.Length..]))!
        : Native.Get<T>(owner, name)!;
    public static void SetField(WorldRoot world, string name, object? value) => Native.Set(Target(world, ref name), name, value);
    public const string PreviewPrefix = "Preview.";
    public const string SessionPrefix = "Session.";
    public const string ConfigPrefix = "Config.";
    public const string TopIconPrefix = "TopIcons.Icon:";

    public static object Target(WorldRoot world, ref string name)
    {
        if (name.StartsWith(PreviewPrefix)) { name = name[PreviewPrefix.Length..]; return ClientPreview(world); }
        if (name.StartsWith(SessionPrefix)) { name = name[SessionPrefix.Length..]; return Game.Session; }
        if (name.StartsWith(ConfigPrefix)) { name = name[ConfigPrefix.Length..]; return Native.StaticOwner("LibreKO.Config"); }
        return world;
    }

    public static List<(Func<bool> IsOpen, Action Close, object Entry)> EscapeEntries(WorldRoot world)
    {
        var entries = new List<(Func<bool> IsOpen, Action Close, object Entry)>();
        foreach (var entry in Native.Get<Array>(world, "escapeOrder") ?? Array.Empty<object>())
        {
            if (entry == null) continue;
            Func<bool> isOpen = Native.Has(entry, "IsOpen") ? () => Native.Get<bool>(entry, "IsOpen") : () => false;
            Action close = Native.HasMethod(entry, "Close") ? () => Native.Call(entry, "Close") : () => Native.Call(entry, "CloseOnEscape");
            entries.Add((isOpen, close, entry));
        }
        return entries;
    }

    public sealed class ClientMember(string path)
    {
        public object? GetValue(object owner) { string name = path; return Native.Get<object>(owner is WorldRoot world ? Target(world, ref name) : owner, name); }
        public void SetValue(object owner, object? value) { string name = path; Native.Set(owner is WorldRoot world ? Target(world, ref name) : owner, name, value); }
    }

    public static object NetOwner(string member)
    {
        foreach (var property in Game.Session.GetType().GetProperties())
            if (property.GetValue(Game.Session) is { } slice && Native.Has(slice, member)) return slice;
        return LibreKO.Network.Net.I;
    }

    public static bool EscapeEntryFrom(object entry, System.Reflection.Assembly assembly) =>
        entry.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(f => f.GetValue(entry) is Delegate d && d.Method.DeclaringType?.Assembly == assembly);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldRoot, object> previews = new();

    public static object ClientPreview(WorldRoot world) =>
        previews.GetValue(world, owner => Activator.CreateInstance(Native.ClientType("LibreKO.WorldPreview")!, owner)!);

    public static T Prepared<T>(T world) where T : WorldRoot
    {
        Native.Call(ClientPreview(world), "Prepare");
        return world;
    }

    public static object? Call(WorldRoot world, string name, params object?[] args) => Native.Call(Target(world, ref name), name, args);
}
