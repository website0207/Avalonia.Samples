using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GameDevToolkit.ViewModels;

namespace GameDevToolkit;

public class ViewLocator : IDataTemplate
{
    // todo 可优化点，只依赖一次反射
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}