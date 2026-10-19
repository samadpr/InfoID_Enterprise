using Renci.SshNet;
using System;
using System.Net;
using System.Net.Sockets;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class MySqlSshTunnelService : IDisposable
{
    private SshClient? _sshClient;
    private ForwardedPortLocal? _forwardedPort;

    public uint LocalPort { get; private set; }

    public void StartPasswordTunnel(
        string sshHost,
        string sshUsername,
        string sshPassword,
        string mysqlHost,
        uint mysqlPort)
    {
        Stop();

        var authentication =
            new PasswordAuthenticationMethod(
                sshUsername,
                sshPassword);

        var connectionInfo =
            new ConnectionInfo(
                sshHost,
                22,
                sshUsername,
                authentication);

        _sshClient =
            new SshClient(connectionInfo);

        _sshClient.Connect();

        if (!_sshClient.IsConnected)
        {
            throw new InvalidOperationException(
                "Could not connect to the SSH server.");
        }

        LocalPort =
            GetAvailableLocalPort();

        _forwardedPort =
            new ForwardedPortLocal(
                "127.0.0.1",
                LocalPort,
                mysqlHost,
                mysqlPort);

        _sshClient.AddForwardedPort(
            _forwardedPort);

        _forwardedPort.Start();

        if (!_forwardedPort.IsStarted)
        {
            throw new InvalidOperationException(
                "Could not start the SSH tunnel.");
        }
    }


    public void StartKeyFileTunnel(
    string sshHost,
    string sshUsername,
    string sshKeyFile,
    string mysqlHost,
    uint mysqlPort)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(sshKeyFile))
        {
            throw new ArgumentException(
                "SSH key file is required.");
        }

        var keyFile =
            new PrivateKeyFile(sshKeyFile);

        var authentication =
            new PrivateKeyAuthenticationMethod(
                sshUsername,
                keyFile);

        var connectionInfo =
            new ConnectionInfo(
                sshHost,
                22,
                sshUsername,
                authentication);

        _sshClient =
            new SshClient(connectionInfo);

        _sshClient.Connect();

        if (!_sshClient.IsConnected)
        {
            throw new InvalidOperationException(
                "Could not connect to the SSH server.");
        }

        LocalPort =
            GetAvailableLocalPort();

        _forwardedPort =
            new ForwardedPortLocal(
                "127.0.0.1",
                LocalPort,
                mysqlHost,
                mysqlPort);

        _sshClient.AddForwardedPort(
            _forwardedPort);

        _forwardedPort.Start();

        if (!_forwardedPort.IsStarted)
        {
            throw new InvalidOperationException(
                "Could not start the SSH tunnel.");
        }
    }
    private static uint GetAvailableLocalPort()
    {
        using var listener =
            new TcpListener(
                IPAddress.Loopback,
                0);

        listener.Start();

        var endpoint =
            (IPEndPoint)listener.LocalEndpoint;

        return (uint)endpoint.Port;
    }

    public void Stop()
    {
        if (_forwardedPort is not null)
        {
            if (_forwardedPort.IsStarted)
            {
                _forwardedPort.Stop();
            }

            _forwardedPort = null;
        }

        if (_sshClient is not null)
        {
            if (_sshClient.IsConnected)
            {
                _sshClient.Disconnect();
            }

            _sshClient.Dispose();
            _sshClient = null;
        }

        LocalPort = 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
