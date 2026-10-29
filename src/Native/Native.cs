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

    public static bool Has(object owner, string member) =>
        DeclaredOwner(TypeOf(owner), ref member) is { } type && Find(type, member) != null;

    public static bool HasMethod(object owner, string method)
    {
        for (var current = DeclaredOwner(TypeOf(owner), ref method); current != null; current = current.BaseType)
            if (current.GetMethods(Members | BindingFlags.DeclaredOnly).Any(m => m.Name == method)) return true;
        return false;
    }

    public static T? Get<T>(object owner, string member)
    {
        if (!Walk(ref owner, ref member)) return default;
        var info = Find(TypeOf(owner), member);
        if (info == null) { Report(owner, member); return default; }
        object? value = info switch
        {
            FieldInfo field => field.GetValue(owner is Type ? null : owner),
            PropertyInfo property => property.GetValue(owner is Type ? null : owner),
            _ => null,
        };
        if (value is T typed) return typed;
        CheckType<T>(owner, member, info, value);
        return default;
    }

    public static bool TryGet<T>(object owner, string member, out T value)
    {
        if (!Walk(ref owner, ref member)) { value = default!; return false; }
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
        else CheckType<T>(owner, member, info, raw);
        return false;
    }

    /// <summary>The declared type of a field or property, for callers that adapt to a changed delegate shape.</summary>
    public static Type? MemberType(object owner, string member)
    {
        string path = member;
        switch (DeclaredOwner(TypeOf(owner), ref member) is { } type ? Find(type, member) : null)
        {
            case FieldInfo field: return field.FieldType;
            case PropertyInfo property: return property.PropertyType;
            default: Report(owner, path); return null;
        }
    }

    public static bool Set(object owner, string member, object? value)
    {
        if (!Walk(ref owner, ref member)) return false;
        switch (Find(TypeOf(owner), member))
        {
            case FieldInfo field: field.SetValue(owner is Type ? null : owner, value); return true;
            case PropertyInfo { CanWrite: true } property: property.SetValue(owner is Type ? null : owner, value); return true;
            default: Report(owner, member); return false;
        }
    }

    public static object? Call(object owner, string method, params object?[] args)
    {
        if (!Walk(ref owner, ref method)) return null;
        var info = Method(TypeOf(owner), method, args);
        if (info == null) { Report(owner, method + "/" + args.Length); return null; }
        try { return info.Invoke(owner is Type ? null : owner, Arguments(info, args)); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    public static bool TryCall(object owner, string method, out object? result, params object?[] args)
    {
        result = null;
        if (!Walk(ref owner, ref method, report: false)) return false;
        var info = Method(TypeOf(owner), method, args);
        if (info == null) return false;
        result = info.Invoke(owner is Type ? null : owner, Arguments(info, args));
        return true;
    }

    /// <summary>
    /// Adds a handler to a non-public C# event or delegate field. The subscription is recorded in
    /// <see cref="NativeHooks"/>: it ends with <see cref="Unsubscribe"/>, when <paramref name="lifetime"/> leaves the
    /// tree, or when the plugin shuts down.
    /// </summary>
    public static bool Subscribe(object owner, string member, Delegate handler, Godot.Node? lifetime = null)
    {
        object holder = owner;
        string name = member;
        if (!Walk(ref holder, ref name)) return false;
        var type = TypeOf(holder);
        var target = holder is Type ? null : holder;
        if (type.GetEvent(name, Members) is { } evt)
        {
            var add = evt.GetAddMethod(true);
            if (add == null) return false;
            add.Invoke(target, new object[] { handler });
        }
        else if (Find(type, name) is FieldInfo field && typeof(Delegate).IsAssignableFrom(field.FieldType))
            field.SetValue(target, Delegate.Combine((Delegate?)field.GetValue(target), handler));
        else
        {
            Report(holder, name);
            return false;
        }
        NativeHooks.Hold(type.Name + "." + name, () => Detach(holder, name, handler), lifetime, (owner, member, handler));
        return true;
    }

    /// <summary>Removes a handler added with <see cref="Subscribe"/>.</summary>
    public static bool Unsubscribe(object owner, string member, Delegate handler) =>
        NativeHooks.ReleaseKey((owner, member, handler)) || Detach(owner, member, handler);

    private static bool Detach(object owner, string member, Delegate handler)
    {
        if (!Walk(ref owner, ref member, report: false)) return false;
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

    public static Type? ClientType(string fullName) => typeof(WorldRoot).Assembly.GetType(fullName);

    public static Type StaticOwner(string fullName)
    {
        if (ClientType(fullName) is { } type) return type;
        if (_missing.TryAdd(fullName, 0))
        {
            GD.PushWarning($"[plugin:knightonline-ui-classic] native type unavailable: {fullName}");
            MemberMissing?.Invoke(fullName);
        }
        return typeof(UnavailableType);
    }

    private static class UnavailableType;

    public static WorldRoot? WorldOf(Node? node)
    {
        for (var current = node; current != null; current = current.GetParent())
            if (current is WorldRoot world) return world;
        return CurrentWorld;
    }

    public static WorldRoot? CurrentWorld
    {
        get
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return null;
            return Descendants(tree.Root).OfType<WorldRoot>().FirstOrDefault();
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

    /// <summary>Forgets reported members and their listeners, so a re-enabled plugin reports again.</summary>
    internal static void Reset()
    {
        _missing.Clear();
        MemberMissing = null;
    }

    private static bool Walk(ref object owner, ref string member, bool report = true)
    {
        int last = member.LastIndexOf('.');
        if (last < 0) return true;
        object start = owner;
        foreach (var step in member[..last].Split('.'))
        {
            object? next = Find(TypeOf(owner), step) switch
            {
                FieldInfo field => field.GetValue(owner is Type ? null : owner),
                PropertyInfo property => property.GetValue(owner is Type ? null : owner),
                _ => Missing,
            };
            if (ReferenceEquals(next, Missing))
            {
                if (report) Report(start, member);
                return false;
            }
            if (next == null) return false;
            owner = next;
        }
        member = member[(last + 1)..];
        return true;
    }

    private static Type? DeclaredOwner(Type type, ref string member)
    {
        int last = member.LastIndexOf('.');
        if (last < 0) return type;
        foreach (var step in member[..last].Split('.'))
        {
            Type? next = Find(type, step) switch
            {
                FieldInfo field => field.FieldType,
                PropertyInfo property => property.PropertyType,
                _ => null,
            };
            if (next == null) return null;
            type = next;
        }
        member = member[(last + 1)..];
        return type;
    }

    private static readonly object Missing = new();

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
                var named = current.GetMethods(Members | BindingFlags.DeclaredOnly).Where(m => m.Name == key.Item2).ToArray();
                var candidates = named.Where(m => m.GetParameters().Length == key.Item3).ToArray();
                if (candidates.Length == 1) return candidates[0];
                var match = candidates.FirstOrDefault(m => Accepts(m, args));
                if (match != null) return match;
                // A method that gained trailing optional parameters (3277df5: ShowItemTooltip's shopGoods) still
                // answers a call with fewer arguments; Arguments fills in the defaults.
                var optional = named.Where(m => m.GetParameters().Length > key.Item3
                    && m.GetParameters().Skip(key.Item3).All(p => p.HasDefaultValue)).ToArray();
                if (optional.Length == 1) return optional[0];
                match = optional.FirstOrDefault(m => Accepts(m, args));
                if (match != null) return match;
            }
            return null;
        });

    private static bool Accepts(MethodInfo method, object?[] args) => method.GetParameters()
        .Zip(args, (p, a) => a == null ? !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null
            : p.ParameterType.IsInstanceOfType(a)).All(ok => ok);

    /// <summary>The call arguments, padded with the defaults of the optional parameters the caller left out.</summary>
    private static object?[] Arguments(MethodInfo method, object?[] args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == args.Length) return args;
        var padded = new object?[parameters.Length];
        Array.Copy(args, padded, args.Length);
        for (int i = args.Length; i < parameters.Length; i++) padded[i] = parameters[i].DefaultValue;
        return padded;
    }

    /// <summary>
    /// Reports once when a member exists but no longer has the type the plugin reads it as (for example a
    /// container that became another container type). A null value of a compatible type is not a mismatch.
    /// </summary>
    private static void CheckType<T>(object owner, string member, MemberInfo info, object? value)
    {
        var declared = info switch { FieldInfo field => field.FieldType, PropertyInfo property => property.PropertyType, _ => null };
        if (declared == null) return;
        if (value == null && (typeof(T).IsAssignableFrom(declared) || declared.IsAssignableFrom(typeof(T))
            || Nullable.GetUnderlyingType(typeof(T)) == declared)) return;
        string key = TypeOf(owner).FullName + "." + member;
        if (!_missing.TryAdd(key + " (type)", 0)) return;
        GD.PushWarning($"[plugin:knightonline-ui-classic] native member type changed: {key} is {(value?.GetType() ?? declared).Name}, read as {typeof(T).Name}");
        MemberMissing?.Invoke(key);
    }

    private static void Report(object owner, string member)
    {
        string key = TypeOf(owner).FullName + "." + member;
        if (!_missing.TryAdd(key, 0)) return;
        GD.PushWarning($"[plugin:knightonline-ui-classic] native member unavailable: {key}");
        MemberMissing?.Invoke(key);
    }
}
