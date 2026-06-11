using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using Xunit;

namespace BlenderSuite.RenderQueue.Tests.Services.Business.Blender;

public sealed class BlenderPythonSafetyTests
{
    [Fact]
    public void PythonScriptLiteral_EscapesPathsWithoutBreakingPythonString()
    {
        var literal = PythonScriptLiteral.FromString("C:\\tmp\\O'Brien\\frame \"A\"\n.blend");

        Assert.Equal("\"C:\\\\tmp\\\\O'Brien\\\\frame \\\"A\\\"\\n.blend\"", literal);
    }

    [Fact]
    public void BlenderQueryService_GeneratesSafeFilepathLiteral()
    {
        var script = GetFilePropertiesScript("C:\\tmp\\O'Brien\\file.blend");

        Assert.Contains("filepath = \"C:\\\\tmp\\\\O'Brien\\\\file.blend\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("filepath = '", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlenderCommandService_GeneratesSafePathAndSceneLiterals()
    {
        var process = new CapturingBlenderProcess();
        var sut = new BlenderCommandService();

        await sut.StartRenderAsync(
            process,
            "C:\\tmp\\O'Brien\\file.blend",
            animation: true,
            sceneName: "Scene 'A'",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("filepath = \"C:\\\\tmp\\\\O'Brien\\\\file.blend\"", process.Script, StringComparison.Ordinal);
        Assert.Contains("bpy.data.scenes[\"Scene 'A'\"]", process.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void BlenderVideoService_GeneratesSafePathAndCodecLiterals()
    {
        var script = GenerateVideoScript(
            ["C:\\renders\\O'Brien\\frame_0001.png"],
            "C:\\renders\\O'Brien\\output.mp4");

        Assert.Contains("name=\"frame_0001\"", script, StringComparison.Ordinal);
        Assert.Contains("filepath=\"C:/renders/O'Brien/frame_0001.png\"", script, StringComparison.Ordinal);
        Assert.Contains("bpy.context.scene.render.filepath = \"C:/renders/O'Brien/output.mp4\"", script, StringComparison.Ordinal);
        Assert.Contains("if os.path.exists(\"C:/renders/O'Brien/output.mp4\"):", script, StringComparison.Ordinal);
        Assert.Contains("codec = \"H264\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("filepath='", script, StringComparison.Ordinal);
        Assert.DoesNotContain("filepath = '", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BlenderVideoService_EscapesQuotesInPaths()
    {
        var script = GenerateVideoScript(
            ["C:\\renders\\say \"hi\"\\frame_0001.png"],
            "C:\\renders\\say \"hi\"\\output.mp4");

        Assert.Contains("filepath=\"C:/renders/say \\\"hi\\\"/frame_0001.png\"", script, StringComparison.Ordinal);
        Assert.Contains("bpy.context.scene.render.filepath = \"C:/renders/say \\\"hi\\\"/output.mp4\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseBlenderProcess_EncodesConsoleScriptBeforeExecution()
    {
        var command = TestBlenderProcess.BuildScript("print('''sentinel breaker''')", "DONE");

        Assert.Contains("base64.b64decode", command, StringComparison.Ordinal);
        Assert.Contains("print('DONE')", command, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel breaker", command, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRenderOutput_RejectsStaleAnimationDirectoryContents()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var staleFrame = Path.Combine(directory, "frame_0001.png");
            File.WriteAllText(staleFrame, "old");
            File.SetLastWriteTimeUtc(staleFrame, DateTime.UtcNow.AddMinutes(-10));

            var verified = VerifyRenderOutput(
                new BlenderWorkerRequest
                {
                    BlendFilePath = "/tmp/scene.blend",
                    Animation = true,
                    FrameStart = 1,
                    FrameEnd = 1,
                    OutputPath = Path.Combine(directory, "frame_####.png")
                },
                new BlenderWorkerResponse
                {
                    Ok = true,
                    OutputVerified = true,
                    RenderStartedAt = DateTimeOffset.UtcNow.ToString("O")
                });

            Assert.False(verified);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void VerifyRenderOutput_AcceptsFreshAnimationResponseOutput()
    {
        using var frame = TemporaryFile.Create(".png");
        File.SetLastWriteTimeUtc(frame.Path, DateTime.UtcNow);

        var verified = VerifyRenderOutput(
            new BlenderWorkerRequest
            {
                BlendFilePath = "/tmp/scene.blend",
                Animation = true,
                FrameStart = 1,
                FrameEnd = 1
            },
            new BlenderWorkerResponse
            {
                Ok = true,
                OutputVerified = true,
                OutputPath = frame.Path,
                RenderStartedAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O")
            });

        Assert.True(verified);
    }

    private static string GetFilePropertiesScript(string blendFilePath)
    {
        var method = typeof(BlenderQueryService).GetMethod(
            "GetFilePropertiesScript",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(new BlenderQueryService(), [blendFilePath]));
    }

    private static string GenerateVideoScript(string[] imageFiles, string outputVideoPath)
    {
        var method = typeof(BlenderVideoService).GetMethod(
            "GenerateVideoScript",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [imageFiles, outputVideoPath, 24.0, "H264", "HIGH", 1920, 1080]));
    }

    private static bool VerifyRenderOutput(BlenderWorkerRequest request, BlenderWorkerResponse response)
    {
        using var host = new PythonConsoleWorkerHost();
        var method = typeof(PythonConsoleWorkerHost).GetMethod(
            "VerifyRenderOutput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method.Invoke(host, [request, response]));
    }

    private sealed class CapturingBlenderProcess : IBlenderProcess
    {
        public string Script { get; private set; } = string.Empty;
        public string ProcessId { get; } = "capture";
        public BlenderProcessType ProcessType => BlenderProcessType.Render;
        public string BlenderPath => "/tmp/blender";
        public bool IsRunning => true;
        public bool IsDisposed => false;
        public event Action<string>? OnOutputReceived;
        public event Action<string>? OnErrorReceived;
        public event Action<int>? OnProcessExited;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            Script = script;
            return Task.FromResult(string.Empty);
        }

        public void Dispose()
        {
        }

        public void KeepEventsAlive()
        {
            OnOutputReceived?.Invoke(string.Empty);
            OnErrorReceived?.Invoke(string.Empty);
            OnProcessExited?.Invoke(0);
        }
    }

    private sealed class TestBlenderProcess : BaseBlenderProcess
    {
        private TestBlenderProcess()
            : base("/tmp/blender", BlenderProcessConfig.CreateQueryConfig())
        {
        }

        public override BlenderProcessType ProcessType => BlenderProcessType.Query;

        public static string BuildScript(string script, string sentinel)
        {
            return BuildConsoleExecScript(script, sentinel);
        }
    }
}
