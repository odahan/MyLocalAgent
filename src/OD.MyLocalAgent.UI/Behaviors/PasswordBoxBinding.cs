using System.Windows;
using System.Windows.Controls;

namespace OD.MyLocalAgent.UI.Behaviors;

/// <summary>
/// Provides a two-way bindable password value without page code-behind.
/// </summary>
public static class PasswordBoxBinding
{
    /// <summary>
    /// Identifies the attached password property.
    /// </summary>
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached(
            "Password",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    /// <summary>
    /// Identifies the attached binding activation property.
    /// </summary>
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach",
            typeof(bool),
            typeof(PasswordBoxBinding),
            new PropertyMetadata(false, OnAttachChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBinding),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the bound password value.
    /// </summary>
    public static string GetPassword(DependencyObject target) =>
        (string)target.GetValue(PasswordProperty);

    /// <summary>
    /// Sets the bound password value.
    /// </summary>
    public static void SetPassword(DependencyObject target, string value) =>
        target.SetValue(PasswordProperty, value);

    /// <summary>
    /// Gets whether password synchronization is active.
    /// </summary>
    public static bool GetAttach(DependencyObject target) =>
        (bool)target.GetValue(AttachProperty);

    /// <summary>
    /// Sets whether password synchronization is active.
    /// </summary>
    public static void SetAttach(DependencyObject target, bool value) =>
        target.SetValue(AttachProperty, value);

    private static void OnAttachChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs e)
    {
        if (target is not PasswordBox passwordBox)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            passwordBox.PasswordChanged -= OnPasswordBoxPasswordChanged;
        }

        if ((bool)e.NewValue)
        {
            passwordBox.PasswordChanged += OnPasswordBoxPasswordChanged;
        }
    }

    private static void OnPasswordChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs e)
    {
        if (target is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= OnPasswordBoxPasswordChanged;
        if (!(bool)passwordBox.GetValue(IsUpdatingProperty))
        {
            passwordBox.Password = e.NewValue as string ?? string.Empty;
        }

        passwordBox.PasswordChanged += OnPasswordBoxPasswordChanged;
    }

    private static void OnPasswordBoxPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(IsUpdatingProperty, true);
        SetPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}
