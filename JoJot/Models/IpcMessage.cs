using System.Text.Json.Serialization;

namespace JoJot.Models;

/// <summary>
/// Base IPC message type with JSON polymorphic serialization.
/// The "action" property serves as the type discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ActivateCommand), "activate")]
[JsonDerivedType(typeof(NewTabCommand), "new-tab")]
[JsonDerivedType(typeof(ShowDesktopCommand), "show-desktop")]
public abstract record IpcMessage;

/// <summary>
/// Activates the existing JoJot window and brings it to the foreground.
/// </summary>
public sealed record ActivateCommand : IpcMessage;

/// <summary>
/// Opens a new note tab, optionally pre-filling content on the specified virtual desktop.
/// </summary>
public sealed record NewTabCommand(string? InitialContent = null, string? DesktopGuid = null) : IpcMessage;

/// <summary>
/// Switches the JoJot window to the notes for the specified virtual desktop.
/// </summary>
public sealed record ShowDesktopCommand(string DesktopGuid) : IpcMessage;

/// <summary>
/// Source-generated JSON serializer context for IPC message types.
/// Enables AOT-friendly serialization via System.Text.Json source generation.
/// </summary>
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(ActivateCommand))]
[JsonSerializable(typeof(NewTabCommand))]
[JsonSerializable(typeof(ShowDesktopCommand))]
public partial class IpcMessageContext : JsonSerializerContext { }
