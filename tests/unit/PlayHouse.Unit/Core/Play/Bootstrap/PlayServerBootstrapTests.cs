#nullable enable

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Discovery;
using Xunit;

namespace PlayHouse.Unit.Core.Play.Bootstrap;

/// <summary>
/// PlayServerBootstrap TLS 설정 테스트
/// </summary>
public class PlayServerBootstrapTests : IDisposable
{
    private readonly string _testCertPath;
    private readonly string _testCertPassword = "test1234";
    private X509Certificate2? _testCert;

    public PlayServerBootstrapTests()
    {
        // 테스트용 인증서 파일 생성
        _testCertPath = Path.Combine(Path.GetTempPath(), $"test-cert-{Guid.NewGuid()}.pfx");
        _testCert = CreateSelfSignedCertificate();
        File.WriteAllBytes(_testCertPath, _testCert.Export(X509ContentType.Pfx, _testCertPassword));
    }

    public void Dispose()
    {
        _testCert?.Dispose();
        if (File.Exists(_testCertPath))
        {
            File.Delete(_testCertPath);
        }
    }

    #region Helper Methods

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    private class TestStage : IStage
    {
        public IStageLink StageLink { get; private set; } = null!;
        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet) =>
            Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("TestReply")));
        public Task OnPostCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    private class TestActor : IActor
    {
        public IActorLink ActorLink { get; private set; } = null!;
        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket) => Task.FromResult<(bool, IPacket?)>((true, null));
        public Task<long> OnCheckStage() => Task.FromResult(0L);
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    private class TestSystemController : ISystemController
    {
        public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
        {
            return Task.FromResult<IReadOnlyList<IServerInfo>>(new List<IServerInfo> { serverInfo });
        }
    }

    private PlayServerBootstrap CreateBootstrap()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        return new PlayServerBootstrap()
            .Configure(opts =>
            {
                opts.ServerType = ServerType.Play;
                opts.AuthenticateMessageId = "Auth";
                opts.TcpPort = null; // 기본 비활성화
            })
            .UseLoggerFactory(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseServiceProvider(serviceProvider)
            .UseSystemController<TestSystemController>();
    }

    #endregion

    #region TCP TLS Tests

    [Fact(DisplayName = "ConfigureTcpWithTls - 인증서 객체로 설정한다")]
    public void ConfigureTcpWithTls_WithCertificate_SetsCertificate()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When
        bootstrap.ConfigureTcpWithTls(6000, _testCert!);

        // Then - Build 시 예외 없이 성공해야 함
        bootstrap.UseStage<TestStage, TestActor>("TestStage");
        var act = () => bootstrap.Build();
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "ConfigureTcpWithTls - 파일 경로와 비밀번호로 설정한다")]
    public void ConfigureTcpWithTls_WithFilePathAndPassword_LoadsCertificate()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When
        bootstrap.ConfigureTcpWithTls(6000, _testCertPath, _testCertPassword);

        // Then - Build 시 예외 없이 성공해야 함
        bootstrap.UseStage<TestStage, TestActor>("TestStage");
        var act = () => bootstrap.Build();
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "ConfigureTcpWithTls - 잘못된 파일 경로는 예외를 발생시킨다")]
    public void ConfigureTcpWithTls_WithInvalidPath_ThrowsException()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When & Then
        var act = () => bootstrap.ConfigureTcpWithTls(6000, "/invalid/path/cert.pfx", "password");
        act.Should().Throw<Exception>();
    }

    #endregion

    #region WebSocket TLS Tests

    [Fact(DisplayName = "ConfigureWebSocketWithTls - 인증서 객체로 설정한다")]
    public void ConfigureWebSocketWithTls_WithCertificate_SetsCertificate()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When
        bootstrap.ConfigureWebSocketWithTls("/ws", _testCert!);

        // Then - Build 시 예외 없이 성공해야 함
        bootstrap.UseStage<TestStage, TestActor>("TestStage");
        var act = () => bootstrap.Build();
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "ConfigureWebSocketWithTls - 파일 경로와 비밀번호로 설정한다")]
    public void ConfigureWebSocketWithTls_WithFilePathAndPassword_LoadsCertificate()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When
        bootstrap.ConfigureWebSocketWithTls("/ws", _testCertPath, _testCertPassword);

        // Then - Build 시 예외 없이 성공해야 함
        bootstrap.UseStage<TestStage, TestActor>("TestStage");
        var act = () => bootstrap.Build();
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "ConfigureWebSocketWithTls - 잘못된 파일 경로는 예외를 발생시킨다")]
    public void ConfigureWebSocketWithTls_WithInvalidPath_ThrowsException()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When & Then
        var act = () => bootstrap.ConfigureWebSocketWithTls("/ws", "/invalid/path/cert.pfx", "password");
        act.Should().Throw<Exception>();
    }

    #endregion

    #region Combined TLS Tests

    [Fact(DisplayName = "TCP와 WebSocket 모두 TLS를 설정할 수 있다")]
    public void ConfigureBothTcpAndWebSocketWithTls_Succeeds()
    {
        // Given
        var bootstrap = CreateBootstrap();

        // When
        bootstrap
            .ConfigureTcpWithTls(6000, _testCert!)
            .ConfigureWebSocketWithTls("/ws", _testCert!);

        // Then - Build 시 예외 없이 성공해야 함
        bootstrap.UseStage<TestStage, TestActor>("TestStage");
        var act = () => bootstrap.Build();
        act.Should().NotThrow();
    }

    #endregion
}
