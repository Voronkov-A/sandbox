using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Yap.ViewModels;

namespace Yap;

public class ViewLocator : IDataTemplate
{
    private readonly Dictionary<string, Type> _pageViewTypes;

    public ViewLocator()
    {
        _pageViewTypes = Assembly.GetAssembly(GetType())!
            .GetTypes()
            .Where(x => x.IsSubclassOf(typeof(UserControl)) && x.Name.EndsWith("PageView"))
            .ToDictionary(x => x.Name + "Model");
    }

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var type = _pageViewTypes[data.GetType().Name];
        return (Control?)Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create instance of type {type}.");
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
