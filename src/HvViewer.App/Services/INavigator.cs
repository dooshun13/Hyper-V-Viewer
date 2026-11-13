using HvViewer.Core.Model;

namespace HvViewer.App.Services;

/// <summary>A screen that can be told to show a query (the Events screen).</summary>
public interface IQueryTarget
{
    void LoadQuery(EventQuery query);
}

/// <summary>Lets screens ask the shell to switch screens.</summary>
public interface INavigator
{
    /// <summary>Show a screen by key: home, events, saved or settings.</summary>
    void Show(string key);

    /// <summary>Open the Events screen and run this query.</summary>
    void ShowEvents(EventQuery query);
}
