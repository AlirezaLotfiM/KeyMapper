using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace KeyMapper
{
    public sealed record LocalAiModelOption(
        string Id,
        string DisplayName,
        string Description,
        string FileName,
        string DownloadUrl,
        long DownloadBytes,
        int SuggestedRamGb)
    {
        public string DownloadSize =>
            DownloadBytes >= 1_000_000_000
                ? $"{DownloadBytes / 1_000_000_000d:0.##} GB download"
                : $"{DownloadBytes / 1_000_000d:0} MB download";
    }

    public sealed record LocalAiDownloadProgress(
        string ModelId,
        long BytesReceived,
        long TotalBytes)
    {
        public int Percentage =>
            TotalBytes <= 0
                ? 0
                : (int)Math.Clamp(BytesReceived * 100L / TotalBytes, 0, 100);
    }

    /// <summary>
    /// Downloads and runs an optional GGUF conversation model entirely on the
    /// user's computer. Models are deliberately not bundled with the executable:
    /// each person sees the size first and chooses whether to download one.
    /// </summary>
    public sealed class LocalAiService : IDisposable
    {
        private static readonly Lazy<LocalAiService> LazyInstance =
            new(() => new LocalAiService());

        public static LocalAiService Instance => LazyInstance.Value;

        public static IReadOnlyList<LocalAiModelOption> Models { get; } =
            new[]
            {
                new LocalAiModelOption(
                    "qwen3-0.6b-q8",
                    "Lite · Qwen3 0.6B",
                    "Fastest and smallest. Good for basic English/Persian chat on modest PCs.",
                    "Qwen3-0.6B-Q8_0.gguf",
                    "https://huggingface.co/Qwen/Qwen3-0.6B-GGUF/resolve/main/Qwen3-0.6B-Q8_0.gguf?download=true",
                    639_000_000,
                    6),
                new LocalAiModelOption(
                    "qwen3-1.7b-q8",
                    "Balanced · Qwen3 1.7B",
                    "Better conversation and personality while remaining practical on most modern PCs.",
                    "Qwen3-1.7B-Q8_0.gguf",
                    "https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q8_0.gguf?download=true",
                    1_830_000_000,
                    10),
                new LocalAiModelOption(
                    "qwen3-4b-q4",
                    "Quality · Qwen3 4B",
                    "More capable and nuanced, but slower and heavier. Best for PCs with generous memory.",
                    "Qwen3-4B-Q4_K_M.gguf",
                    "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf?download=true",
                    2_500_000_000,
                    16)
            };

        private readonly HttpClient _httpClient = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private readonly SemaphoreSlim _downloadLock = new(1, 1);
        private readonly SemaphoreSlim _inferenceLock = new(1, 1);
        private LLamaWeights? _weights;
        private ModelParams? _modelParameters;
        private string _loadedModelId = string.Empty;
        private bool _disposed;

        public string ModelsFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyMapper",
            "Models");

        private LocalAiService()
        {
        }

        public LocalAiModelOption GetRecommendedModel()
        {
            double ramGb = GetTotalMemoryBytes() / 1024d / 1024d / 1024d;
            if (ramGb >= 30 && Environment.ProcessorCount >= 12)
            {
                return Models[2];
            }

            return ramGb >= 9
                ? Models[1]
                : Models[0];
        }

        public string GetHardwareSummary()
        {
            double ramGb = GetTotalMemoryBytes() / 1024d / 1024d / 1024d;
            return $"{ramGb:0.#} GB RAM · {Environment.ProcessorCount} logical CPU threads";
        }

        public LocalAiModelOption? FindModel(string? id)
        {
            foreach (LocalAiModelOption option in Models)
            {
                if (string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        public bool IsInstalled(string? modelId)
        {
            LocalAiModelOption? model = FindModel(modelId);
            return model != null && File.Exists(GetModelPath(model));
        }

        public string GetModelPath(LocalAiModelOption model) =>
            Path.Combine(ModelsFolder, model.FileName);

        public async Task DownloadModelAsync(
            LocalAiModelOption model,
            IProgress<LocalAiDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await _downloadLock.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(ModelsFolder);
                string finalPath = GetModelPath(model);
                if (File.Exists(finalPath))
                {
                    progress?.Report(new LocalAiDownloadProgress(
                        model.Id,
                        model.DownloadBytes,
                        model.DownloadBytes));
                    return;
                }

                string partialPath = finalPath + ".partial";
                long existingLength = File.Exists(partialPath)
                    ? new FileInfo(partialPath).Length
                    : 0;

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    model.DownloadUrl);
                if (existingLength > 0)
                {
                    request.Headers.Range =
                        new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                }

                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                bool resumed =
                    response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (!resumed)
                {
                    existingLength = 0;
                }

                long responseLength =
                    response.Content.Headers.ContentLength ??
                    Math.Max(0, model.DownloadBytes - existingLength);
                long totalLength = existingLength + responseLength;

                await using Stream source =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    partialPath,
                    resumed ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    useAsync: true);

                byte[] buffer = new byte[1024 * 1024];
                long received = existingLength;
                progress?.Report(new LocalAiDownloadProgress(
                    model.Id,
                    received,
                    totalLength));
                while (true)
                {
                    int read = await source.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (read == 0) break;

                    await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                    received += read;
                    progress?.Report(new LocalAiDownloadProgress(
                        model.Id,
                        received,
                        totalLength));
                }

                await destination.FlushAsync(cancellationToken);
                File.Move(partialPath, finalPath, true);
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        public async Task<bool> RemoveModelAsync(string modelId)
        {
            LocalAiModelOption? model = FindModel(modelId);
            if (model == null) return false;

            await _inferenceLock.WaitAsync();
            try
            {
                if (string.Equals(
                    _loadedModelId,
                    modelId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    UnloadModel();
                }

                string path = GetModelPath(model);
                if (File.Exists(path)) File.Delete(path);
                string partialPath = path + ".partial";
                if (File.Exists(partialPath)) File.Delete(partialPath);
                return true;
            }
            finally
            {
                _inferenceLock.Release();
            }
        }

        public async Task<string?> GenerateAsync(
            string modelId,
            string systemPrompt,
            string prompt,
            int maxTokens,
            CancellationToken cancellationToken = default)
        {
            LocalAiModelOption? model = FindModel(modelId);
            if (model == null || !IsInstalled(modelId)) return null;

            await _inferenceLock.WaitAsync(cancellationToken);
            StatelessExecutor? executor = null;
            try
            {
                EnsureModelLoaded(model);
                if (_weights == null || _modelParameters == null) return null;

                executor = new StatelessExecutor(_weights, _modelParameters)
                {
                    SystemMessage = systemPrompt + " /no_think",
                    ApplyTemplate = true
                };

                var parameters = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new List<string>
                    {
                        "<|im_end|>",
                        "<|im_start|>user",
                        "\nUser:"
                    },
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = 0.82f,
                        TopP = 0.9f,
                        RepeatPenalty = 1.12f
                    }
                };

                var result = new StringBuilder();
                await foreach (string part in executor.InferAsync(
                    prompt,
                    parameters,
                    cancellationToken))
                {
                    result.Append(part);
                }

                return CleanGeneratedText(result.ToString());
            }
            catch
            {
                return null;
            }
            finally
            {
                executor?.Context.Dispose();
                _inferenceLock.Release();
            }
        }

        private void EnsureModelLoaded(LocalAiModelOption model)
        {
            if (_weights != null &&
                string.Equals(
                    _loadedModelId,
                    model.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UnloadModel();
            _modelParameters = new ModelParams(GetModelPath(model))
            {
                ContextSize = 4096,
                GpuLayerCount = 0,
                Threads = Math.Max(2, Math.Min(Environment.ProcessorCount - 1, 12)),
                BatchThreads = Math.Max(2, Math.Min(Environment.ProcessorCount - 1, 12)),
                BatchSize = 256,
                UseMemorymap = true
            };

            _weights = LLamaWeights.LoadFromFile(_modelParameters);
            _loadedModelId = model.Id;
        }

        private void UnloadModel()
        {
            _weights?.Dispose();
            _weights = null;
            _modelParameters = null;
            _loadedModelId = string.Empty;
        }

        private static string? CleanGeneratedText(string text)
        {
            string cleaned = Regex.Replace(
                text,
                @"<think>[\s\S]*?</think>",
                string.Empty,
                RegexOptions.IgnoreCase);
            cleaned = cleaned
                .Replace("<|im_end|>", string.Empty, StringComparison.Ordinal)
                .Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private static ulong GetTotalMemoryBytes()
        {
            var status = new MemoryStatusEx();
            return GlobalMemoryStatusEx(status)
                ? status.TotalPhysical
                : (ulong)Math.Max(
                    4L * 1024 * 1024 * 1024,
                    GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(
            [In, Out] MemoryStatusEx buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatusEx
        {
            public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnloadModel();
            _httpClient.Dispose();
            _downloadLock.Dispose();
            _inferenceLock.Dispose();
        }
    }
}
