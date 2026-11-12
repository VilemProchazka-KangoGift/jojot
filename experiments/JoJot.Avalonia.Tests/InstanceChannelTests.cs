using JoJot.AvaloniaExperiment.Platform;
using JoJot.Models;
using System.IO.Pipes;
using Xunit;

namespace JoJot.Avalonia.Tests;

public sealed class InstanceChannelTests
{
    [Fact]
    public void Second_instance_delivers_desktop_to_primary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "JoJot-ipc-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
        using var primary = new InstanceChannel(directory);
        Assert.True(primary.IsPrimary);
        using var received = new CountdownEvent(3);
        var actual = new System.Collections.Concurrent.ConcurrentQueue<IpcMessage>();
        primary.Start(value => { actual.Enqueue(value); received.Signal(); });
        using var secondary = new InstanceChannel(directory);
        Assert.False(secondary.IsPrimary);
        var desktop = Guid.NewGuid().ToString();
        var first = secondary.Send(new ActivateCommand());
        Assert.True(first == InstanceChannel.SendResult.Delivered, secondary.LastError);
        Assert.Equal(InstanceChannel.SendResult.Delivered, secondary.Send(new NewTabCommand("prefilled", desktop)));
        Assert.Equal(InstanceChannel.SendResult.Delivered, secondary.Send(new ShowDesktopCommand(desktop)));
        Assert.True(received.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal<IpcMessage>([new ActivateCommand(), new NewTabCommand("prefilled", desktop), new ShowDesktopCommand(desktop)], actual.ToArray());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Zombie_age_guard_requires_both_wait_and_process_age()
    {
        var now = DateTime.UtcNow;
        Assert.False(InstanceChannel.IsEligibleOwner(now.AddSeconds(-11), now, TimeSpan.FromSeconds(4)));
        Assert.False(InstanceChannel.IsEligibleOwner(now.AddSeconds(-9), now, TimeSpan.FromSeconds(6)));
        Assert.True(InstanceChannel.IsEligibleOwner(now.AddSeconds(-10), now, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Owner_identity_requires_actual_exclusive_holder_and_same_executable()
    {
        Assert.True(InstanceChannel.IdentityMatches(12, 42, @"P:\app\JoJot.Avalonia.exe", [12, 99], 99, @"P:\app\JoJot.Avalonia.exe"));
        Assert.False(InstanceChannel.IdentityMatches(12, 42, @"P:\app\JoJot.Avalonia.exe", [99], 99, @"P:\app\JoJot.Avalonia.exe"));
        Assert.False(InstanceChannel.IdentityMatches(12, 42, @"P:\app\JoJot.Avalonia.exe", [12, 99, 13], 99, @"P:\app\JoJot.Avalonia.exe"));
        Assert.False(InstanceChannel.IdentityMatches(12, 42, @"P:\other.exe", [12, 99], 99, @"P:\app\JoJot.Avalonia.exe"));
    }

    [Fact]
    public void Idle_client_is_disconnected_so_later_commands_can_be_delivered()
    {
        var directory = Path.Combine(Path.GetTempPath(), "JoJot-ipc-idle-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var primary = new InstanceChannel(directory);
            using var received = new ManualResetEventSlim();
            primary.Start(_ => received.Set());
            using var idle = new NamedPipeClientStream(".", primary.PipeNameForTests, PipeDirection.Out);
            idle.Connect(5000);
            var sent = InstanceChannel.SendResult.Failed;
            Exception? senderError = null;
            var sender = new Thread(() =>
            {
                try
                {
                    using var secondary = new InstanceChannel(directory);
                    sent = secondary.Send(new ActivateCommand());
                }
                catch (Exception ex) { senderError = ex; }
            });
            sender.Start();
            Assert.True(sender.Join(TimeSpan.FromSeconds(30)));
            Assert.Null(senderError);
            Assert.Equal(InstanceChannel.SendResult.Delivered, sent);
            Assert.True(received.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
