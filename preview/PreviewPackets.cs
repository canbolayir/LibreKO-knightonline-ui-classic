using System.Reflection;
using LibreKO.Network;

public static class PreviewPackets
{
    private static object? dispatcher;
    private static MethodInfo? dispatch;
    private static GameSession? builtFor;

    public static void Receive(Packet packet)
    {
        if (dispatcher == null || !ReferenceEquals(builtFor, Game.Session))
        {
            var client = typeof(Net).Assembly;
            var dispatcherType = client.GetType("LibreKO.Network.PacketDispatcher", throwOnError: true)!;
            dispatcher = Activator.CreateInstance(dispatcherType, nonPublic: true)!;
            dispatch = dispatcherType.GetMethod("Dispatch")!;
            var register = client.GetType("LibreKO.Network.Readers.PacketReaders", throwOnError: true)!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!;
            // e5f0e420 readers also take the frame clock (Time.GetTicksMsec), as Net registers them.
            object?[] args = register.GetParameters().Length == 4
                ? [dispatcher, Game.Session, Game.Requests, Delegate.CreateDelegate(register.GetParameters()[3].ParameterType, typeof(Godot.Time).GetMethod(nameof(Godot.Time.GetTicksMsec))!)]
                : [dispatcher, Game.Session, Game.Requests];
            register.Invoke(null, args);
            builtFor = Game.Session;
        }
        if (!(bool)dispatch!.Invoke(dispatcher, [packet])!)
            throw new InvalidOperationException($"No reader takes opcode 0x{packet.GetOpcode():X2}");
    }
}
