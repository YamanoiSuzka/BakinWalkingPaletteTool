using System.Windows.Input;

namespace BakinWalkingPaletteTool.ViewModels;

public sealed class ParameterizedRelayCommand<T>(Action<T> execute) : ICommand
    where T : class
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            execute(value);
        }
    }
}

