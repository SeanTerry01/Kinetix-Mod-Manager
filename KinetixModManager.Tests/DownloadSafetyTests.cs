using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Real transfers over a socket on this machine, because what matters is what is left on disk afterwards.</summary>
public sealed class DownloadSafetyTests : IDisposable
{
	private static readonly byte[] Body = Encoding.ASCII.GetBytes(new string('x', 200_000));

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "KinetixDownloadTests_" + Path.GetRandomFileName());
	private readonly TcpListener _server = new(IPAddress.Loopback, 0);
	private readonly NexusService _service = new(new AppSettings());

	public DownloadSafetyTests()
	{
		Directory.CreateDirectory(_folder);
		_server.Start();
	}

	public void Dispose()
	{
		_server.Stop();
		Directory.Delete(_folder, true);
	}

	private string Url => $"http://127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}/file.zip";
	private string Destination => Path.Combine(_folder, "file.zip");
	private string Partial => Destination + ModArchive.PartialDownloadSuffix;

	/// <summary>Answers one request. With <paramref name="holdOpen"/>, sends half the body and then goes quiet.</summary>
	private async Task ServeAsync(bool holdOpen, TaskCompletionSource? halfSent = null)
	{
		using TcpClient client = await _server.AcceptTcpClientAsync();
		using NetworkStream stream = client.GetStream();

		// The whole of the request's headers, which need not arrive in one piece.
		var request = new byte[4096];
		int received = 0;
		while (received < request.Length && !Encoding.ASCII.GetString(request, 0, received).Contains("\r\n\r\n"))
		{
			int read = await stream.ReadAsync(request.AsMemory(received));
			if (read == 0) break;
			received += read;
		}

		byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n");
		await stream.WriteAsync(head);

		if (!holdOpen)
		{
			await stream.WriteAsync(Body);
			return;
		}

		await stream.WriteAsync(Body.AsMemory(0, Body.Length / 2));
		await stream.FlushAsync();
		halfSent?.SetResult();
		// Until the other end gives up, which is a reset rather than a clean close.
		try { while (await stream.ReadAsync(request) > 0) { } }
		catch (IOException) { }
	}

	private static string DigestOf(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));

	[Fact]
	public async Task AMatchingFileArrivesUnderItsRealName()
	{
		Task server = ServeAsync(holdOpen: false);
		await _service.DownloadFileWithProgressAsync(Url, Destination, null, DigestOf(Body));
		await server;

		Assert.Equal(Body, File.ReadAllBytes(Destination));
		Assert.False(File.Exists(Partial));
	}

	[Fact]
	public async Task AFileThatDoesNotMatchIsNeverLeftToBeRun()
	{
		Task server = ServeAsync(holdOpen: false);

		await Assert.ThrowsAsync<DownloadMismatchException>(() =>
			_service.DownloadFileWithProgressAsync(Url, Destination, null, DigestOf(new byte[] { 1, 2, 3 })));
		await server;

		Assert.False(File.Exists(Destination));
		Assert.False(File.Exists(Partial));
	}

	[Fact]
	public async Task AnEarlierCopyIsKeptWhenTheNewOneDoesNotMatch()
	{
		await File.WriteAllTextAsync(Destination, "the one that worked");
		Task server = ServeAsync(holdOpen: false);

		await Assert.ThrowsAsync<DownloadMismatchException>(() =>
			_service.DownloadFileWithProgressAsync(Url, Destination, null, DigestOf(new byte[] { 1, 2, 3 })));
		await server;

		Assert.Equal("the one that worked", await File.ReadAllTextAsync(Destination));
	}

	[Fact]
	public async Task NoPublishedChecksumStillDownloads()
	{
		Task server = ServeAsync(holdOpen: false);
		await _service.DownloadFileWithProgressAsync(Url, Destination);
		await server;

		Assert.True(File.Exists(Destination));
	}

	[Fact]
	public async Task CancellingStopsATransferAndLeavesNothing()
	{
		var halfSent = new TaskCompletionSource();
		Task server = ServeAsync(holdOpen: true, halfSent);

		Task download = _service.DownloadFileWithProgressAsync(Url, Destination);
		await halfSent.Task;
		Assert.True(_service.AnyDownloadRunning);

		_service.CancelDownloads();

		await Assert.ThrowsAsync<DownloadCancelledException>(() => download);
		Assert.False(_service.AnyDownloadRunning);
		Assert.False(File.Exists(Destination));
		Assert.False(File.Exists(Partial));
		await server;
	}

	[Fact]
	public async Task ADownloadAfterACancelIsNotCancelled()
	{
		_service.CancelDownloads();

		Task server = ServeAsync(holdOpen: false);
		await _service.DownloadFileWithProgressAsync(Url, Destination);
		await server;

		Assert.True(File.Exists(Destination));
	}

	[Theory]
	[InlineData(null, DigestResult.NotPublished)]
	[InlineData("", DigestResult.NotPublished)]
	[InlineData("md5:abc", DigestResult.NotPublished)]
	[InlineData("sha256:tooshort", DigestResult.NotPublished)]
	public void AChecksumThatCannotBeReadSaysNothingEitherWay(string? digest, DigestResult expected)
	{
		string path = Path.Combine(_folder, "any.bin");
		File.WriteAllText(path, "x");
		Assert.Equal(expected, DownloadDigest.Check(path, digest));
	}

	[Fact]
	public void ChecksumCaseDoesNotMatter()
	{
		string path = Path.Combine(_folder, "any.bin");
		File.WriteAllBytes(path, Body);

		Assert.Equal(DigestResult.Matches, DownloadDigest.Check(path, DigestOf(Body).ToLowerInvariant()));
	}

	[Fact]
	public void AReleaseCarriesItsAssetChecksums()
	{
		GitHubRelease? release = GitHubReleases.Parse("""
		{ "tag_name": "v1", "assets": [
		  { "name": "a.zip", "browser_download_url": "https://example.test/a.zip", "size": 1, "digest": "sha256:abc" },
		  { "name": "b.zip", "browser_download_url": "https://example.test/b.zip", "size": 1 }
		] }
		""");

		Assert.Equal("sha256:abc", release!.Assets[0].Digest);
		Assert.Null(release.Assets[1].Digest);
	}
}
