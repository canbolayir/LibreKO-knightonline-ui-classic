using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Validated access to non-public client members. The Classic skins depend on native controls and
/// state that the client does not publish through the plugin API; every such access goes through
/// this class so that a renamed member is reported once and the affected skin can fall back to the
/// generic Classic frame instead of failing.
/// </summary>
public static class Native
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConcurrentDictionary<(Type, string), MemberInfo?> _members = new();
    private static readonly ConcurrentDictionary<(Type, string, int), MethodInfo?> _methods = new();
    private static readonly ConcurrentDictionary<string, byte> _missing = new();

    /// <summary>Members that were requested but do not exist in the running client.</summary>
    public static IReadOnlyCollection<string> MissingMembers => _missing.Keys.ToArray();

    public static event Action<string>? MemberMissing;

    public static bool Has(object owner, string member) => Find(TypeOf(owner), member) != null;

    public static bool HasMethod(object owner, string method)
    {
        for (var current = TypeOf(owner); current != null; current = current.BaseType)
            if (current.GetMethods(Members | BindingFlags.DeclaredOnly).Any(m => m.Name == method)) return true;
        return false;
    }

    public static T? Get<T>(object owner, string member)
    {
        var info = Find(TypeOf(owner), member);
        if (info == null) { Report(owner, member); return default; }
        object? value = info switch
        {
            FieldInfo field => field.GetValue(owner is Type ? null : owner),
            PropertyInfo property => property.GetValue(owner is Type ? null : owner),
            _ => null,
        };
        return value is T typed ? typed : default;
    }

    public static bool TryGet<T>(object owner, string member, out T value)
    {
        var info = Find(TypeOf(owner), member);
        object? raw = info switch
        {
            FieldInfo field => field.GetValue(owner is Type ? null : owner),
            PropertyInfo property => property.GetValue(owner is Type ? null : owner),
            _ => null,
        };
        if (raw is T typed) { value = typed; return true; }
        value = default!;
        if (info == null) Report(owner, member);
        return false;
    }

    public static bool Set(object owner, string member, object? value)
    {
        switch (Find(TypeOf(owner), member))
        {
            case FieldInfo field: field.SetValue(owner is Type ? null : owner, value); return true;
            case PropertyInfo { CanWrite: true } property: property.SetValue(owner is Type ? null : owner, value); return true;
            default: Report(owner, member); return false;
        }
    }

    public static object? Call(object owner, string method, params object?[] args)
    {
        var info = Method(TypeOf(owner), method, args);
        if (info == null) { Report(owner, method + "/" + args.Length); return null; }
        try { return info.Invoke(owner is Type ? null : owner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    public static bool TryCall(object owner, string method, out object? result, params object?[] args)
    {
        var info = Method(TypeOf(owner), method, args);
        if (info == null) { result = null; return false; }
        result = info.Invoke(owner is Type ? null : owner, args);
        return true;
    }

    /// <summary>Adds a handler to a non-public C# event or delegate field.</summary>
    public static bool Subscribe(object owner, string member, Delegate handler)
    {
        var type = TypeOf(owner);
        var target = owner is Type ? null : owner;
        if (type.GetEvent(member, Members) is { } evt)
        {
            var add = evt.GetAddMethod(true);
            if (add == null) return false;
            add.Invoke(target, new object[] { handler });
            return true;
        }
        if (Find(type, member) is FieldInfo field && typeof(Delegate).IsAssignableFrom(field.FieldType))
        {
            field.SetValue(target, Delegate.Combine((Delegate?)field.GetValue(target), handler));
            return true;
        }
        Report(owner, member);
        return false;
    }

    /// <summary>Removes a handler added with <see cref="Subscribe"/>.</summary>
    public static bool Unsubscribe(object owner, string member, Delegate handler)
    {
        var type = TypeOf(owner);
        var target = owner is Type ? null : owner;
        if (type.GetEvent(member, Members) is { } evt)
        {
            var remove = evt.GetRemoveMethod(true);
            if (remove == null) return false;
            remove.Invoke(target, new object[] { handler });
            return true;
        }
        if (Find(type, member) is FieldInfo field && typeof(Delegate).IsAssignableFrom(field.FieldType))
        {
            field.SetValue(target, Delegate.Remove((Delegate?)field.GetValue(target), handler));
            return true;
        }
        return false;
    }

    public static Type? ClientType(string fullName) => typeof(World).Assembly.GetType(fullName);

    public static World? WorldOf(Node? node)
    {
        for (var current = node; current != null; current = current.GetParent())
            if (current is World world) return world;
        return CurrentWorld;
    }

    public static World? CurrentWorld
    {
        get
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return null;
            return Descendants(tree.Root).OfType<World>().FirstOrDefault();
        }
    }

    public static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public static HudWindow? WindowOf(Node? node)
    {
        for (var current = node; current != null; current = current.GetParent())
            if (current is HudWindow window) return window;
        return null;
    }

    private static Type TypeOf(object owner) => owner as Type ?? owner.GetType();

    private static MemberInfo? Find(Type type, string member) =>
        _members.GetOrAdd((type, member), key =>
        {
            for (var current = key.Item1; current != null; current = current.BaseType)
            {
                var field = current.GetField(key.Item2, Members | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                var property = current.GetProperty(key.Item2, Members | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        });

    private static MethodInfo? Method(Type type, string name, object?[] args) =>
        _methods.GetOrAdd((type, name, args.Length), key =>
        {
            for (var current = key.Item1; current != null; current = current.BaseType)
            {
                var candidates = current.GetMethods(Members | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == key.Item2 && m.GetParameters().Length == key.Item3).ToArray();
                if (candidates.Length == 1) return candidates[0];
                var match = candidates.FirstOrDefault(m => m.GetParameters()
                    .Zip(args, (p, a) => a == null ? !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null
                        : p.ParameterType.IsInstanceOfType(a)).All(ok => ok));
                if (match != null) return match;
            }
            return null;
        });

    private static void Report(object owner, string member)
    {
        string key = TypeOf(owner).FullName + "." + member;
        if (!_missing.TryAdd(key, 0)) return;
        GD.PushWarning($"[plugin:knightonline-ui-classic] native member unavailable: {key}");
        MemberMissing?.Invoke(key);
    }
}
