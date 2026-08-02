using System.Windows;
using System.Windows.Controls;

namespace OD.MyLocalAgent.UI.Behaviors;

/// <summary>
/// Exposes caret and selection positions of a <see cref="TextBox"/> as two-way bindings
/// so no insertion logic is required in page code-behind.
/// </summary>
public static class TextBoxSelectionBinding
{
    /// <summary>
    /// Identifies the attached caret index property.
    /// </summary>
    public static readonly DependencyProperty CaretIndexProperty =
        DependencyProperty.RegisterAttached(
            "CaretIndex",
            typeof(int),
            typeof(TextBoxSelectionBinding),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCaretIndexChanged));

    /// <summary>
    /// Identifies the attached selection start property.
    /// </summary>
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.RegisterAttached(
            "SelectionStart",
            typeof(int),
            typeof(TextBoxSelectionBinding),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectionStartChanged));

    /// <summary>
    /// Identifies the attached selection length property.
    /// </summary>
    public static readonly DependencyProperty SelectionLengthProperty =
        DependencyProperty.RegisterAttached(
            "SelectionLength",
            typeof(int),
            typeof(TextBoxSelectionBinding),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectionLengthChanged));

    /// <summary>
    /// Identifies the attached binding activation property.
    /// </summary>
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach",
            typeof(bool),
            typeof(TextBoxSelectionBinding),
            new PropertyMetadata(false, OnAttachChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(TextBoxSelectionBinding),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the bound caret index.
    /// </summary>
    public static int GetCaretIndex(DependencyObject target) =>
        (int)target.GetValue(CaretIndexProperty);

    /// <summary>
    /// Sets the bound caret index.
    /// </summary>
    public static void SetCaretIndex(DependencyObject target, int value) =>
        target.SetValue(CaretIndexProperty, value);

    /// <summary>
    /// Gets the bound selection start.
    /// </summary>
    public static int GetSelectionStart(DependencyObject target) =>
        (int)target.GetValue(SelectionStartProperty);

    /// <summary>
    /// Sets the bound selection start.
    /// </summary>
    public static void SetSelectionStart(DependencyObject target, int value) =>
        target.SetValue(SelectionStartProperty, value);

    /// <summary>
    /// Gets the bound selection length.
    /// </summary>
    public static int GetSelectionLength(DependencyObject target) =>
        (int)target.GetValue(SelectionLengthProperty);

    /// <summary>
    /// Sets the bound selection length.
    /// </summary>
    public static void SetSelectionLength(DependencyObject target, int value) =>
        target.SetValue(SelectionLengthProperty, value);

    /// <summary>
    /// Gets whether caret synchronization is active.
    /// </summary>
    public static bool GetAttach(DependencyObject target) =>
        (bool)target.GetValue(AttachProperty);

    /// <summary>
    /// Sets whether caret synchronization is active.
    /// </summary>
    public static void SetAttach(DependencyObject target, bool value) =>
        target.SetValue(AttachProperty, value);

    private static void OnAttachChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBox textBox)
        {
            return;
        }

        textBox.SelectionChanged -= OnSelectionChanged;
        if (args.NewValue is true)
        {
            textBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (sender is not TextBox textBox || IsUpdating(textBox))
        {
            return;
        }

        SetUpdating(textBox, true);
        try
        {
            SetCaretIndex(textBox, textBox.CaretIndex);
            SetSelectionStart(textBox, textBox.SelectionStart);
            SetSelectionLength(textBox, textBox.SelectionLength);
        }
        finally
        {
            SetUpdating(textBox, false);
        }
    }

    private static void OnCaretIndexChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args) =>
        ApplyToTextBox(
            target,
            (textBox, value) => textBox.CaretIndex = value,
            args.NewValue);

    private static void OnSelectionStartChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args) =>
        ApplyToTextBox(
            target,
            (textBox, value) => textBox.SelectionStart = value,
            args.NewValue);

    private static void OnSelectionLengthChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args) =>
        ApplyToTextBox(
            target,
            (textBox, value) => textBox.SelectionLength = value,
            args.NewValue);

    private static void ApplyToTextBox(
        DependencyObject target,
        Action<TextBox, int> apply,
        object? newValue)
    {
        if (target is not TextBox textBox || IsUpdating(textBox) || newValue is not int value)
        {
            return;
        }

        SetUpdating(textBox, true);
        try
        {
            apply(textBox, Math.Clamp(value, 0, textBox.Text.Length));
        }
        finally
        {
            SetUpdating(textBox, false);
        }
    }

    private static bool IsUpdating(DependencyObject target) =>
        (bool)target.GetValue(IsUpdatingProperty);

    private static void SetUpdating(DependencyObject target, bool value) =>
        target.SetValue(IsUpdatingProperty, value);
}
