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
    public static T Field<T>(World world, string name) => Native.Get<T>(world, name)!;
    public static void SetField(World world, string name, object? value) => Native.Set(world, name, value);
    public static object? Call(World world, string name, params object?[] args) => Native.Call(world, name, args);
}
