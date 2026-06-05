using Scada.Core.Models;

namespace Scada.Core.Interfaces;

public interface IDriver : IDisposable
{
    DriverType DriverType { get; }
    bool IsConnected { get; }
    
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    
    Task<Tag> ReadTagAsync(string address, DataType dataType, CancellationToken cancellationToken = default);
    Task WriteTagAsync(string address, object value, DataType dataType, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<Tag>> ReadMultipleTagsAsync(IEnumerable<(string address, DataType dataType)> tags, CancellationToken cancellationToken = default);
    
    event EventHandler<TagValueEventArgs>? TagValueChanged;
    event EventHandler<DriverStatusEventArgs>? ConnectionStatusChanged;
}

public class TagValueEventArgs : EventArgs
{
    public string Address { get; set; } = string.Empty;
    public object? Value { get; set; }
    public DateTime Timestamp { get; set; }
    public TagQuality Quality { get; set; }
}

public class DriverStatusEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? Message { get; set; }
}
