using CommunityToolkit.Mvvm.ComponentModel;

namespace Rox.App.ViewModels;

/// <summary>Base for a navigable page view-model.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; } // Segoe Fluent Icons glyph

    /// <summary>Called when the page becomes active (e.g. refresh from the current session).</summary>
    public virtual void OnActivated() { }
}
