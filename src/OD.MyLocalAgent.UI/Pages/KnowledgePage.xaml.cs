using System.Windows.Controls;
using System.Windows.Input;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>Displays embedded Knowledge library administration and diagnostics.</summary>
public partial class KnowledgePage : UserControl
{
    /// <summary>Initializes the Knowledge page.</summary>
    public KnowledgePage()=>InitializeComponent();

    private void OnExistingTagMouseDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (DataContext is not KnowledgeViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.SelectedExistingTag) ||
            !viewModel.AddSelectedExistingTagCommand.CanExecute(null))
        {
            return;
        }

        viewModel.AddSelectedExistingTagCommand.Execute(null);
        eventArgs.Handled = true;
    }
}
