using System.Net.Sockets;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class PortMonitor
{
    public async Task<bool> IsAcceptingConnectionsAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
        {
            return false;
        }

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(700));
        try
        {
            await client.ConnectAsync("127.0.0.1", port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async Task<bool> WaitUntilAcceptingConnectionsAsync(
        int port,
        TimeSpan timeout,
        Func<bool>? abortCondition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (abortCondition?.Invoke() == true)
            {
                return false;
            }

            if (await IsAcceptingConnectionsAsync(port, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(350, remaining.TotalMilliseconds)), cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }
}

