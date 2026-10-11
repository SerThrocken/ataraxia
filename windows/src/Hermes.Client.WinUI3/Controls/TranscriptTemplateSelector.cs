using Hermes.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Controls;

public sealed class TranscriptTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ApprovalTemplate { get; set; }
    public DataTemplate? ToolTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        UserMessageItem => UserTemplate,
        AssistantMessageItem => AssistantTemplate,
        ApprovalItem => ApprovalTemplate,
        ToolMessageItem => ToolTemplate,
        _ => base.SelectTemplateCore(item),
    };
}
