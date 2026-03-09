using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JoJot.ViewModels;

/// <summary>
/// Base class implementing INotifyPropertyChanged with a SetProperty helper.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises PropertyChanged for the given property name.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets the backing field and raises PropertyChanged if the value changed.
    /// Returns true if the value was changed.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Sets the backing field, raises PropertyChanged for the property and any dependent properties.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, string[] dependentProperties, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return false;

        foreach (var dep in dependentProperties)
            OnPropertyChanged(dep);

        return true;
    }
}
