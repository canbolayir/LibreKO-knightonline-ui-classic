using Godot;

public partial class Preview
{
    private async Task FinishManagedAudit()
    {
        // Release managed resource wrappers while the engine is still servicing disposal queues.
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }
}
