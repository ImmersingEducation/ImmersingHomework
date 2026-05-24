using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ImmersingHomework.Views.WelcomePages;

public partial class LicensePage : UserControl
{
    public LicensePage()
    {
        InitializeComponent();
        var licenseText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "GPL3.0.txt"));
        LicenseTextBox.Text = licenseText;
    }
}