namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents the Stage 4 task-management landing page.
/// </summary>
public sealed class TasksViewModel : StageFivePageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TasksViewModel"/> class.
    /// </summary>
    public TasksViewModel()
        : base(
            "Tasks",
            "Create, schedule, and run local AI workflows.",
            "The task grid and editor arrive in Stage 5. Scheduling services are already connected.")
    {
    }
}
