namespace DovahLink.Host.Identity;

/// <summary>Supplies the current operating-system computer name for Host display metadata.</summary>
public interface IHostMachineNameProvider
{
    /// <summary>Reads the current operating-system computer name.</summary>
    /// <returns>The OS-provided computer name.</returns>
    string GetMachineName();
}

/// <inheritdoc cref="IHostMachineNameProvider"/>
public sealed class SystemHostMachineNameProvider : IHostMachineNameProvider
{
    /// <inheritdoc/>
    public string GetMachineName() => Environment.MachineName;
}
