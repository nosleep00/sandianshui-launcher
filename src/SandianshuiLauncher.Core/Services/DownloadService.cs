using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;
using System.Net;
using System.Net.Http.Headers;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 多线程下载器：并发控制、SHA1 校验、断点续传、失败重试、原子落盘；
/// 大文件（≥ 8 MiB）自动分片并发下载，服务器不支持 Range 时降级单线程整文件。
/// </summary>
public class DownloadService
{
    private const int BufferSize = 81920;

    /// <summary>默认分片阈值：文件声明大小达到该值才启用分片下载。</summary>
    public const long ChunkThresholdDefault = 8 * 1024 * 1024;

    /// <summary>连接停滞超时：连续此时长读不到任何数据则判定连接挂起，触发重试。</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);

    /// <summary>慢速判定宽限期：连接开始后此时长内不做平均速度判定。</summary>
    private static readonly TimeSpan SlowCheckGracePeriod = TimeSpan.FromSeconds(15);

    /// <summary>最低平均速度（字节/秒）：宽限期后仍低于此值则放弃该源并回退。</summary>
    private const double MinAverageSpeedBytesPerSec = 20 * 1024;

    /// <summary>对多连接不友好的源（禁用分片，对齐 PCL 经验）。</summary>
    private static readonly HashSet<string> ChunkUnfriendlyHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cdn.modrinth.com",
            "mod.mcimirror.top",
            "api.curseforge.com",
            "edge.forgecdn.net",
            "mediafilez.forgecdn.net"
        };

    private readonly HttpClient _http;
    private readonly int _retryCount;
    private readonly long _chunkThreshold;

    public DownloadService(HttpClient http, int retryCount = 3, long chunkThresholdBytes = ChunkThresholdDefault)
    {
        _http = http;
        _retryCount = retryCount;
        _chunkThreshold = chunkThresholdBytes;
    }

    /// <summary>并发下载一批文件。</summary>
    public async Task DownloadAsync(
        IReadOnlyList<DownloadTask> tasks,
        int maxParallel,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (maxParallel < 1)
        {
            maxParallel = 1;
        }

        // 先筛掉已经下载且校验通过的文件。
        var pending = new List<DownloadTask>();
        long preCompletedBytes = 0;
        int completedFiles = 0;
        foreach (var task in tasks)
        {
            if (HashUtils.VerifySha1(task.DestinationPath, task.Sha1))
            {
                completedFiles++;
                preCompletedBytes += Math.Max(task.Size, 0);
            }
            else
            {
                pending.Add(task);
            }
        }

        int totalFiles = tasks.Count;
        long totalBytes = tasks.Sum(t => Math.Max(t.Size, 0));
        long liveBytes = preCompletedBytes;
        var currentFiles = new HashSet<string>();
        var currentLock = new object();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

        // 定时上报进度（无论文件大小是否已知）。
        var reporter = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                Report();
                try
                {
                    await Task.Delay(200, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);

        void Report()
        {
            string[] files;
            lock (currentLock)
            {
                files = currentFiles.Take(3).ToArray();
            }
            progress?.Report(new DownloadProgress
            {
                TotalFiles = totalFiles,
                CompletedFiles = completedFiles,
                TotalBytes = totalBytes,
                DownloadedBytes = liveBytes,
                CurrentFile = files.Length > 0 ? string.Join("、", files) : string.Empty
            });
        }

        var semaphore = new SemaphoreSlim(maxParallel);

        // 大文件按总并发数决定分片数：4 线程以上分 4 片，2 线程以上分 2 片，否则单线程。
        int chunkCount = maxParallel >= 8 ? 4 : maxParallel >= 4 ? 2 : 1;

        Task[] workers = pending.Select(async task =>
        {
            await semaphore.WaitAsync(token);
            lock (currentLock)
            {
                currentFiles.Add(task.DisplayName);
            }
            // 该任务已计入 liveBytes 的字节数。回调按"该文件磁盘累计字节"的正增量上报，
            // 重试、206 续传或 200 重下都不会重复计数。
            // 分片下载时回调会从多个并行线程触发，必须用 CAS 保证读-改-写原子，
            // 否则并发回调可能重复累加导致进度超过 100%。
            long fileReported = 0;
            try
            {
                await DownloadOneAsync(
                    task,
                    chunkCount,
                    diskTotal =>
                    {
                        while (true)
                        {
                            long seen = Interlocked.Read(ref fileReported);
                            if (diskTotal <= seen)
                            {
                                return;
                            }
                            if (Interlocked.CompareExchange(ref fileReported, diskTotal, seen) == seen)
                            {
                                Interlocked.Add(ref liveBytes, diskTotal - seen);
                                return;
                            }
                        }
                    },
                    token);
                Interlocked.Increment(ref completedFiles);
                // 完成后按文件声明大小校准，确保最终总字节精确等于 TotalBytes。
                long reported = Interlocked.Read(ref fileReported);
                if (task.Size > 0 && task.Size > reported)
                {
                    if (Interlocked.CompareExchange(ref fileReported, task.Size, reported) == reported)
                    {
                        Interlocked.Add(ref liveBytes, task.Size - reported);
                    }
                }
            }
            finally
            {
                lock (currentLock)
                {
                    currentFiles.Remove(task.DisplayName);
                }
                semaphore.Release();
            }
        }).ToArray();

        Exception? failure = null;
        try
        {
            await Task.WhenAll(workers);
        }
        catch (Exception ex)
        {
            failure = ex;
            cts.Cancel();
            throw;
        }
        finally
        {
            cts.Cancel();
            try
            {
                await reporter;
            }
            catch
            {
                // 忽略进度上报任务的取消异常。
            }
        }

        Report();
    }

    /// <summary>
    /// 下载单个文件（带续传与重试）。
    /// <paramref name="onDiskTotal"/> 报告该文件截至当前在磁盘上的累计字节数
    /// （续传时已包含之前已写入的部分），由调用方按正增量去重后汇总。
    /// </summary>
    private async Task DownloadOneAsync(
        DownloadTask task,
        int chunkCount,
        Action<long> onDiskTotal,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(task.DestinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = task.DestinationPath + ".tmp";
        IReadOnlyList<string> urls = task.AllUrls;

        Exception? lastError = null;

        for (int urlIndex = 0; urlIndex < urls.Count; urlIndex++)
        {
            string url = urls[urlIndex];
            bool isLastUrl = urlIndex == urls.Count - 1;
            // 非最后源只尝试 1 次，尽快回退到其他源；最后一个源充分重试。
            int maxAttempt = isLastUrl ? _retryCount : 0;

            for (int attempt = 0; attempt <= maxAttempt; attempt++)
            {
                try
                {
                    await TryDownloadFromUrlAsync(url, task, tempPath, chunkCount, onDiskTotal, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 用户取消，直接传播，不再回退或重试。
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < maxAttempt)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                    }
                }
            }

            // 当前源彻底失败：切换到下一个源前删除临时文件，
            // 避免把该源可能损坏的部分数据续传到下一个源、导致最终校验失败。
            if (urlIndex < urls.Count - 1)
            {
                TryDelete(tempPath);
            }
        }

        throw new InvalidDataException(
            $"下载失败：{task.DisplayName}（已尝试 {urls.Count} 个下载地址）。\n"
            + "请检查网络，或在“设置”中切换下载源后重试。" + DescribeInnerError(lastError),
            lastError);
    }

    /// <summary>把底层异常压缩成一行中文提示（不直接暴露英文原始异常给用户）。</summary>
    private static string DescribeInnerError(Exception? error)
    {
        if (error is null)
        {
            return string.Empty;
        }
        int? status = error switch
        {
            HttpRequestException { StatusCode: { } code } => (int)code,
            _ => null
        };
        if (status is not null)
        {
            return status == 404
                ? "\n原因：文件在所有下载地址上都不存在（HTTP 404），可能该模组文件已被删除或镜像未同步。"
                : $"\n原因：服务器返回 HTTP {status}。";
        }
        if (error is TaskCanceledException or TimeoutException)
        {
            return "\n原因：连接超时或被服务器中断。";
        }
        if (error is InvalidDataException)
        {
            return "\n原因：" + error.Message;
        }
        return string.Empty;
    }

    /// <summary>从单个地址尝试下载、校验并落盘；大文件优先分片，服务器不支持 Range 时自动降级。</summary>
    private async Task TryDownloadFromUrlAsync(
        string url,
        DownloadTask task,
        string tempPath,
        int chunkCount,
        Action<long> onDiskTotal,
        CancellationToken cancellationToken)
    {
        long existing = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        long? expectedTotal = null;

        if (existing > 0 && task.Size > 0 && existing >= task.Size)
        {
            // 临时文件已完整（可能是上次移动前中断），直接进入校验。
            onDiskTotal(existing);
        }
        else
        {
            // 大文件且服务器允许分片：先尝试分片并发下载；失败/不支持则回退单线程。
            if (CanChunk(url, task, chunkCount))
            {
                bool chunkedOk = false;
                try
                {
                    chunkedOk = await TryChunkedDownloadAsync(url, task, tempPath, onDiskTotal, chunkCount, cancellationToken);
                }
                catch (RangeNotSupportedException)
                {
                    TryDelete(tempPath);
                    chunkedOk = false;
                }
                if (chunkedOk)
                {
                    VerifyAndMove(task, tempPath);
                    return;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            bool resume;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                resume = true;
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                resume = false;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                resume = false;
            }

            // 206 续传在现有文件后追加，磁盘起点为 existing；200 则从头重写，起点为 0。
            bool append = resume && existing > 0;
            long diskTotal = append ? existing : 0;
            onDiskTotal(diskTotal);

            // 用 Content-Length 计算期望总大小，以便发现传输中断（即使没有 SHA1）。
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue)
            {
                expectedTotal = append ? existing + contentLength.Value : contentLength.Value;
            }

            // 停滞超时：若连续一段时间读不到任何数据，判定连接挂起并重试。
            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stallCts.CancelAfter(StallTimeout);

            // 慢速看门狗：宽限期后平均速度仍过低则放弃该源、回退到其他源。
            var speedWatch = System.Diagnostics.Stopwatch.StartNew();
            long startDisk = diskTotal;

            await using (Stream content = await response.Content.ReadAsStreamAsync(stallCts.Token))
            await using (var fileStream = new FileStream(
                tempPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize))
            {
                var buffer = new byte[BufferSize];
                int read;
                while (true)
                {
                    read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), stallCts.Token);
                    if (read == 0)
                    {
                        break;
                    }
                    // 收到数据，重置停滞计时器。
                    stallCts.CancelAfter(StallTimeout);
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), stallCts.Token);
                    diskTotal += read;
                    onDiskTotal(diskTotal);

                    // 宽限期后平均速度仍低于阈值，判定源过慢并回退。
                    double elapsed = speedWatch.Elapsed.TotalSeconds;
                    if (elapsed >= SlowCheckGracePeriod.TotalSeconds)
                    {
                        double speed = (diskTotal - startDisk) / elapsed;
                        if (speed < MinAverageSpeedBytesPerSec)
                        {
                            throw new TimeoutException(
                                $"下载速度过慢：{task.DisplayName}（平均 {speed:F0} B/s）");
                        }
                    }
                }
            }

            if (expectedTotal.HasValue && diskTotal != expectedTotal.Value)
            {
                TryDelete(tempPath);
                throw new InvalidDataException(
                    $"文件传输不完整：{task.DisplayName}（期望 {expectedTotal.Value}，实际 {diskTotal}）");
            }
        }

        VerifyAndMove(task, tempPath);
    }

    /// <summary>共享的校验 + 落盘：SHA1 校验通过后原子改名到正式路径。</summary>
    private static void VerifyAndMove(DownloadTask task, string tempPath)
    {
        if (!HashUtils.VerifySha1(tempPath, task.Sha1))
        {
            TryDelete(tempPath);
            throw new InvalidDataException($"文件 SHA1 校验失败：{task.DisplayName}");
        }
        File.Move(tempPath, task.DestinationPath, overwrite: true);
    }

    /// <summary>是否对该 URL 启用分片：声明大小达到阈值、并发数足够、且源对多连接友好。</summary>
    private bool CanChunk(string url, DownloadTask task, int chunkCount)
    {
        if (task.Size < _chunkThreshold || chunkCount < 2)
        {
            return false;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && ChunkUnfriendlyHosts.Contains(uri.Host))
        {
            return false;
        }
        return true;
    }

    /// <summary>服务器不支持 Range 请求时的内部标记异常。</summary>
    private sealed class RangeNotSupportedException : Exception
    {
    }

    /// <summary>
    /// 分片并发下载：按声明大小切分为 <paramref name="chunkCount"/> 个 Range 分片并行下载，
    /// 全部完成后按序合并到临时文件并上报总字节数。
    /// 任一分片遇到服务器不支持 Range（200/非 206）时抛出 <see cref="RangeNotSupportedException"/>
    /// 由调用方降级；其他失败抛出原异常交由上层重试/换源。
    /// </summary>
    private async Task<bool> TryChunkedDownloadAsync(
        string url,
        DownloadTask task,
        string tempPath,
        Action<long> onDiskTotal,
        int chunkCount,
        CancellationToken cancellationToken)
    {
        long total = task.Size;
        var parts = new List<(string Path, long Start, long Length)>(chunkCount);
        for (int i = 0; i < chunkCount; i++)
        {
            long start = total * i / chunkCount;
            long end = total * (i + 1) / chunkCount - 1;
            if (start > end)
            {
                continue;
            }
            parts.Add((tempPath + $".part{i}", start, end - start + 1));
        }

        try
        {
            long downloaded = 0;
            var lockObject = new object();
            Exception? failure = null;

            await Parallel.ForEachAsync(
                parts,
                new ParallelOptions { MaxDegreeOfParallelism = chunkCount, CancellationToken = cancellationToken },
                async (part, token) =>
                {
                    try
                    {
                        await DownloadPartAsync(url, part.Path, part.Start, part.Length, token);
                        long sum;
                        lock (lockObject)
                        {
                            downloaded += part.Length;
                            sum = downloaded;
                        }
                        onDiskTotal(sum);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (lockObject)
                        {
                            failure ??= ex;
                        }
                        throw;
                    }
                });

            if (failure is not null)
            {
                throw failure;
            }

            // 按序合并分片到临时文件。
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                foreach ((string partPath, _, _) in parts)
                {
                    await using var input = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                    await input.CopyToAsync(output, BufferSize, cancellationToken);
                }
            }
            DeletePartFiles(parts);
            onDiskTotal(total);
            return true;
        }
        catch (RangeNotSupportedException)
        {
            DeletePartFiles(parts);
            throw;
        }
        catch
        {
            DeletePartFiles(parts);
            throw;
        }
    }

    /// <summary>下载单个 Range 分片；服务器忽略 Range（200）或未返回 206 时标记为不支持。</summary>
    private async Task DownloadPartAsync(
        string url, string partPath, long start, long length, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            response.EnsureSuccessStatusCode();
            throw new RangeNotSupportedException();
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);
        var buffer = new byte[BufferSize];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
        }
        if (received != length)
        {
            throw new InvalidDataException($"分片下载不完整：期望 {length} 字节，实际 {received} 字节。");
        }
    }

    private static void DeletePartFiles(IEnumerable<(string Path, long Start, long Length)> parts)
    {
        foreach ((string path, _, _) in parts)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 删除失败时下次重试会覆盖。
        }
    }
}
