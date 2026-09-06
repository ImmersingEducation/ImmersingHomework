using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class HomeworkContentInput : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkContentInput>();

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HomeworkContentInput, string>(nameof(Text), "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private string? _templateName;
    private readonly List<string> _templateParameters = [];
    private bool _isFreeTextMode = true;
    private bool _suppressTemplateRadioRebuild;

    public HomeworkContentInput()
    {
        _logger.Debug("HomeworkContentInput 初始化");
        InitializeComponent();
        BuildTemplateRadios();

        AppSettings.Instance.HomeworkTemplates.CollectionChanged += OnTemplatesChanged;

        FreeTextBox.TextChanged += (_, _) =>
        {
            if (_isFreeTextMode)
                SyncTextProperty();
        };
    }

    private void OnTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressTemplateRadioRebuild)
            BuildTemplateRadios();
    }

    private void BuildTemplateRadios()
    {
        TemplateRadioPanel.Children.Clear();

        var noneRadio = new RadioButton
        {
            Content = "无"
        };
        noneRadio.IsCheckedChanged += (sender, _) =>
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
                SwitchToFreeTextMode();
        };
        TemplateRadioPanel.Children.Add(noneRadio);

        foreach (var template in AppSettings.Instance.HomeworkTemplates)
        {
            var radio = new RadioButton
            {
                Content = template
            };
            radio.IsCheckedChanged += (sender, _) =>
            {
                if (sender is RadioButton rb && rb.IsChecked == true)
                    SwitchToTemplateMode(template);
            };
            TemplateRadioPanel.Children.Add(radio);
        }

        if (_isFreeTextMode)
            noneRadio.IsChecked = true;
        else if (_templateName != null)
        {
            foreach (var child in TemplateRadioPanel.Children)
            {
                if (child is RadioButton rb && rb.Content?.ToString() == _templateName)
                {
                    rb.IsChecked = true;
                    break;
                }
            }
        }
    }

    private void SwitchToFreeTextMode()
    {
        _logger.Debug("切换到自由文本模式");
        _isFreeTextMode = true;
        _templateName = null;
        _templateParameters.Clear();
        FreeTextBox.IsVisible = true;
        TemplatePanel.IsVisible = false;
        SyncTextProperty();
    }

    private void SwitchToTemplateMode(string templateName)
    {
        _logger.Debug("切换到模板模式: {Template}", templateName);
        _isFreeTextMode = false;
        _templateName = templateName;
        _templateParameters.Clear();

        var paramCount = CountParameters(templateName);
        for (var i = 0; i < paramCount; i++)
            _templateParameters.Add("");

        FreeTextBox.IsVisible = false;
        TemplatePanel.IsVisible = true;
        BuildParameterInputs();
        UpdateTemplatePreview();
        SyncTextProperty();
    }

    private void BuildParameterInputs()
    {
        ParameterInputsPanel.Children.Clear();

        if (_templateName == null)
            return;

        var paramCount = CountParameters(_templateName);
        for (var i = 0; i < paramCount; i++)
        {
            var index = i;
            var label = new TextBlock
            {
                Text = $"参数 {i + 1}：",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var textBox = new TextBox
            {
                Text = _templateParameters.Count > i ? _templateParameters[i] : "",
                PlaceholderText = $"请输入参数 {i + 1}",
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            textBox.TextChanged += (_, _) =>
            {
                if (_isFreeTextMode || index >= _templateParameters.Count)
                    return;
                _templateParameters[index] = textBox.Text ?? "";
                UpdateTemplatePreview();
                SyncTextProperty();
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { label, textBox }
            };
            ParameterInputsPanel.Children.Add(row);
        }
    }

    private void UpdateTemplatePreview()
    {
        TemplatePreviewText.Text = GetRenderedTemplate();
    }

    private string GetRenderedTemplate()
    {
        if (_templateName == null)
            return "";

        var result = _templateName;
        var paramIndex = 0;
        while (result.Contains("[]", StringComparison.Ordinal) && paramIndex < _templateParameters.Count)
        {
            var pos = result.IndexOf("[]", StringComparison.Ordinal);
            result = string.Concat(result.AsSpan(0, pos), _templateParameters[paramIndex], result.AsSpan(pos + 2));
            paramIndex++;
        }

        return result;
    }

    private void SyncTextProperty()
    {
        var content = _isFreeTextMode ? (FreeTextBox.Text ?? "") : GetRenderedTemplate();
        SetValue(TextProperty, content);
    }

    public string GetContent()
    {
        return _isFreeTextMode
            ? (FreeTextBox.Text?.Trim() ?? "")
            : GetRenderedTemplate().Trim();
    }

    public void SetHomeworkContent(string content, string? templateName, List<string>? parameters)
    {
        _logger.Debug("设置作业内容, TemplateName: {TemplateName}, FreeText: {IsFreeText}",
            templateName, templateName == null);

        if (!string.IsNullOrEmpty(templateName) && parameters != null)
        {
            _isFreeTextMode = false;
            _templateName = templateName;
            _templateParameters.Clear();
            _templateParameters.AddRange(parameters);

            _suppressTemplateRadioRebuild = true;
            foreach (var child in TemplateRadioPanel.Children)
            {
                if (child is RadioButton rb && rb.Content?.ToString() == templateName)
                {
                    rb.IsChecked = true;
                    break;
                }
            }
            _suppressTemplateRadioRebuild = false;

            FreeTextBox.IsVisible = false;
            TemplatePanel.IsVisible = true;
            BuildParameterInputs();
            UpdateTemplatePreview();
        }
        else
        {
            _isFreeTextMode = true;
            _templateName = null;
            _templateParameters.Clear();

            _suppressTemplateRadioRebuild = true;
            foreach (var child in TemplateRadioPanel.Children)
            {
                if (child is RadioButton rb && rb.Content?.ToString() == "无")
                {
                    rb.IsChecked = true;
                    break;
                }
            }
            _suppressTemplateRadioRebuild = false;

            FreeTextBox.Text = content;
            FreeTextBox.IsVisible = true;
            TemplatePanel.IsVisible = false;
        }

        SyncTextProperty();
    }

    public string? GetTemplateName() => _templateName;

    public List<string>? GetTemplateParameters() =>
        _isFreeTextMode ? null : new List<string>(_templateParameters);

    private static int CountParameters(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("[]", index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += 2;
        }

        return count;
    }
}
