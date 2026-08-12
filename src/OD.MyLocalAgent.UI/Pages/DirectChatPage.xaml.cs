using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>Displays persisted Direct Chat conversations and streamed responses.</summary>
public partial class DirectChatPage : UserControl
{
    private DirectChatViewModel? _viewModel;

    /// <summary>Initializes the Direct Chat page.</summary>
    public DirectChatPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        _viewModel = e.NewValue as DirectChatViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in _viewModel.Messages)
        {
            message.PropertyChanged += OnMessagePropertyChanged;
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e) =>
        DetachViewModel();

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DirectChatMessageItem message in e.OldItems)
            {
                message.PropertyChanged -= OnMessagePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DirectChatMessageItem message in e.NewItems)
            {
                message.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        QueueScrollToEnd();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DirectChatMessageItem.Content))
        {
            QueueScrollToEnd();
        }
    }

    private void QueueScrollToEnd() =>
        _ = Dispatcher.BeginInvoke(
            MessagesScrollViewer.ScrollToEnd,
            DispatcherPriority.Background);

    private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            DataContext is DirectChatViewModel viewModel &&
            viewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendCommand.Execute(null);
        }
    }

    private void OnDraftTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (DataContext is DirectChatViewModel viewModel)
            {
                viewModel.DraftMessage = textBox.Text;
                viewModel.RefreshComposerCommands();
            }
        }
    }

    private void OnDraftGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is DirectChatViewModel viewModel)
        {
            viewModel.RefreshComposerCommands();
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        foreach (var message in _viewModel.Messages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        _viewModel = null;
    }
}
