using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using ImageEncoder = System.Drawing.Imaging.Encoder;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class RemotePreviewFrames
    {
        private readonly object gate = new();
        private long leaseUntil;
        private long nextCaptureAt;
        private long currentRequest;
        private long frameRequest;
        private long serial;
        private int epoch;
        private bool busy;
        private CancellationTokenSource? operation;
        private string image = string.Empty;
        private string error = string.Empty;

        public string Read(string status)
        {
            lock (gate)
            {
                leaseUntil = Environment.TickCount64 + 2000;
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    requestId = frameRequest,
                    serial,
                    image,
                    error,
                    status = JsonSerializer.Deserialize<JsonElement>(status)
                });
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                epoch++;
                image = string.Empty;
                error = string.Empty;
                frameRequest = 0;
                currentRequest = 0;
                leaseUntil = 0;
                nextCaptureAt = 0;
                _ = operation?.CancelAsync();
            }
        }

        public void Update(ITextureReadbackProvider reader, nint srv, int width, int height, long request, bool ready)
        {
            lock (gate)
            {
                if (request != currentRequest)
                {
                    epoch++;
                    currentRequest = request;
                    image = string.Empty;
                    error = string.Empty;
                    frameRequest = 0;
                    _ = operation?.CancelAsync();
                }

                var now = Environment.TickCount64;
                if (!ready || srv == 0 || busy || now > leaseUntil || now < nextCaptureAt || width < 1 || height < 1 || width > 4096 || height > 4096)
                    return;
                var wrap = new RetainedSRV(srv, width, height);
                var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                operation = cancellation;
                busy = true;
                nextCaptureAt = now + 30;
                _ = Capture(reader, wrap, request, epoch, cancellation);
            }
        }

        private async Task Capture(ITextureReadbackProvider reader, RetainedSRV wrap, long request, int ticket, CancellationTokenSource cancellation)
        {
            try
            {
                var token = cancellation.Token;
                (RawImageSpecification Specification, byte[] RawData) raw;
                using (wrap)
                {
                    raw = await reader.GetRawImageAsync(wrap, new TextureModificationArgs
                    {
                        MakeOpaque = false,
                        NewWidth = 0,
                        NewHeight = 0
                    }, true, token).ConfigureAwait(false);
                }

                var encoded = await Task.Run(() => Encode(raw.Specification, raw.RawData, token), token).ConfigureAwait(false);
                var data = "data:image/jpeg;base64," + Convert.ToBase64String(encoded);
                lock (gate)
                {
                    if (ticket == epoch && request == currentRequest)
                    {
                        image = data;
                        frameRequest = request;
                        error = string.Empty;
                        serial++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    if (ticket == epoch)
                        error = "预览读回超时，正在重试";
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    if (ticket == epoch)
                        error = $"预览读取失败：{exception.Message}";
                }
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(operation, cancellation))
                        operation = null;
                    busy = false;
                }

                cancellation.Dispose();
            }
        }

        internal static byte[] Encode(RawImageSpecification spec, byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var width = spec.Width;
            var height = spec.Height;
            var pitch = spec.Pitch;
            var format = spec.DxgiFormat;
            if (width < 1 || height < 1 || width > 4096 || height > 4096 || pitch < width * 4 || pitch > 32768 || (long)pitch * height > bytes.Length)
                throw new InvalidDataException("预览像素尺寸无效");
            if (format is not (28 or 29 or 87 or 91 or 88 or 93))
                throw new InvalidDataException($"预览像素格式暂不支持：{format}");
            var swap = format is 28 or 29;
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var i = y * pitch + x * 4;
                    if (swap)
                        (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
                    bytes[i + 3] = 255;
                }
            }

            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                using var original = new Bitmap(width, height, pitch, PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
                var scale = Math.Min(1, 720d / height);
                using var output = new Bitmap(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(output))
                {
                    graphics.InterpolationMode = InterpolationMode.Bilinear;
                    graphics.DrawImage(original, 0, 0, output.Width, output.Height);
                }

                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream();
                using var quality = new EncoderParameters(1);
                quality.Param[0] = new EncoderParameter(ImageEncoder.Quality, 68L);
                output.Save(stream, ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid), quality);
                if (stream.Length > 512 * 1024)
                    throw new InvalidDataException("预览帧过大");
                return stream.ToArray();
            }
            finally
            {
                pin.Free();
            }
        }

        private sealed class RetainedSRV : IDalamudTextureWrap
        {
            private nint pointer;

            public RetainedSRV(nint pointer, int width, int height)
            {
                Marshal.AddRef(pointer);
                this.pointer = pointer;
                Width = width;
                Height = height;
            }

            public ImTextureID Handle => new((ulong)pointer);
            public int Width { get; }
            public int Height { get; }
            public Vector2 Size => new(Width, Height);

            public IDalamudTextureWrap CreateWrapSharingLowLevelResource()
            {
                if (pointer == 0)
                    throw new ObjectDisposedException(nameof(RetainedSRV));
                return new RetainedSRV(pointer, Width, Height);
            }

            public void Dispose()
            {
                var resource = Interlocked.Exchange(ref pointer, 0);
                if (resource != 0)
                    Marshal.Release(resource);
            }
        }
    }
}